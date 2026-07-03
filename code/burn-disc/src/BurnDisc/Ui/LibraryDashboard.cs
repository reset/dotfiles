using System.Text.RegularExpressions;
using BurnDisc.Infrastructure;
using BurnDisc.Model;
using BurnDisc.Pipeline;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace BurnDisc.Ui;

//
// Full-screen, alternate-screen library browser. Scans local (~/roms) and the
// media server, lets the operator navigate/search with vim keys, and burns the
// selected title — server titles download first — drawing the extract/convert/
// burn progress inside the same live frame.
//
// The dashboard is its own IProgressScope: the burn runs on a background task
// that writes phase state here under a lock, and the render loop paints it.
//
internal sealed partial class LibraryDashboard : IProgressScope, IDisposable {
    private enum EMode { Browse, Search, Burning, Result, ConfirmQuit, ConfirmBurn, ConfirmAbort }

    private const int PollIntervalMs = 50;
    private const int DriveRescanTicks = 60; // ~3s at the poll interval

    private readonly ILibraryScanner m_scanner;
    private readonly IDriveScanner m_driveScanner;
    private readonly IImagePreparer m_preparer;
    private readonly IBurner m_burner;
    private readonly IProcessRunner m_processRunner;
    private readonly IDependencyChecker m_dependencies;
    private readonly IPlatformDetector m_platformDetector;
    private readonly LibraryConfig m_config;

    private readonly object m_sync = new();

    // Shared state (guarded by m_sync where touched off the render loop).
    private List<LibraryItem> m_all = [];
    private bool m_serverLoading = true;
    private readonly List<PhaseState> m_phases = [];
    private readonly List<string> m_platformLines = [];
    private string? m_burnLog;
    private EMode m_mode = EMode.Browse;
    private bool m_resultOk;
    private string? m_resultMessage;

    // Render-loop-only state.
    private EPlatform? m_selectedPlatform; // null = at the top-level platform menu
    private string m_filter = "";
    private int m_cursor;
    private int m_scroll;
    private int m_visibleRows = 10;
    private bool m_pendingG;
    private bool m_quit;
    private OpticalDrive? m_drive;
    private Task<IReadOnlyList<LibraryItem>>? m_serverScan;
    private Task? m_driveScan;
    private int m_scanTicks; // throttles the idle drive re-scan
    private Task? m_burnTask;
    private CancellationTokenSource? m_burnCts;
    private LibraryItem? m_pendingBurn; // awaiting non-blank-disc confirmation
    private Task? m_ejectTask;
    private bool m_ejecting;
    private string? m_notice; // transient browse-line message (e.g. "no disc")

    public LibraryDashboard(
        ILibraryScanner scanner, IDriveScanner driveScanner, IImagePreparer preparer,
        IBurner burner, IProcessRunner processRunner, IDependencyChecker dependencies,
        IPlatformDetector platformDetector, LibraryConfig config) {
        m_scanner = scanner;
        m_driveScanner = driveScanner;
        m_preparer = preparer;
        m_burner = burner;
        m_processRunner = processRunner;
        m_dependencies = dependencies;
        m_platformDetector = platformDetector;
        m_config = config;
    }

    //
    // Entry point
    //
    public async Task<int> RunAsync(CancellationToken cancellationToken) {
        bool interactive = !Console.IsInputRedirected && !Console.IsOutputRedirected;
        if (!interactive) {
            Console.Error.WriteLine("burn-disc: the library browser needs an interactive terminal. Pass a file to burn directly, or use --dry-run.");
            return 1;
        }

        m_all = [.. m_scanner.ScanLocal()];
        m_serverScan = Task.Run(() => m_scanner.ScanServerAsync(cancellationToken), cancellationToken);
        m_driveScan = Task.Run(async () => m_drive = await m_driveScanner.ScanAsync(cancellationToken).ConfigureAwait(false), cancellationToken);

        // Trap Ctrl-C: deliver it to the key loop as input rather than letting it
        // SIGINT the process (and the child cdrdao) mid-burn. Restored on exit.
        bool originalTreatCtrlC = Console.TreatControlCAsInput;
        Console.TreatControlCAsInput = true;
        try {
            using AlternateScreen screen = AlternateScreen.Enter(enabled: true);

            await AnsiConsole.Live(BuildFrame())
                .AutoClear(false)
                .Overflow(VerticalOverflow.Ellipsis)
                .StartAsync(async ctx => {
                    ctx.Refresh();
                    while (!m_quit && !cancellationToken.IsCancellationRequested) {
                        m_visibleRows = Math.Max(3, TerminalHeight() - 8);
                        HandleKeys();
                        AdoptServerScan();
                        AdoptEject();
                        MaybeRescanDrive();
                        ctx.UpdateTarget(BuildFrame());
                        ctx.Refresh();
                        try {
                            await Task.Delay(PollIntervalMs, cancellationToken).ConfigureAwait(false);
                        } catch (OperationCanceledException) {
                            break;
                        }
                    }
                }).ConfigureAwait(false);
        } finally {
            Console.TreatControlCAsInput = originalTreatCtrlC;
        }

        return 0;
    }

    private void AdoptServerScan() {
        if (m_serverScan is { IsCompleted: true }) {
            try {
                IReadOnlyList<LibraryItem> serverItems = m_serverScan.GetAwaiter().GetResult();
                lock (m_sync) {
                    m_all.AddRange(serverItems);
                    m_serverLoading = false;
                }
            } catch {
                lock (m_sync) {
                    m_serverLoading = false;
                }
            }
            m_serverScan = null;
        }
    }

    //
    // Input
    //
    private void HandleKeys() {
        while (Console.KeyAvailable) {
            DispatchKey(Console.ReadKey(intercept: true));
        }
    }

    // Routes one keypress by mode. Internal so the Ctrl-C and quit-confirm logic
    // is unit-tested without a terminal.
    internal void DispatchKey(ConsoleKeyInfo key) {
        EMode mode;
        lock (m_sync) {
            mode = m_mode;
        }

        // Ctrl-C is trapped (Console.TreatControlCAsInput) so it can't SIGINT a
        // running cdrdao. While a burn is active it routes to the abort confirm
        // (quitting would orphan cdrdao); idle, it's a clean quit.
        if (IsCtrlC(key)) {
            if (mode is EMode.Burning or EMode.ConfirmAbort) {
                SetMode(EMode.ConfirmAbort);
            } else {
                m_quit = true;
            }
            return;
        }

        switch (mode) {
            case EMode.Search:
                HandleSearchKey(key);
                break;
            case EMode.ConfirmQuit:
                if (IsYes(key)) {
                    m_quit = true;
                } else {
                    SetMode(EMode.Browse);
                }
                break;
            case EMode.ConfirmBurn:
                if (IsYes(key)) {
                    LibraryItem? pending = m_pendingBurn;
                    m_pendingBurn = null;
                    if (pending is not null) {
                        StartBurn(pending);
                    }
                } else {
                    m_pendingBurn = null;
                    SetMode(EMode.Browse);
                }
                break;
            case EMode.ConfirmAbort:
                if (IsYes(key)) {
                    m_burnCts?.Cancel();
                    Log("Aborting…");
                }
                SetMode(EMode.Burning); // 'n'/anything resumes; the task flips to Result once cancelled
                break;
            case EMode.Result:
                if (key.KeyChar == 'q' || key.Key == ConsoleKey.Q) {
                    m_quit = true;
                } else {
                    SetMode(EMode.Browse);
                }
                break;
            case EMode.Burning:
                // x or Escape abort the burn (via confirm); other keys ignored.
                if (key.KeyChar is 'x' or 'X' || key.Key == ConsoleKey.Escape) {
                    SetMode(EMode.ConfirmAbort);
                }
                break;
            default:
                HandleBrowseKey(key);
                break;
        }
    }

    private static bool IsYes(ConsoleKeyInfo key) => key.KeyChar is 'y' or 'Y' || key.Key == ConsoleKey.Y;

    private static bool IsCtrlC(ConsoleKeyInfo key) =>
        key.KeyChar == '\u0003' || (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control));

    // Test seams for the key dispatch (the macOS KeyChar/Key.None case, quit
    // confirmation, and Ctrl-C handling).
    internal bool InSearchModeForTest { get { lock (m_sync) { return m_mode == EMode.Search; } } }
    internal bool InConfirmQuitForTest { get { lock (m_sync) { return m_mode == EMode.ConfirmQuit; } } }
    internal bool InConfirmBurnForTest { get { lock (m_sync) { return m_mode == EMode.ConfirmBurn; } } }
    internal bool InBurningForTest { get { lock (m_sync) { return m_mode == EMode.Burning; } } }
    internal bool QuitRequestedForTest => m_quit;
    internal bool EjectingForTest => m_ejecting;
    internal bool InConfirmAbortForTest { get { lock (m_sync) { return m_mode == EMode.ConfirmAbort; } } }
    internal string? NoticeForTest => m_notice;
    internal int CursorForTest => m_cursor;
    internal bool AtPlatformListForTest => m_selectedPlatform is null;
    internal IReadOnlyList<LibraryItem> FilteredForTest() => CurrentTitles();
    internal void EnterBurningModeForTest() { lock (m_sync) { m_mode = EMode.Burning; } }
    internal void SetDriveForTest(OpticalDrive? drive) => m_drive = drive;
    internal void HandleKeyForTest(ConsoleKeyInfo key) => DispatchKey(key);

    private void HandleBrowseKey(ConsoleKeyInfo key) {
        int count = CurrentListCount();
        int page = Math.Max(1, m_visibleRows - 1);
        char c = key.KeyChar;
        bool atPlatformList = m_selectedPlatform is null;

        m_notice = null; // any keypress clears a stale notice; RequestBurn may re-set it

        // A lone 'g' arms go-to-top; any other key cancels the pending sequence.
        if (c != 'g') {
            m_pendingG = false;
        }

        // Arrows and Enter map reliably through Key on every platform.
        switch (key.Key) {
            case ConsoleKey.DownArrow:
                MoveCursor(1, count);
                return;
            case ConsoleKey.UpArrow:
                MoveCursor(-1, count);
                return;
            case ConsoleKey.Enter:
                if (count > 0) {
                    Activate(Math.Clamp(m_cursor, 0, count - 1));
                }
                return;
            case ConsoleKey.Escape:
                // Escape backs out one level: clear an active filter, else leave the
                // platform (back to the top-level menu), else quit.
                if (m_filter.Length > 0) {
                    m_filter = "";
                    m_cursor = 0;
                    m_scroll = 0;
                } else if (!atPlatformList) {
                    SelectPlatform(null);
                } else {
                    SetMode(EMode.ConfirmQuit);
                }
                return;
            default:
                break;
        }

        // Character commands are matched on KeyChar: on macOS/Unix, ReadKey
        // leaves ConsoleKeyInfo.Key as None for punctuation (and often letters),
        // so '/' and friends only come through reliably as KeyChar.
        switch (c) {
            case 'j':
                MoveCursor(1, count);
                break;
            case 'k':
                MoveCursor(-1, count);
                break;
            case 'd':
                MoveCursor(page, count);
                break;
            case 'u':
                MoveCursor(-page, count);
                break;
            case 'G':
                MoveCursor(count, count); // clamps to the last row
                break;
            case 'g':
                if (m_pendingG) {
                    m_cursor = 0;
                    m_pendingG = false;
                } else {
                    m_pendingG = true;
                }
                break;
            case '/':
                if (!atPlatformList) {
                    SetMode(EMode.Search); // search applies within a platform's titles
                }
                break;
            case 'e':
                StartEject();
                break;
            case 'q':
                SetMode(EMode.ConfirmQuit);
                break;
            default:
                break;
        }
    }

    // Opens the highlighted platform, or burns the highlighted title.
    private void Activate(int index) {
        if (m_selectedPlatform is null) {
            IReadOnlyList<(EPlatform Platform, int Count)> groups = LibraryView.GroupByPlatform(Snapshot());
            if (index < groups.Count) {
                SelectPlatform(groups[index].Platform);
            }
        } else {
            IReadOnlyList<LibraryItem> titles = CurrentTitles();
            if (index < titles.Count) {
                RequestBurn(titles[index]);
            }
        }
    }

    private void SelectPlatform(EPlatform? platform) {
        m_selectedPlatform = platform;
        m_filter = "";
        m_cursor = 0;
        m_scroll = 0;
    }

    private int CurrentListCount() =>
        m_selectedPlatform is null ? LibraryView.GroupByPlatform(Snapshot()).Count : CurrentTitles().Count;

    private List<LibraryItem> Snapshot() {
        lock (m_sync) {
            return [.. m_all];
        }
    }

    // Titles in the selected platform, narrowed by the active search filter.
    private IReadOnlyList<LibraryItem> CurrentTitles() {
        if (m_selectedPlatform is not { } platform) {
            return [];
        }
        List<LibraryItem> inPlatform = Snapshot().Where(i => i.Platform == platform).ToList();
        return LibraryView.Filter(inPlatform, m_filter);
    }

    private void MoveCursor(int delta, int count) {
        m_cursor = LibraryView.Clamp(m_cursor + delta, 0, Math.Max(0, count - 1));
    }

    public void Dispose() => m_burnCts?.Dispose();

    private void HandleSearchKey(ConsoleKeyInfo key) {
        char c = key.KeyChar;

        // Enter applies the filter; Escape clears it. Both map reliably via Key,
        // with a KeyChar fallback for terminals that only fill KeyChar.
        if (key.Key == ConsoleKey.Enter || c is '\r' or '\n') {
            SetMode(EMode.Browse);
            return;
        }
        if (key.Key == ConsoleKey.Escape || c == '\x1b') {
            m_filter = "";
            SetMode(EMode.Browse);
            m_cursor = 0;
            m_scroll = 0;
            return;
        }

        if (key.Key == ConsoleKey.Backspace || c is '\b' or '\x7f') {
            if (m_filter.Length > 0) {
                m_filter = m_filter[..^1];
            }
        } else if (!char.IsControl(c)) {
            m_filter += c;
        }

        m_cursor = 0;
        m_scroll = 0;
    }

    //
    // Burn
    //
    // Eject the disc (drutil unmounts and opens the tray), then re-scan so the
    // drive line reflects the now-empty drive. Runs off the loop; ignored if an
    // eject is already in flight.
    private void StartEject() {
        if (m_ejectTask is not null) {
            return;
        }
        m_ejecting = true;
        m_ejectTask = Task.Run(async () => {
            try {
                _ = await m_processRunner.RunAsync("drutil", ["eject"]).ConfigureAwait(false);
                m_drive = await m_driveScanner.ScanAsync().ConfigureAwait(false);
            } catch {
                // Eject is best-effort; a failure just leaves the drive line as-is.
            }
        });
    }

    private void AdoptEject() {
        if (m_ejectTask is { IsCompleted: true }) {
            m_ejecting = false;
            m_ejectTask = null;
        }
    }

    // Periodically re-scan the drive while idle so the media line stays current
    // as discs are inserted, burned, or ejected. Skipped mid-burn (the drive is
    // busy being written) and while an eject is in flight.
    private void MaybeRescanDrive() {
        EMode mode;
        lock (m_sync) {
            mode = m_mode;
        }
        if (mode is EMode.Burning or EMode.ConfirmAbort || m_ejecting) {
            m_scanTicks = 0;
            return;
        }
        if (++m_scanTicks < DriveRescanTicks) {
            return;
        }
        m_scanTicks = 0;
        if (m_driveScan is null or { IsCompleted: true }) {
            m_driveScan = Task.Run(async () => {
                try {
                    m_drive = await m_driveScanner.ScanAsync().ConfigureAwait(false);
                } catch {
                    // transient scan failure — keep the last known state
                }
            });
        }
    }

    // Guards before a burn: refuse with no disc loaded, confirm on a non-blank
    // disc (guaranteed coaster), otherwise proceed.
    private void RequestBurn(LibraryItem item) {
        if (m_drive is { MediaType: null }) {
            m_notice = "No disc in the drive — insert a blank CD-R.";
            return;
        }
        if (m_drive?.IsBlank == false) {
            m_pendingBurn = item;
            SetMode(EMode.ConfirmBurn);
        } else {
            StartBurn(item);
        }
    }

    private void StartBurn(LibraryItem item) {
        lock (m_sync) {
            m_phases.Clear();
            m_platformLines.Clear();
            m_burnLog = null;
            m_resultMessage = null;
            m_mode = EMode.Burning;
        }
        m_burnCts?.Dispose();
        m_burnCts = new CancellationTokenSource();
        int? speed = m_drive?.MinWriteSpeed;
        m_burnTask = Task.Run(() => BurnItemAsync(item, speed, m_burnCts.Token));
    }

    private async Task BurnItemAsync(LibraryItem item, int? speed, CancellationToken cancellationToken) {
        string workDir = Directory.CreateTempSubdirectory("burn-disc-").FullName;
        try {
            string localFile;
            if (item.Source == ELibrarySource.Server) {
                localFile = Path.Combine(workDir, Path.GetFileName(item.Path));
                await DownloadAsync(item, localFile, cancellationToken).ConfigureAwait(false);
            } else {
                localFile = item.Path;
            }

            PreparedImage prepared = await m_preparer.PrepareAsync(localFile, workDir, this, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<string> platformLines = PlatformSummary.Lines(m_platformDetector.Detect(prepared.DataImagePath));
            lock (m_sync) {
                m_platformLines.Clear();
                m_platformLines.AddRange(platformLines);
            }
            Log(prepared.Describe());
            await m_burner.BurnAsync(prepared, speed, this, cancellationToken).ConfigureAwait(false);
            SetResult(ok: true, $"Burned {item.DisplayName}");
        } catch (OperationCanceledException) {
            SetResult(ok: false, "Burn aborted — the disc is a coaster.");
        } catch (Exception ex) {
            SetResult(ok: false, ex.Message);
        } finally {
            TryDeleteDirectory(workDir);
        }
    }

    private async Task DownloadAsync(LibraryItem item, string destination, CancellationToken cancellationToken) {
        m_dependencies.EnsureAvailable("rsync", "brew install rsync");
        IProgressTask task = AddTask($"Download {item.DisplayName}", 100);
        string remote = $"{m_config.MediaHost}:{m_config.MediaPath}/{item.Path}";

        void OnToken(string line) {
            Match m = Percent().Match(line);
            if (m.Success && double.TryParse(m.Groups[1].Value, out double pct)) {
                task.Value = pct;
            }
        }

        ProcessResult result = await m_processRunner.RunAsync(
            "rsync", ["-a", "--info=progress2", "--protect-args", remote, destination], onToken: OnToken, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded) {
            throw new ProcessException($"Download failed (exit {result.ExitCode}).\n{result.Output}");
        }
        task.Complete();
    }

    //
    // IProgressScope — called from the burn task
    //
    public IProgressTask AddTask(string description, double maxValue) {
        PhaseState state = new(description, Math.Max(maxValue, 1));
        lock (m_sync) {
            m_phases.Add(state);
        }
        return new PhaseTask(this, state);
    }

    public void Log(string message) {
        lock (m_sync) {
            m_burnLog = message;
        }
    }

    private void SetResult(bool ok, string message) {
        lock (m_sync) {
            m_resultOk = ok;
            m_resultMessage = message;
            m_mode = EMode.Result;
        }
    }

    private void SetMode(EMode mode) {
        lock (m_sync) {
            m_mode = mode;
        }
    }


    // Test seam: seed the item list so a render can be exercised headlessly.
    internal void SeedForTest(IReadOnlyList<LibraryItem> items, bool serverLoading = false) {
        lock (m_sync) {
            m_all = [.. items];
            m_serverLoading = serverLoading;
        }
    }

    //
    // Rendering
    //
    internal Panel BuildFrame() {
        EMode mode;
        int localCount, serverCount;
        bool serverLoading;
        List<PhaseState> phases;
        List<string> platformLines;
        string? burnLog;
        string? resultMessage;
        bool resultOk;
        lock (m_sync) {
            mode = m_mode;
            localCount = m_all.Count(static i => i.Source == ELibrarySource.Local);
            serverCount = m_all.Count(static i => i.Source == ELibrarySource.Server);
            serverLoading = m_serverLoading;
            phases = [.. m_phases.Select(p => p.Snapshot())];
            platformLines = [.. m_platformLines];
            burnLog = m_burnLog;
            resultMessage = m_resultMessage;
            resultOk = m_resultOk;
        }

        List<IRenderable> rows = [new Markup(StatusLine(localCount, serverCount, serverLoading, mode)), new Rule { Style = Style.Parse("grey15") }];

        switch (mode) {
            case EMode.Burning or EMode.ConfirmAbort:
                rows.AddRange(BurnBody(phases, platformLines, burnLog));
                break;
            case EMode.Result:
                rows.AddRange(ResultBody(resultOk, resultMessage));
                break;
            default:
                rows.AddRange(ListBody());
                break;
        }

        rows.Add(new Rule { Style = Style.Parse("grey15") });
        rows.Add(new Markup(FooterLine(mode, m_selectedPlatform is null)));

        return new Panel(new Rows(rows)) {
            Header = new PanelHeader($"[bold {Theme.Header}]burn-disc[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(foreground: Theme.Border),
            Expand = true,
            Padding = new Padding(1, 0, 1, 0)
        };
    }

    private string StatusLine(int localCount, int serverCount, bool serverLoading, EMode mode) {
        string server = serverLoading ? "[yellow]scanning server…[/]" : $"{serverCount} server";
        string library = $"[grey]Library[/]  [green]{localCount} local[/]  {server}";
        string driveState = m_ejecting ? "[yellow]ejecting…[/]" : m_drive is { } d ? $"[grey]{Markup.Escape(d.MediaSummary)}[/]" : "";
        string drive = m_drive is { } dd ? $"   [grey]·[/]  {Markup.Escape(dd.DisplayName)}  {driveState}" : m_ejecting ? $"   [grey]·[/]  {driveState}" : "";
        if (mode == EMode.Search) {
            return $"[grey]Search[/]  {Markup.Escape(m_filter)}[blink]▌[/]";
        }
        // Breadcrumb: library counts at the top level, the platform name once opened.
        string head = m_selectedPlatform is { } platform
            ? $"[grey]Library ›[/] {Markup.Escape(Platform.DisplayName(platform))}"
            : library;
        string notice = m_notice is { Length: > 0 } n ? $"   [red]! {Markup.Escape(n)}[/]" : "";
        return head + drive + notice;
    }

    private List<IRenderable> ListBody() =>
        m_selectedPlatform is null ? PlatformListBody() : TitleListBody();

    private List<IRenderable> PlatformListBody() {
        IReadOnlyList<(EPlatform Platform, int Count)> groups = LibraryView.GroupByPlatform(Snapshot());
        int count = groups.Count;
        m_cursor = LibraryView.Clamp(m_cursor, 0, Math.Max(0, count - 1));
        m_scroll = LibraryView.ScrollFor(count, m_cursor, m_visibleRows, m_scroll);

        int nameWidth = Math.Clamp(TerminalWidth() - 24, 12, 40);
        List<IRenderable> lines = [];
        if (count == 0) {
            lines.Add(new Markup("[grey]  (scanning for titles…)[/]"));
        }
        for (int i = 0; i < m_visibleRows; i++) {
            int index = m_scroll + i;
            if (index >= count) {
                lines.Add(new Text(""));
                continue;
            }
            lines.Add(new Markup(PlatformRowMarkup(groups[index], index == m_cursor, nameWidth)));
        }
        return lines;
    }

    private List<IRenderable> TitleListBody() {
        IReadOnlyList<LibraryItem> items = CurrentTitles();
        int count = items.Count;
        m_cursor = LibraryView.Clamp(m_cursor, 0, Math.Max(0, count - 1));
        m_scroll = LibraryView.ScrollFor(count, m_cursor, m_visibleRows, m_scroll);

        int nameWidth = Math.Clamp(TerminalWidth() - 24, 12, 60);
        List<IRenderable> lines = [];
        if (count == 0) {
            lines.Add(new Markup("[grey]  (no matching titles)[/]"));
        }
        for (int i = 0; i < m_visibleRows; i++) {
            int index = m_scroll + i;
            if (index >= count) {
                lines.Add(new Text(""));
                continue;
            }
            lines.Add(new Markup(RowMarkup(items[index], index == m_cursor, nameWidth)));
        }
        return lines;
    }

    private static string PlatformRowMarkup((EPlatform Platform, int Count) group, bool selected, int nameWidth) {
        string name = Truncate(Platform.DisplayName(group.Platform), nameWidth).PadRight(nameWidth);
        string count = $"{group.Count} title{(group.Count == 1 ? "" : "s")}";
        if (selected) {
            return $"[black on white]› {Markup.Escape(name)}  {count}[/]";
        }
        return $"  {Markup.Escape(name)}  [grey]{count}[/]";
    }

    private static string RowMarkup(LibraryItem item, bool selected, int nameWidth) {
        string name = Truncate(item.DisplayName, nameWidth).PadRight(nameWidth);
        string size = item.SizeDisplay.PadLeft(6);
        string src = item.SourceLabel.PadRight(6);
        if (selected) {
            return $"[black on white]› {Markup.Escape(name)}  {size}  {src}[/]";
        }
        return $"  {Markup.Escape(name)}  [grey]{size}[/]  [grey]{src}[/]";
    }

    private List<IRenderable> BurnBody(List<PhaseState> phases, List<string> platformLines, string? burnLog) {
        List<IRenderable> lines = [];
        foreach (string line in platformLines) {
            lines.Add(new Markup($"[grey]  {Markup.Escape(line)}[/]"));
        }
        if (platformLines.Count > 0) {
            lines.Add(new Text(""));
        }

        int barWidth = Math.Clamp(TerminalWidth() - 30, 10, 50);
        foreach (PhaseState phase in phases) {
            double pct = phase.Max > 0 ? phase.Value / phase.Max * 100 : 0;
            string colour = phase.Done ? "green" : "yellow";
            string bar = LibraryView.Bar(phase.Value, phase.Max, barWidth);
            lines.Add(new Markup($"  {Markup.Escape(Truncate(phase.Name, 22).PadRight(22))} [{colour}]{bar}[/] {pct,5:0}%"));
        }
        if (phases.Count == 0) {
            lines.Add(new Markup("[grey]  starting…[/]"));
        }
        lines.Add(new Text(""));
        if (burnLog is not null) {
            lines.Add(new Markup($"[grey]  {Markup.Escape(burnLog)}[/]"));
        }
        return PadTo(lines, m_visibleRows);
    }

    private List<IRenderable> ResultBody(bool ok, string? message) {
        List<IRenderable> lines = [
            new Text(""),
            ok
                ? new Markup($"  [green]✓ {Markup.Escape(message ?? "Done")}[/]")
                : new Markup($"  [red]✗ {Markup.Escape(message ?? "Failed")}[/]")
        ];
        return PadTo(lines, m_visibleRows);
    }

    private static string FooterLine(EMode mode, bool atPlatformList) => mode switch {
        EMode.Search => "[dim][[type]] filter  [[enter]] apply  [[esc]] clear[/]",
        EMode.Burning => "[dim]burning… please wait  ·  [[x]]/[[esc]] abort[/]",
        EMode.ConfirmAbort => "[yellow]Abort burn? This ruins the disc.[/]  [[y]] yes   [[n]] no",
        EMode.Result => "[dim][[any key]] back  [[q]] quit[/]",
        EMode.ConfirmQuit => "[yellow]Quit?[/]  [[y]] yes   [[n]] no",
        EMode.ConfirmBurn => "[yellow]! Disc is not blank — burn will likely coaster.[/]  [[y]] burn anyway   [[n]] cancel",
        _ => atPlatformList
            ? "[dim][[j/k]] move  [[enter]] open  [[e]] eject  [[q]] quit[/]"
            : "[dim][[j/k]] move  [[/]] search  [[enter]] burn  [[esc]] back  [[e]] eject  [[q]] quit[/]"
    };

    private static List<IRenderable> PadTo(List<IRenderable> lines, int target) {
        for (int i = lines.Count; i < target; i++) {
            lines.Add(new Text(""));
        }
        return lines;
    }

    [GeneratedRegex(@"(\d+)%")]
    private static partial Regex Percent();

    private static string Truncate(string value, int width) =>
        value.Length <= width ? value : value[..Math.Max(0, width - 1)] + "…";

    private static int TerminalWidth() {
        try {
            return Console.WindowWidth > 0 ? Console.WindowWidth : 100;
        } catch (IOException) {
            return 100;
        }
    }

    private static int TerminalHeight() {
        try {
            return Console.WindowHeight > 0 ? Console.WindowHeight : 30;
        } catch (IOException) {
            return 30;
        }
    }

    private static void TryDeleteDirectory(string dir) {
        try {
            if (Directory.Exists(dir)) {
                Directory.Delete(dir, recursive: true);
            }
        } catch (IOException) {
        } catch (UnauthorizedAccessException) {
        }
    }

    //
    // Progress state shared between the burn task and the render loop
    //
    private sealed class PhaseState {
        public PhaseState(string name, double max) {
            Name = name;
            Max = max;
        }

        public string Name { get; }
        public double Max { get; set; }
        public double Value { get; set; }
        public bool Done { get; set; }

        public PhaseState Snapshot() => new(Name, Max) { Value = Value, Done = Done };
    }

    private sealed class PhaseTask : IProgressTask {
        private readonly LibraryDashboard m_dashboard;
        private readonly PhaseState m_state;

        public PhaseTask(LibraryDashboard dashboard, PhaseState state) {
            m_dashboard = dashboard;
            m_state = state;
        }

        public double MaxValue {
            get { lock (m_dashboard.m_sync) { return m_state.Max; } }
            set { lock (m_dashboard.m_sync) { m_state.Max = Math.Max(value, 1); } }
        }

        public double Value {
            get { lock (m_dashboard.m_sync) { return m_state.Value; } }
            set { lock (m_dashboard.m_sync) { m_state.Value = value; } }
        }

        public void Complete() {
            lock (m_dashboard.m_sync) {
                m_state.Value = m_state.Max;
                m_state.Done = true;
            }
        }
    }
}
