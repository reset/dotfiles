using BurnDisc.Model;

namespace BurnDisc.Ui;

//
// Pure presentation logic for the library browser: filtering, scroll
// windowing, and bar rendering. Kept free of any console interaction so the
// behaviour is unit-tested without a terminal.
//
internal static class LibraryView {
    //
    // Filter by a space-separated query (every token must be a case-insensitive
    // substring of the name), then order local-first and alphabetical.
    //
    public static IReadOnlyList<LibraryItem> Filter(IReadOnlyList<LibraryItem> items, string query) {
        string[] tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return items
            .Where(item => tokens.All(t => item.DisplayName.Contains(t, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(static item => item.Source)
            .ThenBy(static item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    //
    // Group the library by platform for the top-level menu: one entry per
    // platform present, with its title count, ordered alphabetically with the
    // catch-all "Other" (Unknown) last.
    //
    public static IReadOnlyList<(EPlatform Platform, int Count)> GroupByPlatform(IReadOnlyList<LibraryItem> items) {
        return items
            .GroupBy(static i => i.Platform)
            .Select(static g => (Platform: g.Key, Count: g.Count()))
            .OrderBy(static g => g.Platform == EPlatform.Unknown ? 1 : 0)
            .ThenBy(static g => Model.Platform.DisplayName(g.Platform), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    //
    // Given a list, a cursor index, and how many rows are visible, return the
    // scroll offset (index of the first visible row) that keeps the cursor on
    // screen while moving as little as possible.
    //
    public static int ScrollFor(int count, int cursor, int visibleRows, int currentScroll) {
        if (count <= visibleRows) {
            return 0;
        }
        int scroll = currentScroll;
        if (cursor < scroll) {
            scroll = cursor;
        } else if (cursor >= scroll + visibleRows) {
            scroll = cursor - visibleRows + 1;
        }
        int maxScroll = Math.Max(0, count - visibleRows);
        return Math.Clamp(scroll, 0, maxScroll);
    }

    public static int Clamp(int value, int min, int max) => max < min ? min : Math.Clamp(value, min, max);

    //
    // A fixed-width progress bar: filled cells proportional to value/max.
    //
    public static string Bar(double value, double max, int width) {
        double fraction = max <= 0 ? 0 : Math.Clamp(value / max, 0, 1);
        int filled = (int)Math.Round(fraction * width);
        return new string('█', filled) + new string('░', width - filled);
    }
}
