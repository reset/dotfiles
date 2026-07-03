using System.Text.RegularExpressions;
using BurnDisc.Infrastructure;
using BurnDisc.Model;
using BurnDisc.Ui;

namespace BurnDisc.Pipeline;

internal interface IBurner {
    Task BurnAsync(PreparedImage image, int? speed, IProgressScope scope, CancellationToken cancellationToken = default);
}

//
// Writes a PreparedImage to the disc: cdrdao for a CUE (with --swap for CCD
// sources), cdrecord for a bare ISO. Both tools stream "N of M MB" progress
// which we parse into a bar sized to the total.
//
internal sealed partial class Burner : IBurner {
    private readonly IProcessRunner m_processRunner;
    private readonly IDependencyChecker m_dependencies;

    public Burner(IProcessRunner processRunner, IDependencyChecker dependencies) {
        m_processRunner = processRunner;
        m_dependencies = dependencies;
    }

    public Task BurnAsync(PreparedImage image, int? speed, IProgressScope scope, CancellationToken cancellationToken = default) =>
        image.IsIso
            ? BurnIsoAsync(image.IsoFilePath!, speed, scope, cancellationToken)
            : BurnCueAsync(image, speed, scope, cancellationToken);

    private async Task BurnCueAsync(PreparedImage image, int? speed, IProgressScope scope, CancellationToken cancellationToken) {
        m_dependencies.EnsureAvailable("cdrdao", "brew install cdrdao");

        string cuePath = image.CueFilePath!;
        // cdrdao resolves FILE paths relative to the invocation directory, not
        // the cue's location — so run from the cue's directory with a basename.
        string workingDir = Path.GetDirectoryName(Path.GetFullPath(cuePath)) ?? ".";
        string cueName = Path.GetFileName(cuePath);

        List<string> args = ["write"];
        if (speed is int s) {
            args.Add("--speed");
            args.Add(s.ToString());
        }
        if (image.NeedsSwap) {
            args.Add("--swap");
        }
        args.Add(cueName);

        IProgressTask task = scope.AddTask("Burn", 100);
        void OnToken(string line) {
            Match wrote = CdrdaoProgress().Match(line);
            if (wrote.Success) {
                UpdateMbTask(task, wrote);
                return;
            }
            Match track = CdrdaoTrack().Match(line);
            if (track.Success) {
                scope.Log($"Writing track {track.Groups[1].Value}...");
            }
        }

        ProcessResult result = await m_processRunner.RunAsync("cdrdao", args, workingDir, OnToken, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded) {
            throw new ProcessException($"cdrdao write failed (exit {result.ExitCode}). The disc is likely a coaster — start fresh with a new CD-R.\n{result.Output}");
        }
        task.Complete();
    }

    private async Task BurnIsoAsync(string isoFile, int? speed, IProgressScope scope, CancellationToken cancellationToken) {
        m_dependencies.EnsureAvailable("cdrecord", "brew install cdrtools");

        List<string> args = ["-v"];
        if (speed is int s) {
            args.Add($"speed={s}");
        }
        args.Add(isoFile);

        IProgressTask task = scope.AddTask("Burn", 100);
        void OnToken(string line) {
            Match wrote = CdrecordProgress().Match(line);
            if (wrote.Success) {
                UpdateMbTask(task, wrote);
            }
        }

        ProcessResult result = await m_processRunner.RunAsync("cdrecord", args, onToken: OnToken, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded) {
            throw new ProcessException($"cdrecord failed (exit {result.ExitCode}). The disc is likely a coaster — start fresh with a new CD-R.\n{result.Output}");
        }
        task.Complete();
    }

    private static void UpdateMbTask(IProgressTask task, Match mbMatch) {
        double written = double.Parse(mbMatch.Groups[1].Value);
        double total = double.Parse(mbMatch.Groups[2].Value);
        if (total > 0) {
            task.MaxValue = total;
        }
        task.Value = written;
    }

    [GeneratedRegex(@"Wrote\s+(\d+)\s+of\s+(\d+)\s+MB")]
    private static partial Regex CdrdaoProgress();

    [GeneratedRegex(@"Writing track\s+(\d+)")]
    private static partial Regex CdrdaoTrack();

    [GeneratedRegex(@"(\d+)\s+of\s+(\d+)\s+MB\s+written")]
    private static partial Regex CdrecordProgress();
}
