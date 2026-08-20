using System.Runtime.InteropServices;

namespace BlazorCodeFirst.Samples.HeighwayDragon.Tests;

public class DragonCurveGeneratorTests
{
    [Fact]
    public void Order0_MatchesKnownSequence()
    {
        var points = Fill(0);
        Assert.Equal([new Point(0, 0), new Point(1, 0)], points);
    }

    [Fact]
    public void Order1_MatchesKnownSequence()
    {
        var points = Fill(1);
        Assert.Equal([new Point(0, 0), new Point(1, 0), new Point(1, -1)], points);
    }

    [Fact]
    public void Order2_MatchesKnownSequence()
    {
        var points = Fill(2);
        Assert.Equal(
            [new Point(0, 0), new Point(1, 0), new Point(1, -1), new Point(0, -1), new Point(0, -2)],
            points);
    }

    [Fact]
    public void Order3_MatchesKnownSequence()
    {
        var points = Fill(3);
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
        var expected = GeneratePoints(order).ToArray();
        var actual = Fill(order);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FillPoints_ReturnedBoundsMatchSeparateBoundsComputation()
    {
        const int order = 10;
        var points = new Point[DragonCurveGenerator.VertexCount(order)];
        var bounds = DragonCurveGenerator.FillPoints(points, order);
        Assert.Equal(Bounds(points), bounds);
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
        var points = Fill(24);
        Assert.Equal(16_777_217, points.Length);
    }

    [Fact]
    public void Bounds_ReturnsMinAndMaxOfEachAxis()
    {
        Point[] points = [new(1, -3), new(-2, 4), new(0, 0)];

        var (minX, maxX, minY, maxY) = Bounds(points);

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

    private static Point[] Fill(int order)
    {
        var points = new Point[DragonCurveGenerator.VertexCount(order)];
        DragonCurveGenerator.FillPoints(points, order);
        return points;
    }

    /// <summary>
    /// An independently-written iterator over the dragon curve's bit-twiddling turn rule, kept
    /// only in the test project, so <c>FillPoints_AgreesWithIterator</c> checks
    /// <c>FillPoints</c>' output against a second implementation rather than itself.
    /// </summary>
    private static IEnumerable<Point> GeneratePoints(int order)
    {
        var total = 1 << order;
        var cx = 0f;
        var cy = 0f;
        var dx = 1f;
        var dy = 0f;

        yield return new Point(cx, cy);

        for (var k = 1; k <= total; k++)
        {
            cx += dx;
            cy += dy;
            yield return new Point(cx, cy);

            if (k < total)
            {
                (dx, dy) = (k & ((k & -k) << 1)) != 0 ? (-dy, dx) : (dy, -dx);
            }
        }
    }

    /// <summary>
    /// An independently-written bounds sweep, kept only in the test project, so
    /// <c>FillPoints_ReturnedBoundsMatchSeparateBoundsComputation</c> checks the production
    /// method's inline bounds tracking against a second implementation rather than itself.
    /// </summary>
    private static (double minX, double maxX, double minY, double maxY) Bounds(ReadOnlySpan<Point> points)
    {
        var minX = double.MaxValue;
        var maxX = double.MinValue;
        var minY = double.MaxValue;
        var maxY = double.MinValue;

        foreach (var point in points)
        {
            if (point.X < minX) minX = point.X;
            if (point.X > maxX) maxX = point.X;
            if (point.Y < minY) minY = point.Y;
            if (point.Y > maxY) maxY = point.Y;
        }

        return (minX, maxX, minY, maxY);
    }
}
