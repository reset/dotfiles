using BurnDisc.Infrastructure;
using BurnDisc.Model;

namespace BurnDisc.Tests;

public sealed class BurnHistoryTests {
    [Fact]
    public void RecordThenLookup_RoundTripsThroughDisk() {
        string path = Path.Combine(Path.GetTempPath(), $"burn-hist-{Guid.NewGuid():N}.json");
        try {
            new BurnHistory(path).Record(new DiscFingerprint(42, 358), "Ecco - The Tides of Time (USA)");

            // A fresh instance reads the persisted file — cross-session behavior.
            BurnHistory reloaded = new(path);
            Assert.Equal("Ecco - The Tides of Time (USA)", reloaded.Lookup(new DiscFingerprint(42, 358)));
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void Lookup_ToleratesOneMibRounding_ButNotTrackMismatch() {
        string path = Path.Combine(Path.GetTempPath(), $"burn-hist-{Guid.NewGuid():N}.json");
        try {
            BurnHistory history = new(path);
            history.Record(new DiscFingerprint(42, 358), "Ecco");

            Assert.Equal("Ecco", history.Lookup(new DiscFingerprint(42, 359))); // +1 MiB rounding
            Assert.Null(history.Lookup(new DiscFingerprint(42, 362)));           // too far off
            Assert.Null(history.Lookup(new DiscFingerprint(40, 358)));           // different track count
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void Record_ReplacesTitleForSameFingerprint() {
        string path = Path.Combine(Path.GetTempPath(), $"burn-hist-{Guid.NewGuid():N}.json");
        try {
            BurnHistory history = new(path);
            history.Record(new DiscFingerprint(42, 358), "Old");
            history.Record(new DiscFingerprint(42, 358), "New");
            Assert.Equal("New", history.Lookup(new DiscFingerprint(42, 358)));
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void Lookup_MissingFile_ReturnsNull() {
        BurnHistory history = new(Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.json"));
        Assert.Null(history.Lookup(new DiscFingerprint(1, 1)));
    }

    [Theory]
    [InlineData("TEST", true)]
    [InlineData("MEGADRIVE_GAME_SPECIAL", true)]
    [InlineData("Audio CD", true)]
    [InlineData("SEGA_ANYTHING", true)]
    [InlineData("", true)]
    [InlineData("Ecco - The Tides of Time", false)]
    [InlineData("FINAL_FIGHT", false)]
    public void DiscLabels_IsPlaceholder(string label, bool expected) {
        Assert.Equal(expected, DiscLabels.IsPlaceholder(label));
    }
}
