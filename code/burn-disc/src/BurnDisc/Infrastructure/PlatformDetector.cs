using System.Text;
using BurnDisc.Model;

namespace BurnDisc.Infrastructure;

internal readonly record struct DetectedPlatform(EPlatform Platform, string? Signature);

//
// Formats a detected platform into display lines for the burn view / log:
// a "Platform" line (with the matched signature) and an optional caveat.
// Empty for an unidentified disc, so it adds no noise.
//
internal static class PlatformSummary {
    public static IReadOnlyList<string> Lines(DetectedPlatform detected) {
        if (detected.Platform == EPlatform.Unknown) {
            return [];
        }
        string signature = detected.Signature is { } s ? $" ({s})" : "";
        List<string> lines = [$"Platform: {Platform.DisplayName(detected.Platform)}{signature}"];
        if (Platform.Caveat(detected.Platform) is { } caveat) {
            lines.Add($"Note: {caveat}");
        }
        return lines;
    }
}

internal interface IPlatformDetector {
    DetectedPlatform Detect(string? dataImagePath);
}

//
// Identifies the console by scanning the start of the data track for a known
// system-area signature (verified against Sega CD images: "SEGADISCSYSTEM").
// Falls back to an ISO9660 marker for plain data discs, else Unknown.
//
internal sealed class PlatformDetector : IPlatformDetector {
    private const int ScanBytes = 64 * 1024;

    // Ordered by specificity — the first signature found wins.
    private static readonly (string Signature, EPlatform Platform)[] s_signatures = [
        ("SEGADISCSYSTEM", EPlatform.SegaCd),
        ("SEGABOOTDISC", EPlatform.SegaCd),
        ("SEGA SEGASATURN", EPlatform.Saturn),
        ("SEGA SEGAKATANA", EPlatform.Dreamcast),
        ("Sony Computer Entertainment", EPlatform.PlayStation),
        ("PLAYSTATION", EPlatform.PlayStation),
    ];

    public DetectedPlatform Detect(string? dataImagePath) {
        if (string.IsNullOrEmpty(dataImagePath) || !File.Exists(dataImagePath)) {
            return new DetectedPlatform(EPlatform.Unknown, null);
        }

        string header;
        try {
            using FileStream stream = File.OpenRead(dataImagePath);
            byte[] buffer = new byte[ScanBytes];
            int read = stream.ReadAtLeast(buffer, ScanBytes, throwOnEndOfStream: false);
            // Latin1 maps every byte 1:1 to a char, so ASCII signatures match
            // without any decoding failures on binary data.
            header = Encoding.Latin1.GetString(buffer, 0, read);
        } catch (IOException) {
            return new DetectedPlatform(EPlatform.Unknown, null);
        }

        foreach ((string signature, EPlatform platform) in s_signatures) {
            if (header.Contains(signature, StringComparison.Ordinal)) {
                return new DetectedPlatform(platform, signature);
            }
        }

        // ISO9660 volume descriptor identifier — a plain data/PC disc.
        if (header.Contains("CD001", StringComparison.Ordinal)) {
            return new DetectedPlatform(EPlatform.DataDisc, "ISO9660");
        }

        return new DetectedPlatform(EPlatform.Unknown, null);
    }
}
