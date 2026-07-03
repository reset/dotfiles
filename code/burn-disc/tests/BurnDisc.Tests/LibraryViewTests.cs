using BurnDisc.Model;
using BurnDisc.Ui;

namespace BurnDisc.Tests;

public sealed class LibraryViewTests {
    private static LibraryItem Local(string name) => new(name, ELibrarySource.Local, $"/roms/{name}.7z", 100);
    private static LibraryItem Server(string name) => new(name, ELibrarySource.Server, $"{name}.7z", 100);

    [Fact]
    public void Filter_MatchesAllTokens_CaseInsensitive() {
        LibraryItem[] items = [Local("Sonic CD"), Local("Final Fight CD"), Local("Snatcher")];
        IReadOnlyList<LibraryItem> result = LibraryView.Filter(items, "cd sonic");
        Assert.Single(result);
        Assert.Equal("Sonic CD", result[0].DisplayName);
    }

    [Fact]
    public void Filter_EmptyQuery_ReturnsAll_LocalFirstThenAlphabetical() {
        LibraryItem[] items = [Server("Alpha"), Local("Zeta"), Local("Beta")];
        IReadOnlyList<LibraryItem> result = LibraryView.Filter(items, "");

        Assert.Equal(["Beta", "Zeta", "Alpha"], result.Select(i => i.DisplayName));
        Assert.Equal(ELibrarySource.Local, result[0].Source);
        Assert.Equal(ELibrarySource.Server, result[^1].Source);
    }

    [Fact]
    public void GroupByPlatform_CountsPerPlatform_OtherLast() {
        LibraryItem[] items = [
            new("Sonic CD", ELibrarySource.Local, "a", 1, EPlatform.SegaCd),
            new("Snatcher", ELibrarySource.Local, "b", 1, EPlatform.SegaCd),
            new("Nights", ELibrarySource.Local, "c", 1, EPlatform.Saturn),
            new("mystery", ELibrarySource.Local, "d", 1, EPlatform.Unknown),
        ];

        var groups = LibraryView.GroupByPlatform(items)
            .Select(g => (Name: Platform.DisplayName(g.Platform), g.Count))
            .ToList();

        Assert.Equal(("Saturn", 1), groups[0]);   // alphabetical
        Assert.Equal(("Sega CD", 2), groups[1]);
        Assert.Equal(("Other", 1), groups[^1]);    // Unknown ("Other") sorts last
    }

    [Theory]
    // count, cursor, visible, currentScroll -> expected scroll
    [InlineData(3, 0, 10, 0, 0)]     // everything fits
    [InlineData(100, 0, 10, 0, 0)]   // cursor at top
    [InlineData(100, 15, 10, 0, 6)]  // cursor below window -> scroll to show it at bottom
    [InlineData(100, 5, 10, 8, 5)]   // cursor above window -> scroll up to cursor
    [InlineData(100, 99, 10, 0, 90)] // cursor at end -> clamp to last window
    public void ScrollFor_KeepsCursorVisible(int count, int cursor, int visible, int current, int expected) {
        Assert.Equal(expected, LibraryView.ScrollFor(count, cursor, visible, current));
    }

    [Theory]
    [InlineData(0, 100, 10, 0)]
    [InlineData(50, 100, 10, 5)]
    [InlineData(100, 100, 10, 10)]
    [InlineData(200, 100, 10, 10)] // over 100% clamps
    public void Bar_FillsProportionally(double value, double max, int width, int expectedFilled) {
        string bar = LibraryView.Bar(value, max, width);
        Assert.Equal(width, bar.Length);
        Assert.Equal(expectedFilled, bar.Count(c => c == '█'));
    }
}
