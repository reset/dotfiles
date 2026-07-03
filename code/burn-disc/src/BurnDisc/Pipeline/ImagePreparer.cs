using System.Text.RegularExpressions;
using BurnDisc.Infrastructure;
using BurnDisc.Model;
using BurnDisc.Ui;

namespace BurnDisc.Pipeline;

internal interface IImagePreparer {
    Task<PreparedImage> PrepareAsync(string inputFile, string workDir, IProgressScope scope, CancellationToken cancellationToken = default);
}

//
// Turns the user's input into a burnable PreparedImage: unwraps archives,
// converts CHD/CCD to a CUE, and locates the sibling image for bare bin/img.
// Extraction and CHD conversion report progress; CCD conversion is native and
// instant.
//
internal sealed partial class ImagePreparer : IImagePreparer {
    private readonly IProcessRunner m_processRunner;
    private readonly IDependencyChecker m_dependencies;

    public ImagePreparer(IProcessRunner processRunner, IDependencyChecker dependencies) {
        m_processRunner = processRunner;
        m_dependencies = dependencies;
    }

    public async Task<PreparedImage> PrepareAsync(string inputFile, string workDir, IProgressScope scope, CancellationToken cancellationToken = default) {
        string label = Path.GetFileName(inputFile);
        string ext = Extension(inputFile);

        string? cueFile = null;
        string? chdFile = null;
        string? ccdFile = null;
        string? isoFile = null;

        switch (ext) {
            case "7z" or "zip" or "rar":
                await ExtractArchiveAsync(inputFile, workDir, scope, cancellationToken).ConfigureAwait(false);
                (cueFile, chdFile, ccdFile, isoFile) = DiscoverExtracted(workDir);
                break;
            case "cue":
                cueFile = inputFile;
                break;
            case "chd":
                chdFile = inputFile;
                break;
            case "iso":
                isoFile = inputFile;
                break;
            case "ccd":
                ccdFile = inputFile;
                break;
            case "bin" or "img":
                ccdFile = FindSibling(inputFile, "ccd");
                if (ccdFile is null) {
                    cueFile = FindSibling(inputFile, "cue")
                        ?? throw new ProcessException($"No .cue or .ccd file found alongside {Path.GetFileName(inputFile)}");
                }
                break;
            default:
                throw new ProcessException($"Unsupported format: .{ext}");
        }

        if (chdFile is not null) {
            cueFile = await ConvertChdAsync(chdFile, workDir, scope, cancellationToken).ConfigureAwait(false);
        }

        if (ccdFile is not null) {
            CueSheet sheet = ConvertCcd(ccdFile, workDir);
            string convertedCue = Path.Combine(workDir, $"{Path.GetFileNameWithoutExtension(ccdFile)}.cue");
            await File.WriteAllTextAsync(convertedCue, sheet.Render(), cancellationToken).ConfigureAwait(false);
            string ccdImage = Path.Combine(workDir, sheet.ImageFileName);
            return PreparedImage.FromCue(convertedCue, EImageFormat.Ccd, label, needsSwap: true, sheet.Tracks, ccdImage);
        }

        if (cueFile is not null) {
            string cueText = await File.ReadAllTextAsync(cueFile, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<DiscTrack> tracks = CueReader.Parse(cueText);
            EImageFormat format = chdFile is not null ? EImageFormat.Chd : EImageFormat.Cue;
            string? dataImage = CueReader.DataFile(cueText) is { } dataFile
                ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(cueFile)) ?? ".", dataFile)
                : null;
            return PreparedImage.FromCue(cueFile, format, label, needsSwap: false, tracks, dataImage);
        }

        if (isoFile is not null) {
            return PreparedImage.FromIso(isoFile, label);
        }

        throw new ProcessException("No burnable image found.");
    }

    //
    // Archive extraction
    //
    private async Task ExtractArchiveAsync(string archive, string workDir, IProgressScope scope, CancellationToken cancellationToken) {
        string ext = Extension(archive);
        IProgressTask task = scope.AddTask($"Extract {Path.GetFileName(archive)}", 100);

        void OnToken(string line) {
            Match m = Percent().Match(line);
            if (m.Success && double.TryParse(m.Groups[1].Value, out double pct)) {
                task.Value = pct;
            }
        }

        ProcessResult result;
        switch (ext) {
            case "7z":
                m_dependencies.EnsureAvailable("7z", "brew install p7zip");
                result = await m_processRunner.RunAsync("7z", ["x", archive, $"-o{workDir}", "-y", "-bsp1"], onToken: OnToken, cancellationToken: cancellationToken).ConfigureAwait(false);
                break;
            case "zip":
                result = await m_processRunner.RunAsync("unzip", ["-o", archive, "-d", workDir], cancellationToken: cancellationToken).ConfigureAwait(false);
                break;
            case "rar":
                m_dependencies.EnsureAvailable("unrar", "brew install unrar");
                result = await m_processRunner.RunAsync("unrar", ["x", "-y", archive, $"{workDir}/"], onToken: OnToken, cancellationToken: cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new ProcessException($"Unsupported archive format: .{ext}");
        }

        if (!result.Succeeded) {
            throw new ProcessException($"Extraction failed (exit {result.ExitCode}).\n{result.Output}");
        }
        task.Complete();
    }

    private static (string? Cue, string? Chd, string? Ccd, string? Iso) DiscoverExtracted(string workDir) {
        string? cue = FindByExtension(workDir, "cue");
        string? chd = FindByExtension(workDir, "chd");
        string? ccd = FindByExtension(workDir, "ccd");
        string? iso = FindByExtension(workDir, "iso");

        if (cue is null && chd is null && ccd is null && iso is null) {
            string? bin = FindByExtension(workDir, "bin");
            throw bin is not null
                ? new ProcessException($"Found {Path.GetFileName(bin)} but no matching .cue file in archive. A .cue is required to burn a multi-track disc correctly.")
                : new ProcessException("No burnable image found in archive (looked for .cue, .chd, .ccd, .iso).");
        }
        return (cue, chd, ccd, iso);
    }

    //
    // CHD -> bin/cue via chdman
    //
    private async Task<string> ConvertChdAsync(string chdFile, string workDir, IProgressScope scope, CancellationToken cancellationToken) {
        m_dependencies.EnsureAvailable("chdman", "brew install mame");
        string baseName = Path.GetFileNameWithoutExtension(chdFile);
        string cueOut = Path.Combine(workDir, $"{baseName}.cue");
        string binOut = Path.Combine(workDir, $"{baseName}.bin");

        IProgressTask task = scope.AddTask("Convert CHD → bin/cue", 100);
        void OnToken(string line) {
            Match m = Percent().Match(line);
            if (m.Success && double.TryParse(m.Groups[1].Value, out double pct)) {
                task.Value = pct;
            }
        }

        ProcessResult result = await m_processRunner.RunAsync(
            "chdman", ["extractcd", "-i", chdFile, "-o", cueOut, "-ob", binOut], onToken: OnToken, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded) {
            throw new ProcessException($"chdman extractcd failed (exit {result.ExitCode}).\n{result.Output}");
        }
        task.Complete();
        return cueOut;
    }

    //
    // CCD -> cue (native). The .img is symlinked next to the generated cue so
    // cdrdao — which resolves FILE relative to the cue's directory — can find it.
    //
    private static CueSheet ConvertCcd(string ccdFile, string workDir) {
        string baseName = Path.GetFileNameWithoutExtension(ccdFile);
        string ccdDir = Path.GetDirectoryName(Path.GetFullPath(ccdFile)) ?? ".";

        string imgFile = FindByExtension(ccdDir, "img", topOnly: true, baseName: baseName)
            ?? throw new ProcessException($"No .img file found alongside {Path.GetFileName(ccdFile)}");
        string imgName = Path.GetFileName(imgFile);

        string linkPath = Path.Combine(workDir, imgName);
        if (Path.GetFullPath(imgFile) != Path.GetFullPath(linkPath) && !File.Exists(linkPath)) {
            File.CreateSymbolicLink(linkPath, Path.GetFullPath(imgFile));
        }

        return CcdParser.Parse(File.ReadAllText(ccdFile), imgName);
    }

    //
    // Helpers
    //
    private static string Extension(string path) => Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

    private static string? FindSibling(string file, string ext) {
        string dir = Path.GetDirectoryName(Path.GetFullPath(file)) ?? ".";
        string baseName = Path.GetFileNameWithoutExtension(file);
        return FindByExtension(dir, ext, topOnly: true, baseName: baseName);
    }

    private static string? FindByExtension(string dir, string ext, bool topOnly = false, string? baseName = null) {
        if (!Directory.Exists(dir)) {
            return null;
        }
        SearchOption option = topOnly ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;
        return Directory.EnumerateFiles(dir, "*", option)
            .Where(f => string.Equals(Extension(f), ext, StringComparison.OrdinalIgnoreCase))
            .Where(f => baseName is null || string.Equals(Path.GetFileNameWithoutExtension(f), baseName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static f => f, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    [GeneratedRegex(@"(\d+(?:\.\d+)?)%")]
    private static partial Regex Percent();
}
