using System.Globalization;

namespace BurnDisc.Model;

internal static class Sizes {
    private static readonly string[] s_units = ["B", "K", "M", "G", "T"];

    // Human-readable byte count, e.g. 1536 -> "1.5K", 1073741824 -> "1G".
    public static string Human(long bytes) {
        if (bytes < 1024) {
            return $"{bytes}B";
        }
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < s_units.Length - 1) {
            value /= 1024;
            unit++;
        }
        return string.Create(CultureInfo.InvariantCulture, $"{value:0.#}{s_units[unit]}");
    }
}
