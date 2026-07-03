using BurnDisc.Cli;

namespace BurnDisc.Tests;

public sealed class CliParserTests {
    [Fact]
    public void Parse_InputFileOnly_DefaultsSpeedAndDryRun() {
        CliOptions options = CliParser.Parse(["game.7z"]);

        Assert.Equal("game.7z", options.InputFile);
        Assert.Null(options.Speed);
        Assert.False(options.DryRun);
    }

    [Fact]
    public void Parse_SpeedAndDryRun() {
        CliOptions options = CliParser.Parse(["--speed", "10", "--dry-run", "game.chd"]);

        Assert.Equal("game.chd", options.InputFile);
        Assert.Equal(10, options.Speed);
        Assert.True(options.DryRun);
    }

    [Theory]
    [InlineData("--DRY-RUN")]
    [InlineData("--Dry-Run")]
    public void Parse_FlagsAreCaseInsensitive(string flag) {
        CliOptions options = CliParser.Parse([flag, "game.iso"]);
        Assert.True(options.DryRun);
    }

    [Fact]
    public void Parse_MissingInputFile_Throws() {
        Assert.Throws<CliUsageException>(() => CliParser.Parse(["--dry-run"]));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    public void Parse_InvalidSpeed_Throws(string speed) {
        Assert.Throws<CliUsageException>(() => CliParser.Parse(["--speed", speed, "game.cue"]));
    }

    [Fact]
    public void Parse_UnknownOption_Throws() {
        Assert.Throws<CliUsageException>(() => CliParser.Parse(["--frobnicate", "game.cue"]));
    }

    [Fact]
    public void Parse_ExtraPositional_Throws() {
        Assert.Throws<CliUsageException>(() => CliParser.Parse(["a.cue", "b.cue"]));
    }

    [Fact]
    public void IsHelpRequested_DetectsHelpFlags() {
        Assert.True(CliParser.IsHelpRequested(["-h"]));
        Assert.True(CliParser.IsHelpRequested(["--HELP"]));
        Assert.False(CliParser.IsHelpRequested(["game.cue"]));
    }
}
