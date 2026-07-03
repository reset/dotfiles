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
    public void CtrlC_WhileBrowsing_Quits() {
        LibraryDashboard dashboard = Seeded();
        dashboard.HandleKeyForTest(CtrlC());
        Assert.True(dashboard.QuitRequestedForTest);
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
