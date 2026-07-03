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
        return ParseDrutilStatus(result.Output);
    }

    public static OpticalDrive? ParseDrutilStatus(string text) {
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

        return new OpticalDrive(vendor, product, mediaType, speeds, isBlank, usedBytes);
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
}
