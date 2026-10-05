namespace Zinc.Tests;

public sealed class CellHeightTests
{
    private static Config With(string value) => new Config().Apply("adjust-cell-height", value)!;

    [Fact]
    public void NothingSetKeepsTheFontsHeight() => Assert.Equal(20u, new Config().AdjustedCellHeight(20, 1.5f));

    [Fact]
    public void APercentageIsOfTheFontsHeight()
    {
        Assert.Equal(26u, With("30%").AdjustedCellHeight(20, 1f));
        Assert.Equal(16u, With("-20%").AdjustedCellHeight(20, 1f));
    }

    [Fact]
    public void PixelsScaleWithTheDisplay()
    {
        Assert.Equal(23u, With("2").AdjustedCellHeight(20, 1.5f)); // 2 px at 100% = 3 device pixels at 150%
        Assert.Equal(22u, With("2px").AdjustedCellHeight(20, 1f));
    }

    [Fact]
    public void NeverBelowHalfTheFontsHeight() => Assert.Equal(10u, With("-90%").AdjustedCellHeight(20, 1f));

    [Fact]
    public void ReadFromTheFontSection()
    {
        var config = ConfigFile.Parse("[font]\nadjust-cell-height = \"25%\"\n");
        Assert.Empty(config.Warnings);
        Assert.Equal((25.0, true), config.Global.AdjustCellHeight);
        Assert.Equal((3.0, false), ConfigFile.Parse("[font]\nadjust-cell-height = 3\n").Global.AdjustCellHeight);
    }
}
