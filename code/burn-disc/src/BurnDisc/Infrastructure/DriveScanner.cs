using System.Text.RegularExpressions;
using BurnDisc.Model;

namespace BurnDisc.Infrastructure;

internal interface IDriveScanner {
    Task<OpticalDrive?> ScanAsync(CancellationToken cancellationToken = default);
}

//
// Discovers the optical drive and loaded media by parsing `drutil status`
// (macOS). The parse is a pure static so it can be unit-tested against
// captured drutil output without a real drive.
//
internal sealed partial class DriveScanner : IDriveScanner {
    private readonly IProcessRunner m_processRunner;

    public DriveScanner(IProcessRunner processRunner) {
        m_processRunner = processRunner;
    }

    public async Task<OpticalDrive?> ScanAsync(CancellationToken cancellationToken = default) {
        ProcessResult result;
        try {
            result = await m_processRunner.RunAsync("drutil", ["status"], cancellationToken: cancellationToken).ConfigureAwait(false);
        } catch (ProcessException) {
            return null; // drutil unavailable (non-macOS, etc.) — not fatal
        }

        // If a written disc is loaded, macOS mounts it — read its volume label
        // so we can show what the disc actually contains (e.g. "FINAL_FIGHT").
        string? volumeLabel = null;
        if (ParseDeviceNode(result.Output) is { } device) {
            try {
                ProcessResult mount = await m_processRunner.RunAsync("mount", [], cancellationToken: cancellationToken).ConfigureAwait(false);
                volumeLabel = ExtractVolumeLabel(mount.Output, device);
            } catch (ProcessException) {
                // mount unavailable — the label is a nicety, not required
            }
        }

        return ParseDrutilStatus(result.Output, volumeLabel);
    }

    public static OpticalDrive? ParseDrutilStatus(string text, string? volumeLabel = null) {
        string[] lines = text.Split('\n');

        // The vendor/product/rev row is the line immediately after the header row.
        string vendor = "";
        string product = "";
        for (int i = 0; i < lines.Length; i++) {
            if (lines[i].Contains("Vendor", StringComparison.Ordinal) && lines[i].Contains("Product", StringComparison.Ordinal)) {
                if (i + 1 < lines.Length) {
                    string[] columns = WhitespaceRun().Split(lines[i + 1].Trim());
                    if (columns.Length >= 2) {
                        vendor = columns[0];
                        product = columns[1];
                    }
                }
                break;
            }
        }

        if (vendor.Length == 0 && product.Length == 0) {
            return null; // no drive reported
        }

        string? mediaType = null;
        Match typeMatch = MediaType().Match(text);
        if (typeMatch.Success) {
            mediaType = typeMatch.Groups[1].Value.Trim();
        }

        List<int> speeds = [];
        Match speedsMatch = WriteSpeedsLine().Match(text);
        if (speedsMatch.Success) {
            foreach (Match m in SpeedValue().Matches(speedsMatch.Groups[1].Value)) {
                speeds.Add(int.Parse(m.Groups[1].Value));
            }
        }

        // Used space (drutil reports 2048-byte blocks), and blank/used state.
        long usedBytes = 0;
        bool? isBlank = null;
        Match usedMatch = SpaceUsedLine().Match(text);
        if (usedMatch.Success && long.TryParse(usedMatch.Groups[1].Value, out long usedBlocks)) {
            usedBytes = usedBlocks * 2048L;
        }
        Match writabilityMatch = WritabilityLine().Match(text);
        if (writabilityMatch.Success) {
            isBlank = writabilityMatch.Groups[1].Value.Contains("blank", StringComparison.OrdinalIgnoreCase);
        } else if (usedMatch.Success) {
            isBlank = usedBytes == 0;
        }

        int trackCount = 0;
        Match tracksMatch = TracksLine().Match(text);
        if (tracksMatch.Success) {
            _ = int.TryParse(tracksMatch.Groups[1].Value, out trackCount);
        }

        return new OpticalDrive(vendor, product, mediaType, speeds, isBlank, usedBytes, volumeLabel, trackCount);
    }

    // The drive's device node from drutil's "Name: /dev/diskN" line.
    public static string? ParseDeviceNode(string drutilText) {
        Match m = DeviceNode().Match(drutilText);
        return m.Success ? m.Groups[1].Value : null;
    }

    // The mounted volume name for the given device, from `mount` output (lines
    // like "/dev/disk4s0 on /Volumes/FINAL_FIGHT (cd9660, ...)"). The audio
    // (cddafs) volume carries the game title, so it's preferred over the data
    // track's ISO9660 label, which is often generic mastering boilerplate
    // ("MEGADRIVE_GAME_SPECIAL"). Falls back to the first volume found.
    public static string? ExtractVolumeLabel(string mountOutput, string deviceNode) {
        string? audio = null;
        string? data = null;
        foreach (string line in mountOutput.Split('\n')) {
            Match m = MountLine().Match(line);
            if (!m.Success || !m.Groups[1].Value.StartsWith(deviceNode, StringComparison.Ordinal)) {
                continue;
            }
            string label = m.Groups[2].Value.Trim();
            if (m.Groups[3].Value == "cddafs") {
                audio ??= label;
            } else {
                data ??= label;
            }
        }
        // Prefer the audio volume's title, but skip macOS's generic "Audio CD"
        // (used when the disc has no CD-Text and no lookup matched); fall back to
        // the data track's label.
        if (audio is { } a && !a.Equals("Audio CD", StringComparison.OrdinalIgnoreCase)) {
            return a;
        }
        return data ?? audio;
    }

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex WhitespaceRun();

    [GeneratedRegex(@"Type:\s*([A-Za-z0-9+-]+)")]
    private static partial Regex MediaType();

    [GeneratedRegex(@"Write Speeds:\s*(.+)")]
    private static partial Regex WriteSpeedsLine();

    [GeneratedRegex(@"(\d+)x")]
    private static partial Regex SpeedValue();

    [GeneratedRegex(@"Space Used:.*?blocks:\s*(\d+)")]
    private static partial Regex SpaceUsedLine();

    [GeneratedRegex(@"Writability:\s*(.+)")]
    private static partial Regex WritabilityLine();

    [GeneratedRegex(@"Tracks:\s*(\d+)")]
    private static partial Regex TracksLine();

    [GeneratedRegex(@"Name:\s*(/dev/\S+)")]
    private static partial Regex DeviceNode();

    [GeneratedRegex(@"^(\S+) on /Volumes/(.+?) \((\w+)")]
    private static partial Regex MountLine();
}
