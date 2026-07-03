namespace BurnDisc.Cli;

internal sealed class CliOptions {
    public CliOptions(string inputFile, int? speed, bool dryRun) {
        InputFile = inputFile;
        Speed = speed;
        DryRun = dryRun;
    }

    public string InputFile { get; }
    public int? Speed { get; }      // null => auto-detect the drive minimum
    public bool DryRun { get; }
}

//
// Raised for bad or missing arguments; the caller prints usage and exits 1.
//
internal sealed class CliUsageException : Exception {
    public CliUsageException(string message) : base(message) {
    }
}
