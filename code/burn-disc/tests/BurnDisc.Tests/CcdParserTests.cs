using BurnDisc.Model;
using BurnDisc.Pipeline;

namespace BurnDisc.Tests;

public sealed class CcdParserTests {
    // A 2-track disc: track 1 data at LBA 0, track 2 audio at LBA 12000.
    // Entries 0-2 are TOC metadata (Point 0xa0/a1/a2) and must be ignored.
    private const string TwoTrackCcd = """
        [CloneCD]
        Version=3
        [Disc]
        TocEntries=5
        Sessions=1
        [Entry 0]
        Session=1
        Point=0xa0
        Control=0x04
        PLBA=0
        [Entry 1]
        Session=1
        Point=0xa1
        Control=0x04
        PLBA=0
        [Entry 2]
        Session=1
        Point=0xa2
        Control=0x04
        PLBA=13000
        [Entry 3]
        Session=1
        Point=0x01
        Control=0x04
        PLBA=0
        [Entry 4]
        Session=1
        Point=0x02
        Control=0x00
        PLBA=12000
        """;

    [Fact]
    public void Parse_ExtractsOnlyRealTracks_SkippingTocMetadata() {
        CueSheet sheet = CcdParser.Parse(TwoTrackCcd, "game.img");

        Assert.Equal(2, sheet.Tracks.Count);
        Assert.Equal(1, sheet.DataTrackCount);
        Assert.Equal(1, sheet.AudioTrackCount);
    }

    [Fact]
    public void Parse_ReadsTrackTypeFromControlBit() {
        CueSheet sheet = CcdParser.Parse(TwoTrackCcd, "game.img");

        DiscTrack track1 = sheet.Tracks.Single(t => t.Number == 1);
        DiscTrack track2 = sheet.Tracks.Single(t => t.Number == 2);

        Assert.Equal(ETrackType.Data, track1.Type);   // Control 0x04
        Assert.Equal(ETrackType.Audio, track2.Type);   // Control 0x00
        Assert.Equal(0, track1.Plba);
        Assert.Equal(12000, track2.Plba);
    }

    [Fact]
    public void Render_EmitsDataTrackAndAudioWithReconstructedPregap() {
        CueSheet sheet = CcdParser.Parse(TwoTrackCcd, "game.img");

        string expected =
            "FILE \"game.img\" BINARY\n" +
            "  TRACK 01 MODE1/2352\n" +
            "    INDEX 01 00:00:00\n" +
            "  TRACK 02 AUDIO\n" +
            "    INDEX 00 02:38:00\n" +   // 12000 - 150 pregap frames
            "    INDEX 01 02:40:00\n";

        Assert.Equal(expected, sheet.Render());
    }

    [Fact]
    public void Parse_IsCaseInsensitiveForEntryHeaders() {
        string lowercased = TwoTrackCcd.Replace("[Entry", "[entry");
        CueSheet sheet = CcdParser.Parse(lowercased, "game.img");
        Assert.Equal(2, sheet.Tracks.Count);
    }
}
