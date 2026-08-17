using System.Runtime.InteropServices;
using BlazorCodeFirst.Samples.HeighwayDragon;

namespace BlazorCodeFirst.Samples.HeighwayDragon.Tests;

public class DragonCurveGeneratorTests
{
    [Fact]
    public void Order0_MatchesKnownSequence()
    {
        var points = DragonCurveGenerator.GeneratePoints(0).ToArray();
        Assert.Equal([new Point(0, 0), new Point(1, 0)], points);
    }

    [Fact]
    public void Order1_MatchesKnownSequence()
    {
        var points = DragonCurveGenerator.GeneratePoints(1).ToArray();
        Assert.Equal([new Point(0, 0), new Point(1, 0), new Point(1, -1)], points);
    }

    [Fact]
    public void Order2_MatchesKnownSequence()
    {
        var points = DragonCurveGenerator.GeneratePoints(2).ToArray();
        Assert.Equal(
            [new Point(0, 0), new Point(1, 0), new Point(1, -1), new Point(0, -1), new Point(0, -2)],
            points);
    }

    [Fact]
    public void Order3_MatchesKnownSequence()
    {
        var points = DragonCurveGenerator.GeneratePoints(3).ToArray();
        Assert.Equal(
            [
                new Point(0, 0), new Point(1, 0), new Point(1, -1), new Point(0, -1), new Point(0, -2),
                new Point(-1, -2), new Point(-1, -1), new Point(-2, -1), new Point(-2, -2)
            ],
            points);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(10)]
    [InlineData(24)]
    public void VertexCount_MatchesFormula(int order)
    {
        Assert.Equal((1 << order) + 1, DragonCurveGenerator.VertexCount(order));
    }

    [Fact]
    public void FillPoints_AgreesWithIterator()
    {
        const int order = 10;
        var expected = DragonCurveGenerator.GeneratePoints(order).ToArray();
        var actual = new Point[DragonCurveGenerator.VertexCount(order)];
        DragonCurveGenerator.FillPoints(actual, order);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FillPoints_ReturnedBoundsMatchSeparateBoundsComputation()
    {
        const int order = 10;
        var points = new Point[DragonCurveGenerator.VertexCount(order)];
        var bounds = DragonCurveGenerator.FillPoints(points, order);
        Assert.Equal(DragonCurveGenerator.Bounds(points), bounds);
    }

    [Fact]
    public void FillPoints_RejectsWrongLengthDestination()
    {
        var destination = new Point[1];
        Assert.Throws<ArgumentException>(() => DragonCurveGenerator.FillPoints(destination, order: 5));
    }

    [Fact]
    public void FillPoints_Order24_ProducesExpectedVertexCount()
    {
        const int order = 24;
        var expected = DragonCurveGenerator.GeneratePoints(order).ToArray();
        var points = new Point[DragonCurveGenerator.VertexCount(order)];
        DragonCurveGenerator.FillPoints(points, order);
        Assert.Equal(16_777_217, points.Length);
        Assert.Equal(expected, points);
    }

    [Fact]
    public void Bounds_ReturnsMinAndMaxOfEachAxis()
    {
        Point[] points = [new(1, -3), new(-2, 4), new(0, 0)];

        var (minX, maxX, minY, maxY) = DragonCurveGenerator.Bounds(points);

        Assert.Equal(-2, minX);
        Assert.Equal(1, maxX);
        Assert.Equal(-3, minY);
        Assert.Equal(4, maxY);
    }

    [Fact]
    public void CastToFloatSpan_InterleavesXAndY()
    {
        var points = new[] { new Point(1f, 2f), new Point(3f, 4f) };
        var floats = MemoryMarshal.Cast<Point, float>(points);
        Assert.Equal<float>([1f, 2f, 3f, 4f], floats.ToArray());
    }
}
