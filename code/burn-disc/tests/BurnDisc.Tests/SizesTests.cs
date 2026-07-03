using BurnDisc.Model;

namespace BurnDisc.Tests;

public sealed class SizesTests {
    [Theory]
    [InlineData(0, "0B")]
    [InlineData(512, "512B")]
    [InlineData(1024, "1K")]
    [InlineData(1536, "1.5K")]
    [InlineData(1048576, "1M")]
    [InlineData(339527171, "323.8M")]
    [InlineData(1073741824, "1G")]
    public void Human_FormatsBytes(long bytes, string expected) {
        Assert.Equal(expected, Sizes.Human(bytes));
    }
}
