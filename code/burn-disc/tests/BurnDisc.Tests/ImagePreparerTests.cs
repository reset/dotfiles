using BurnDisc.Infrastructure;
using BurnDisc.Model;
using BurnDisc.Pipeline;
using BurnDisc.Ui;

namespace BurnDisc.Tests;

public sealed class ImagePreparerTests {
    [Fact]
    public async Task PrepareAsync_CcdInput_ConvertsNativelyWithoutSpawningProcesses() {
        string sourceDir = Directory.CreateTempSubdirectory("burn-src-").FullName;
        string workDir = Directory.CreateTempSubdirectory("burn-work-").FullName;
        try {
            string ccdPath = Path.Combine(sourceDir, "game.ccd");
            await File.WriteAllTextAsync(ccdPath, SampleCcd);
            await File.WriteAllTextAsync(Path.Combine(sourceDir, "game.img"), "not-a-real-image");

            ImagePreparer preparer = new(new ThrowingProcessRunner(), new DependencyChecker());
            PreparedImage image = await preparer.PrepareAsync(ccdPath, workDir, new NullScope());

            Assert.Equal(EImageFormat.Ccd, image.SourceFormat);
            Assert.True(image.NeedsSwap);
            Assert.Equal(2, image.Tracks.Count);

            // A CUE was written into the work dir, and the .img was linked next
            // to it so cdrdao (which resolves FILE relative to the cue) finds it.
            Assert.NotNull(image.CueFilePath);
            Assert.Equal(workDir, Path.GetDirectoryName(image.CueFilePath));
            Assert.True(File.Exists(Path.Combine(workDir, "game.img")));

            string cue = await File.ReadAllTextAsync(image.CueFilePath!);
            Assert.Contains("FILE \"game.img\" BINARY", cue);
            Assert.Contains("TRACK 01 MODE1/2352", cue);
            Assert.Contains("TRACK 02 AUDIO", cue);
        } finally {
            Directory.Delete(sourceDir, recursive: true);
            Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public async Task PrepareAsync_UnsupportedExtension_Throws() {
        ImagePreparer preparer = new(new ThrowingProcessRunner(), new DependencyChecker());
        await Assert.ThrowsAsync<ProcessException>(
            () => preparer.PrepareAsync("game.xyz", Path.GetTempPath(), new NullScope()));
    }

    private const string SampleCcd = """
        [Disc]
        TocEntries=4
        [Entry 0]
        Point=0xa2
        Control=0x04
        PLBA=13000
        [Entry 1]
        Point=0x01
        Control=0x04
        PLBA=0
        [Entry 2]
        Point=0x02
        Control=0x00
        PLBA=12000
        """;

    private sealed class ThrowingProcessRunner : IProcessRunner {
        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory = null, Action<string>? onToken = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException($"No external process should run for a CCD source, but '{fileName}' was invoked.");
    }

    private sealed class NullScope : IProgressScope {
        public IProgressTask AddTask(string description, double maxValue) => new NullTask();
        public void Log(string message) { }

        private sealed class NullTask : IProgressTask {
            public double MaxValue { get; set; } = 1;
            public double Value { get; set; }
            public void Complete() { }
        }
    }
}
