namespace BurnDisc.Model;

//
// Platform metadata: display names, burn caveats (from hard-won retro-burning
// experience), and a cheap path-based guess for the browser list.
//
internal static class Platform {
    public static string DisplayName(EPlatform platform) => platform switch {
        EPlatform.SegaCd => "Sega CD",
        EPlatform.Saturn => "Saturn",
        EPlatform.PlayStation => "PlayStation",
        EPlatform.Dreamcast => "Dreamcast",
        EPlatform.DataDisc => "Data disc",
        _ => "Unknown"
    };

    // A one-line burn caveat, or null when there's nothing special to warn about.
    public static string? Caveat(EPlatform platform) => platform switch {
        EPlatform.SegaCd => "CD-R only · may need internal memory format",
        EPlatform.Saturn => "burn at the drive's slowest speed · needs a mod to boot",
        EPlatform.PlayStation => "needs a modchip or soft-mod to boot",
        EPlatform.Dreamcast => "boots on MIL-CD-compatible units only",
        _ => null
    };

    //
    // Best-effort platform from a file path, by matching a library folder name
    // (e.g. ~/roms/Sega CD/…). Cheap enough to run for the whole browser list;
    // the authoritative check is the data-track signature at burn time.
    //
    public static EPlatform FromPath(string path) {
        string lower = path.Replace('\\', '/').ToLowerInvariant();
        if (lower.Contains("sega cd") || lower.Contains("segacd") || lower.Contains("mega cd") || lower.Contains("megacd")) {
            return EPlatform.SegaCd;
        }
        if (lower.Contains("saturn")) {
            return EPlatform.Saturn;
        }
        if (lower.Contains("playstation") || lower.Contains("psx") || lower.Contains("/ps1")) {
            return EPlatform.PlayStation;
        }
        if (lower.Contains("dreamcast")) {
            return EPlatform.Dreamcast;
        }
        return EPlatform.Unknown;
    }
}
