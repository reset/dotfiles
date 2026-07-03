using BurnDisc.Model;

namespace BurnDisc.Tests;

public sealed class MsfTests {
    [Theory]
    [InlineData(0, "00:00:00")]
    [InlineData(74, "00:00:74")]
    [InlineData(75, "00:01:00")]     // 1 second == 75 frames
    [InlineData(4500, "01:00:00")]   // 60 seconds
    [InlineData(12000, "02:40:00")]
    public void LbaToMsf_ConvertsSectorsToMinutesSecondsFrames(int lba, string expected) {
        Assert.Equal(expected, Msf.LbaToMsf(lba));
    }

    [Fact]
    public void StandardPregap_IsTwoSeconds() {
        Assert.Equal(150, Msf.StandardPregapFrames);
    }
}
