using System.Text;
using BurnDisc.Infrastructure;
using BurnDisc.Model;

namespace BurnDisc.Tests;

public sealed class PlatformTests {
    [Theory]
    // Expected compared by enum name so this public test method needn't expose the internal enum.
    [InlineData("/Users/reset/roms/Sega CD/Time Gal (USA).7z", "SegaCd")]
    [InlineData("Sega CD/Sonic CD (USA).7z", "SegaCd")]
    [InlineData("/roms/Saturn/Nights.chd", "Saturn")]
    [InlineData("/roms/PlayStation/FF7.cue", "PlayStation")]
    [InlineData("/roms/Dreamcast/game.cdi", "Dreamcast")]
    [InlineData("/roms/misc/backup.iso", "Unknown")]
    public void FromPath_MatchesLibraryFolder(string path, string expected) {
        Assert.Equal(expected, Platform.FromPath(path).ToString());
    }

    [Fact]
    public void Detect_SegaCdSignature() {
        string file = WriteHeader("some padding SEGADISCSYSTEM  TIME GAL CD  SEGA CD");
        try {
            DetectedPlatform result = new PlatformDetector().Detect(file);
            Assert.Equal(EPlatform.SegaCd, result.Platform);
            Assert.Equal("SEGADISCSYSTEM", result.Signature);
        } finally {
            File.Delete(file);
        }
    }

    [Fact]
    public void Detect_Iso9660FallsBackToDataDisc() {
        string file = WriteHeader(new string('\0', 100) + "CD001 stuff");
        try {
            Assert.Equal(EPlatform.DataDisc, new PlatformDetector().Detect(file).Platform);
        } finally {
            File.Delete(file);
        }
    }

    [Fact]
    public void Detect_UnknownContent_ReturnsUnknown() {
        string file = WriteHeader("just some random bytes with no signature");
        try {
            Assert.Equal(EPlatform.Unknown, new PlatformDetector().Detect(file).Platform);
        } finally {
            File.Delete(file);
        }
    }

    [Fact]
    public void Detect_NullOrMissingPath_ReturnsUnknown() {
        Assert.Equal(EPlatform.Unknown, new PlatformDetector().Detect(null).Platform);
        Assert.Equal(EPlatform.Unknown, new PlatformDetector().Detect("/no/such/file.img").Platform);
    }

    [Fact]
    public void Summary_UnknownProducesNoLines() {
        Assert.Empty(PlatformSummary.Lines(new DetectedPlatform(EPlatform.Unknown, null)));
    }

    [Fact]
    public void Summary_SegaCdIncludesSignatureAndCaveat() {
        IReadOnlyList<string> lines = PlatformSummary.Lines(new DetectedPlatform(EPlatform.SegaCd, "SEGADISCSYSTEM"));
        Assert.Equal(2, lines.Count);
        Assert.Contains("Sega CD", lines[0]);
        Assert.Contains("SEGADISCSYSTEM", lines[0]);
        Assert.Contains("CD-R only", lines[1]);
    }

    private static string WriteHeader(string content) {
        string path = Path.GetTempFileName();
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(content));
        return path;
    }
}
