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

        return new OpticalDrive(vendor, product, mediaType, speeds);
    }

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex WhitespaceRun();

    [GeneratedRegex(@"Type:\s*([A-Za-z0-9+-]+)")]
    private static partial Regex MediaType();

    [GeneratedRegex(@"Write Speeds:\s*(.+)")]
    private static partial Regex WriteSpeedsLine();

    [GeneratedRegex(@"(\d+)x")]
    private static partial Regex SpeedValue();
}
