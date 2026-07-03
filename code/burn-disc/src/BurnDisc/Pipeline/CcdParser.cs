using System.Globalization;
using System.Text.RegularExpressions;
using BurnDisc.Model;

namespace BurnDisc.Pipeline;

//
// Parses a CloneCD .ccd control file into a CueSheet.
//
// CCD files describe the disc with [Entry N] sections, NOT [TRACK N]. Track
// identity comes from the hex Point field:
//   0x01-0x63 -> real track numbers 1..99
//   0xa0/0xa1/0xa2 -> first-track / last-track / lead-out TOC metadata (ignored)
// Per entry: Control & 0x04 marks a data track (else audio); PLBA is the
// track's absolute sector offset into the .img.
//
internal static partial class CcdParser {
    public static CueSheet Parse(string ccdText, string imageFileName) {
        Dictionary<string, string>? current = null;
        List<Dictionary<string, string>> entries = [];

        foreach (string rawLine in ccdText.Split('\n')) {
            string line = rawLine.Trim();
            if (EntryHeader().IsMatch(line)) {
                current = [];
                entries.Add(current);
            } else if (current is not null) {
                int eq = line.IndexOf('=');
                if (eq > 0) {
                    string key = line[..eq].Trim();
                    string value = line[(eq + 1)..].Trim();
                    current[key] = value;
                }
            }
        }

        List<DiscTrack> tracks = [];
        foreach (Dictionary<string, string> entry in entries) {
            if (!entry.TryGetValue("Point", out string? pointStr) || !TryParseHex(pointStr, out int point)) {
                continue;
            }
            if (point is < 0x01 or > 0x63) {
                continue; // TOC metadata entry (0xa0/a1/a2), not a real track
            }

            int control = entry.TryGetValue("Control", out string? controlStr) && TryParseHex(controlStr, out int c) ? c : 0x00;
            int plba = entry.TryGetValue("PLBA", out string? plbaStr) && int.TryParse(plbaStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) ? p : 0;

            ETrackType type = (control & 0x04) != 0 ? ETrackType.Data : ETrackType.Audio;
            tracks.Add(new DiscTrack(point, type, plba));
        }

        return new CueSheet(imageFileName, tracks);
    }

    private static bool TryParseHex(string value, out int result) {
        string trimmed = value.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
            trimmed = trimmed[2..];
        }
        return int.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
    }

    [GeneratedRegex(@"^\[Entry \d+\]", RegexOptions.IgnoreCase)]
    private static partial Regex EntryHeader();
}
