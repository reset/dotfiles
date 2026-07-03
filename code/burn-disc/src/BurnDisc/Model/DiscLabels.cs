namespace BurnDisc.Model;

//
// Recognizes the generic placeholder volume labels that Sega CD masters stamp
// ("TEST", "MEGADRIVE_GAME_SPECIAL", macOS's "Audio CD", SEGA*, …) so the
// dashboard shows "has data" rather than pretending one of those is the game.
//
internal static class DiscLabels {
    private static readonly HashSet<string> s_placeholders = new(StringComparer.OrdinalIgnoreCase) {
        "TEST", "AUDIO CD", "MEGADRIVE_GAME_SPECIAL", "SEGA_CD", "SEGACD", "UNTITLED", "CDROM", "CD-ROM", "DISC"
    };

    public static bool IsPlaceholder(string? label) {
        if (string.IsNullOrWhiteSpace(label)) {
            return true;
        }
        string trimmed = label.Trim();
        return s_placeholders.Contains(trimmed) || trimmed.StartsWith("SEGA", StringComparison.OrdinalIgnoreCase);
    }

    // The label if it looks like a real title, else null.
    public static string? Meaningful(string? label) => IsPlaceholder(label) ? null : label?.Trim();
}
