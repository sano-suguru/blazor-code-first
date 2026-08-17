namespace BlazorCodeFirst.Samples.HeighwayDragon;

public readonly record struct Point(float X, float Y);

public static class DragonCurveGenerator
{
    public static int VertexCount(int order) => (1 << order) + 1;

    /// <summary>
    /// The bit-twiddling turn rule from the JS prototype attached to #295: no recursion, no
    /// string rewriting, and no auxiliary state beyond position, direction, and the step counter.
    /// </summary>
    public static IEnumerable<Point> GeneratePoints(int order)
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
                var isLeft = (k & ((k & -k) << 1)) != 0;
                if (isLeft)
                {
                    (dx, dy) = (-dy, dx);
                }
                else
                {
                    (dx, dy) = (dy, -dx);
                }
            }
        }
    }

    /// <summary>
    /// The hot-path entry point: the vertex count for <paramref name="order"/> is known ahead of
    /// time, so the caller preallocates exactly that many <see cref="Point"/>s. This re-implements
    /// the turn rule from <see cref="GeneratePoints"/> as a direct loop over the span (rather than
    /// enumerating that iterator) and tracks bounds inline, so an order-24 fill is one pass over 16M
    /// points instead of a state-machine-driven pass plus a separate bounds sweep. Equivalence with
    /// <see cref="GeneratePoints"/> is covered by <c>FillPoints_AgreesWithIterator</c>.
    /// </summary>
    public static (double minX, double maxX, double minY, double maxY) FillPoints(Span<Point> destination, int order)
    {
        var expected = VertexCount(order);
        if (destination.Length != expected)
        {
            throw new ArgumentException(
                $"destination must have length {expected} for order {order}, had {destination.Length}.",
                nameof(destination));
        }

        var total = 1 << order;
        var cx = 0f;
        var cy = 0f;
        var dx = 1f;
        var dy = 0f;

        destination[0] = new Point(cx, cy);
        var minX = cx;
        var maxX = cx;
        var minY = cy;
        var maxY = cy;

        for (var k = 1; k <= total; k++)
        {
            cx += dx;
            cy += dy;
            destination[k] = new Point(cx, cy);

            if (cx < minX) minX = cx;
            else if (cx > maxX) maxX = cx;
            if (cy < minY) minY = cy;
            else if (cy > maxY) maxY = cy;

            if (k < total)
            {
                var isLeft = (k & ((k & -k) << 1)) != 0;
                if (isLeft)
                {
                    (dx, dy) = (-dy, dx);
                }
                else
                {
                    (dx, dy) = (dy, -dx);
                }
            }
        }

        return (minX, maxX, minY, maxY);
    }

    public static (double minX, double maxX, double minY, double maxY) Bounds(ReadOnlySpan<Point> points)
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
