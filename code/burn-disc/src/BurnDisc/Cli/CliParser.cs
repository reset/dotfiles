using System.Globalization;

namespace BurnDisc.Cli;

//
// Parses the command line. Flags and options are case-insensitive per house
// CLI style; the single positional argument is the input file.
//
internal static class CliParser {
    public const string Usage = """
        Usage: burn-disc <file> [--speed N] [--dry-run]

        Supported formats:
          Archives:   .7z .zip .rar  (containing a disc image)
          Images:     .bin/.cue  .chd  .ccd/.img  .iso

        Options:
          --speed N    Burn speed (default: drive minimum). Lower is more
                       compatible with aging retro hardware.
          --dry-run    Extract and convert only; print the generated CUE
                       and exit without burning.
          -h, --help   Show this help.
        """;

    public static bool IsHelpRequested(string[] args) =>
        args.Any(static a => a.Equals("-h", StringComparison.OrdinalIgnoreCase)
            || a.Equals("--help", StringComparison.OrdinalIgnoreCase));

    public static CliOptions Parse(string[] args) {
        string? inputFile = null;
        int? speed = null;
        bool dryRun = false;

        for (int i = 0; i < args.Length; i++) {
            string arg = args[i];
            switch (arg.ToLowerInvariant()) {
                case "--speed":
                    if (i + 1 >= args.Length) {
                        throw new CliUsageException("--speed requires a value.");
                    }
                    if (!int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed <= 0) {
                        throw new CliUsageException($"Invalid --speed value: '{args[i]}' (expected a positive integer).");
                    }
                    speed = parsed;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                default:
                    if (arg.StartsWith('-')) {
                        throw new CliUsageException($"Unknown option: {arg}");
                    }
                    if (inputFile is not null) {
                        throw new CliUsageException($"Unexpected extra argument: {arg}");
                    }
                    inputFile = arg;
                    break;
            }
        }

        if (inputFile is null) {
            throw new CliUsageException("No input file given.");
        }

        return new CliOptions(inputFile, speed, dryRun);
    }
}
