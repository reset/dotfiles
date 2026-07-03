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
            processRunner: null!, dependencies: null!, config: new LibraryConfig());

    private static ConsoleKeyInfo Char(char c) => new(c, ConsoleKey.None, shift: false, alt: false, control: false);

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
