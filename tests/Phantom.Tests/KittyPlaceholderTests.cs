using Phantom.Terminal;
using static Phantom.Terminal.KittyPlaceholders;

namespace Phantom.Tests;

public class KittyPlaceholderTests
{
    [Fact]
    public void DiacriticsEncodeIndexes()
    {
        Assert.Equal(0, DiacriticIndex(0x0305));
        Assert.Equal(1, DiacriticIndex(0x030D));
        Assert.Equal(296, DiacriticIndex(0x1D244));
        Assert.Null(DiacriticIndex('A'));
    }

    [Fact]
    public void ColorsEncodeIds()
    {
        Assert.Equal(42u, ColorId(1, 42)); // palette
        Assert.Equal(0x015618u, ColorId(2, 0x18_56_01)); // rgb bytes r=0x01 g=0x56 b=0x18
        Assert.Equal(0u, ColorId(0, 123));
    }

    [Fact]
    public void ParsesRowColumnAndHighByte()
    {
        Assert.Equal(new Cell(7, null, 0, 2, 3), Parse([0x030E, 0x0310], 7, 0));
        Assert.Equal(new Cell(7, 1, 3, 0, 0), Parse([0x0305, 0x0305, 0x030D], 7, 3));
        Assert.Equal(new Cell(7, null, 0, null, null), Parse([], 7, 0));
        Assert.Equal(new Cell(7, null, 0, null, 1), Parse(['x', 0x030D], 7, 0)); // bad row mark ignored
    }

    [Fact]
    public void CellsWithoutMarksContinueTheRun()
    {
        var a = new Cell(9, null, 0, 1, 0);
        var more = new Cell(9, null, 0, null, null);
        var other = new Cell(4, null, 0, 0, 0);
        var runs = new List<Run>();
        Runs(new Cell?[] { null, a, more, more, null, other, more }, runs);

        Assert.Equal([new Run(1, 9, 0, 1, 0, 3), new Run(5, 4, 0, 0, 0, 1), new Run(6, 9, 0, 0, 0, 1)], runs);
    }

    [Fact]
    public void ExplicitColumnsMustBeConsecutive()
    {
        var runs = new List<Run>();
        Runs(new Cell?[] { new Cell(1, null, 0, 0, 0), new Cell(1, null, 0, 0, 1), new Cell(1, null, 0, 0, 5) }, runs);
        Assert.Equal([new Run(0, 1, 0, 0, 0, 2), new Run(2, 1, 0, 0, 5, 1)], runs);
    }

    [Fact]
    public void HighByteExtendsTheId()
    {
        var runs = new List<Run>();
        Runs(new Cell?[] { new Cell(1, 2, 0, 0, 0) }, runs);
        Assert.Equal(0x0200_0001u, runs[0].ImageId);
    }

    [Fact]
    public void ExactFitSlicesByCell()
    {
        // 100x100 image in a 4x2 grid of 10x20 cells (40x40 px): no margins.
        var slice = Geometry(new Run(0, 1, 0, Row: 1, Col: 0, Width: 4), 100, 100, 4, 2, 10, 20);
        Assert.Equal(new Slice(0, 0, 40, 20, 0, 50, 100, 50), slice);
    }

    [Fact]
    public void WideImagesAreCenteredVertically()
    {
        // 200x100 image in a 2x2 grid of 10x10 cells: 20x10 px image, 5 px bands above and below.
        Assert.Equal(new Slice(0, 5, 20, 5, 0, 0, 200, 50), Geometry(new Run(0, 1, 0, 0, 0, 2), 200, 100, 2, 2, 10, 10));
        Assert.Equal(new Slice(0, 0, 20, 5, 0, 50, 200, 50), Geometry(new Run(0, 1, 0, 1, 0, 2), 200, 100, 2, 2, 10, 10));
    }

    [Fact]
    public void TallImagesAreCenteredHorizontally()
    {
        // 100x200 image in a 4x1 grid of 10x20 cells: 10x20 px image centered in 40 px.
        Assert.Null(Geometry(new Run(0, 1, 0, 0, 0, 1), 100, 200, 4, 1, 10, 20)); // all margin
        Assert.Equal(new Slice(5, 0, 5, 20, 0, 0, 50, 200), Geometry(new Run(1, 1, 0, 0, 1, 1), 100, 200, 4, 1, 10, 20));
    }

    [Fact]
    public void GridDefaultsToTheImageSize()
    {
        // No columns/rows given: 25x15 px at 10x10 cells needs 3x2.
        var slice = Geometry(new Run(0, 1, 0, 0, 0, 3), 25, 15, 0, 0, 10, 10);
        Assert.NotNull(slice);
    }
}
