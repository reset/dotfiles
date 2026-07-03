using BurnDisc.Cli;
using BurnDisc.Infrastructure;
using BurnDisc.Model;
using BurnDisc.Pipeline;
using BurnDisc.Ui;

namespace BurnDisc;

//
// Top-level workflow: scan the drive, resolve the write speed, prepare the
// image (extract/convert), then either dump the CUE (dry-run) or burn it.
//
internal sealed class BurnService {
    private readonly IDriveScanner m_driveScanner;
    private readonly IImagePreparer m_preparer;
    private readonly IBurner m_burner;
    private readonly IProgressReporter m_reporter;
    private readonly IPlatformDetector m_platformDetector;

    public BurnService(IDriveScanner driveScanner, IImagePreparer preparer, IBurner burner, IProgressReporter reporter, IPlatformDetector platformDetector) {
        m_driveScanner = driveScanner;
        m_preparer = preparer;
        m_burner = burner;
        m_reporter = reporter;
        m_platformDetector = platformDetector;
    }

    public async Task<int> RunAsync(CliOptions options, CancellationToken cancellationToken = default) {
        string inputFile = options.InputFile!; // Program only reaches here with a file
        if (!File.Exists(inputFile)) {
            m_reporter.Warn($"File not found: {inputFile}");
            return 1;
        }

        OpticalDrive? drive = options.DryRun ? null : await m_driveScanner.ScanAsync(cancellationToken).ConfigureAwait(false);
        int? speed = ResolveSpeed(options, drive);

        if (!options.DryRun && drive is { MediaType: null }) {
            m_reporter.Warn("No disc in the drive — insert a blank CD-R.");
            return 1;
        }
        if (!options.DryRun && drive?.IsBlank == false) {
            m_reporter.Warn("Disc is not blank — the burn will likely fail. Insert a blank CD-R.");
        }

        m_reporter.Header("burn-disc", BuildHeaderRows(inputFile, drive, speed, options));

        string workDir = Directory.CreateTempSubdirectory("burn-disc-").FullName;
        try {
            PreparedImage? prepared = null;
            await m_reporter.RunAsync(async scope => {
                prepared = await m_preparer.PrepareAsync(inputFile, workDir, scope, cancellationToken).ConfigureAwait(false);
                scope.Log(prepared.Describe());
                foreach (string line in PlatformSummary.Lines(m_platformDetector.Detect(prepared.DataImagePath))) {
                    scope.Log(line);
                }
                if (!options.DryRun) {
                    await m_burner.BurnAsync(prepared, speed, scope, cancellationToken).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            if (options.DryRun) {
                DumpDryRun(prepared!);
            } else {
                m_reporter.Success("Done!");
            }
            return 0;
        } finally {
            TryDeleteDirectory(workDir);
        }
    }

    private int? ResolveSpeed(CliOptions options, OpticalDrive? drive) {
        if (options.Speed is int requested) {
            if (drive?.MinWriteSpeed is int min && requested < min) {
                m_reporter.Warn($"Requested speed {requested}x is below the drive minimum {min}x; cdrdao will use {min}x.");
            }
            return requested;
        }

        if (drive?.MinWriteSpeed is int detected) {
            m_reporter.Info($"Using drive minimum write speed: {detected}x");
            return detected;
        }

        if (!options.DryRun) {
            m_reporter.Warn("Could not detect drive write speed; the burn tool will use its default.");
        }
        return null;
    }

    private static List<(string Label, string Value)> BuildHeaderRows(string inputFile, OpticalDrive? drive, int? speed, CliOptions options) {
        List<(string, string)> rows = [("Source", Path.GetFileName(inputFile))];
        if (drive is not null) {
            rows.Add(("Drive", drive.DisplayName));
            rows.Add(("Media", drive.MediaType ?? "unknown"));
        }
        rows.Add(("Speed", speed is int s ? $"{s}x" : options.DryRun ? "n/a (dry-run)" : "drive default"));
        return rows;
    }

    private static void DumpDryRun(PreparedImage image) {
        if (image.IsIso) {
            Console.WriteLine($"ISO: {Path.GetFileName(image.IsoFilePath!)} (no CUE — single data track)");
            return;
        }
        Console.WriteLine($"=== {Path.GetFileName(image.CueFilePath!)} ===");
        Console.Write(File.ReadAllText(image.CueFilePath!));
    }

    private static void TryDeleteDirectory(string dir) {
        try {
            if (Directory.Exists(dir)) {
                Directory.Delete(dir, recursive: true);
            }
        } catch (IOException) {
            // best-effort cleanup of a temp dir; ignore
        } catch (UnauthorizedAccessException) {
            // ditto
        }
    }
}
