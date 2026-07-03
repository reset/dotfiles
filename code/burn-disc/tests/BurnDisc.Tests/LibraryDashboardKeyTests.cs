using BurnDisc.Infrastructure;
using BurnDisc.Model;
using BurnDisc.Ui;

namespace BurnDisc.Tests;

//
// Guards the macOS key dispatch: Console.ReadKey there leaves Key == None for
// punctuation and letters, so commands must be matched on KeyChar. These feed
// KeyChar-only ConsoleKeyInfo values (Key == None) the way a Mac terminal does.
//
public sealed class LibraryDashboardKeyTests {
    private static LibraryDashboard NewDashboard() =>
        new(scanner: null!, driveScanner: null!, preparer: null!, burner: null!,
            processRunner: null!, dependencies: null!, platformDetector: null!, config: new LibraryConfig());

    private static ConsoleKeyInfo Char(char c) => new(c, ConsoleKey.None, shift: false, alt: false, control: false);
    private static ConsoleKeyInfo Enter() => new('\r', ConsoleKey.Enter, shift: false, alt: false, control: false);
    private static OpticalDrive Drive(bool? isBlank) => new("ASUS", "SDRW", "CD-R", [10], isBlank, usedBytes: isBlank == false ? 100_000 : 0);
    private static OpticalDrive NoDisc() => new("ASUS", "SDRW", mediaType: null, [10], isBlank: null, usedBytes: 0);
    private static ConsoleKeyInfo CtrlC() => new('\u0003', ConsoleKey.C, shift: false, alt: false, control: true);

    private static LibraryDashboard Seeded() {
        LibraryDashboard dashboard = NewDashboard();
        dashboard.SeedForTest([
            new LibraryItem("Final Fight CD", ELibrarySource.Local, "/roms/Final Fight CD.7z", 100),
            new LibraryItem("Sonic CD", ELibrarySource.Local, "/roms/Sonic CD.7z", 100),
            new LibraryItem("Snatcher", ELibrarySource.Local, "/roms/Snatcher.7z", 100),
        ]);
        return dashboard;
    }

    [Fact]
    public void Slash_AsKeyCharWithKeyNone_EntersSearch() {
        LibraryDashboard dashboard = Seeded();
        Assert.False(dashboard.InSearchModeForTest);

        dashboard.HandleKeyForTest(Char('/'));

        Assert.True(dashboard.InSearchModeForTest);
    }

    [Fact]
    public void JAndK_AsKeyChar_MoveCursor() {
        LibraryDashboard dashboard = Seeded();

        dashboard.HandleKeyForTest(Char('j'));
        Assert.Equal(1, dashboard.CursorForTest);

        dashboard.HandleKeyForTest(Char('j'));
        Assert.Equal(2, dashboard.CursorForTest);

        dashboard.HandleKeyForTest(Char('k'));
        Assert.Equal(1, dashboard.CursorForTest);
    }

    [Fact]
    public void Q_PromptsConfirm_ThenYQuits() {
        LibraryDashboard dashboard = Seeded();

        dashboard.HandleKeyForTest(Char('q'));
        Assert.True(dashboard.InConfirmQuitForTest);
        Assert.False(dashboard.QuitRequestedForTest); // not yet — awaiting confirmation

        dashboard.HandleKeyForTest(Char('y'));
        Assert.True(dashboard.QuitRequestedForTest);
    }

    [Fact]
    public void Q_ThenAnythingElse_CancelsQuit() {
        LibraryDashboard dashboard = Seeded();

        dashboard.HandleKeyForTest(Char('q'));
        dashboard.HandleKeyForTest(Char('n'));

        Assert.False(dashboard.QuitRequestedForTest);
        Assert.False(dashboard.InConfirmQuitForTest); // back to browsing
    }

    [Fact]
    public void Enter_WithNonBlankDisc_AsksBurnConfirmation() {
        LibraryDashboard dashboard = Seeded();
        dashboard.SetDriveForTest(Drive(isBlank: false));

        dashboard.HandleKeyForTest(Enter());

        Assert.True(dashboard.InConfirmBurnForTest);
        Assert.False(dashboard.InBurningForTest); // held until confirmed
    }

    [Fact]
    public void BurnConfirm_N_CancelsBackToBrowse() {
        LibraryDashboard dashboard = Seeded();
        dashboard.SetDriveForTest(Drive(isBlank: false));

        dashboard.HandleKeyForTest(Enter());
        dashboard.HandleKeyForTest(Char('n'));

        Assert.False(dashboard.InConfirmBurnForTest);
        Assert.False(dashboard.InBurningForTest);
    }

    [Fact]
    public void Enter_WithNoDisc_RefusesAndNotifies() {
        LibraryDashboard dashboard = Seeded();
        dashboard.SetDriveForTest(NoDisc());

        dashboard.HandleKeyForTest(Enter());

        Assert.False(dashboard.InConfirmBurnForTest);
        Assert.False(dashboard.InBurningForTest); // never starts a burn with no disc
        Assert.NotNull(dashboard.NoticeForTest);
    }

    [Fact]
    public void Enter_WithBlankDisc_SkipsConfirmation() {
        LibraryDashboard dashboard = Seeded();
        dashboard.SetDriveForTest(Drive(isBlank: true));

        dashboard.HandleKeyForTest(Enter());

        // Blank disc → no confirmation gate (proceeds straight to the burn).
        Assert.False(dashboard.InConfirmBurnForTest);
    }

    [Fact]
    public void E_TriggersEject() {
        LibraryDashboard dashboard = Seeded();
        Assert.False(dashboard.EjectingForTest);

        dashboard.HandleKeyForTest(Char('e'));

        Assert.True(dashboard.EjectingForTest);
    }

    [Fact]
    public void CtrlC_WhileBrowsing_Quits() {
        LibraryDashboard dashboard = Seeded();
        dashboard.HandleKeyForTest(CtrlC());
        Assert.True(dashboard.QuitRequestedForTest);
    }

    [Fact]
    public void Escape_MirrorsQ_PromptingQuitConfirm() {
        LibraryDashboard dashboard = Seeded();
        dashboard.HandleKeyForTest(new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, shift: false, alt: false, control: false));
        Assert.True(dashboard.InConfirmQuitForTest);
    }

    [Fact]
    public void CtrlC_WhileBurning_IsIgnored() {
        LibraryDashboard dashboard = Seeded();
        dashboard.EnterBurningModeForTest();

        dashboard.HandleKeyForTest(CtrlC());

        Assert.False(dashboard.QuitRequestedForTest); // never interrupt a burn
    }

    [Fact]
    public void TypingInSearch_FiltersLive() {
        LibraryDashboard dashboard = Seeded();
        dashboard.HandleKeyForTest(Char('/'));

        foreach (char c in "son") {
            dashboard.HandleKeyForTest(Char(c));
        }

        IReadOnlyList<LibraryItem> filtered = dashboard.FilteredForTest();
        Assert.Single(filtered);
        Assert.Equal("Sonic CD", filtered[0].DisplayName);
    }
}
