using BurnDisc.Infrastructure;
using BurnDisc.Model;
using BurnDisc.Ui;
using Spectre.Console;

namespace BurnDisc.Tests;

public sealed class LibraryDashboardRenderTests {
    // BuildFrame touches only render state (never the injected services), so
    // null deps are safe here — this exercises frame assembly and, crucially,
    // markup escaping of titles containing '[' , '(' and other Spectre markup.
    private static LibraryDashboard NewDashboard(IBurnHistory? history = null) =>
        new(scanner: null!, driveScanner: null!, preparer: null!, burner: null!,
            processRunner: null!, dependencies: null!, platformDetector: null!,
            history: history ?? new FakeHistory(), config: new LibraryConfig());

    private sealed class FakeHistory : IBurnHistory {
        private readonly Dictionary<DiscFingerprint, string> m_map = [];
        public string? Lookup(DiscFingerprint fingerprint) => m_map.GetValueOrDefault(fingerprint);
        public void Record(DiscFingerprint fingerprint, string title) => m_map[fingerprint] = title;
    }

    private static string Render(LibraryDashboard dashboard) {
        StringWriter sink = new();
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(sink)
        });
        console.Profile.Width = 200;
        console.Profile.Height = 40;
        console.Write(dashboard.BuildFrame());
        return sink.ToString();
    }

    [Fact]
    public void BuildFrame_PlatformMenu_RendersPlatformNames() {
        LibraryDashboard dashboard = NewDashboard();
        dashboard.SeedForTest([
            new LibraryItem("Sonic CD (USA)", ELibrarySource.Local, "/roms/Sega CD/Sonic CD (USA).7z", 415_875_370, EPlatform.SegaCd),
            new LibraryItem("Nights", ELibrarySource.Local, "/roms/Saturn/Nights.chd", 500_000_000, EPlatform.Saturn),
        ]);

        string output = Render(dashboard);

        Assert.Contains("burn-disc", output);
        Assert.Contains("Sega CD", output);
        Assert.Contains("Saturn", output);
        Assert.Contains("title", output); // "N title(s)" count column
    }

    [Fact]
    public void BuildFrame_TitleList_RendersTitlesWithMarkupCharacters_WithoutThrowing() {
        LibraryDashboard dashboard = NewDashboard();
        // Both under one platform; open it, then the selected row renders a title
        // with markup characters ('[' / '(') that must be escaped, not parsed.
        dashboard.SeedForTest([
            new LibraryItem("Sonic CD (USA)", ELibrarySource.Local, "/roms/Sega CD/Sonic CD (USA).7z", 415_875_370, EPlatform.SegaCd),
            new LibraryItem("Snatcher [proto]", ELibrarySource.Server, "Snatcher [proto].7z", 463_654_039, EPlatform.SegaCd),
        ]);
        dashboard.HandleKeyForTest(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false));

        string output = Render(dashboard);

        Assert.Contains("Sonic CD (USA)", output);
        Assert.Contains("Snatcher [proto]", output);
        Assert.Contains("Library", output); // breadcrumb
    }

    [Fact]
    public void BuildFrame_ShowsBurnedTitle_OverGenericDiscLabel() {
        LibraryDashboard dashboard = NewDashboard();
        // Disc's own label is generic; we burned it, so show what we burned.
        dashboard.SetDriveForTest(new OpticalDrive("ASUS", "SDRW", "CD-ROM", [10], isBlank: false, usedBytes: 358_500_000, volumeLabel: "Audio CD"));
        dashboard.SetLastBurnedForTest("Heart of the Alien - Out of This World Parts I and II (USA)");

        string output = Render(dashboard);

        Assert.Contains("Heart of the Alien", output);
        Assert.DoesNotContain("Audio CD", output);
    }

    [Fact]
    public void BuildFrame_ResolvesTitleFromBurnHistory_ForJunkLabelledDisc() {
        FakeHistory history = new();
        history.Record(new DiscFingerprint(42, 358), "Heart of the Alien - Out of This World Parts I and II (USA)");
        LibraryDashboard dashboard = NewDashboard(history);
        // Disc's own label is the placeholder "TEST"; history should win.
        dashboard.SetDriveForTest(new OpticalDrive("ASUS", "SDRW", "CD-ROM", [10],
            isBlank: false, usedBytes: 358L * 1024 * 1024, volumeLabel: "TEST", trackCount: 42));

        string output = Render(dashboard);

        Assert.Contains("Heart of the Alien", output);
        Assert.DoesNotContain("TEST", output);
    }

    [Fact]
    public void BuildFrame_JunkLabelWithNoHistory_ShowsNoIdentity() {
        LibraryDashboard dashboard = NewDashboard();
        dashboard.SetDriveForTest(new OpticalDrive("ASUS", "SDRW", "CD-ROM", [10],
            isBlank: false, usedBytes: 358L * 1024 * 1024, volumeLabel: "TEST", trackCount: 42));

        string output = Render(dashboard);

        Assert.Contains("has data", output);
        Assert.DoesNotContain("TEST", output); // placeholder label is suppressed, not shown as a title
    }

    [Fact]
    public void BuildFrame_EmptyLibrary_RendersPlaceholder() {
        LibraryDashboard dashboard = NewDashboard();
        dashboard.SeedForTest([]);
        Assert.Contains("scanning for titles", Render(dashboard));
    }
}
