namespace BlazorCodeFirst.Samples.HeighwayDragon;

public readonly record struct Point(float X, float Y);

public static class DragonCurveGenerator
{
    public static int VertexCount(int order) => (1 << order) + 1;

    /// <summary>
    /// The hot-path entry point: the vertex count for <paramref name="order"/> is known ahead of
    /// time, so the caller preallocates exactly that many <see cref="Point"/>s. Fills the span in a
    /// single pass and tracks bounds inline, so an order-24 fill is one pass over 16M points instead
    /// of a separate bounds sweep. Equivalence with an independently-written iterator is covered by
    /// <c>FillPoints_AgreesWithIterator</c> in the test project.
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
                (dx, dy) = Turn(k, dx, dy);
            }
        }

        return (minX, maxX, minY, maxY);
    }

    /// <summary>
    /// The bit-twiddling turn rule from the JS prototype attached to #295: no recursion, no
    /// string rewriting, and no auxiliary state beyond position, direction, and the step counter.
    /// </summary>
    private static (float dx, float dy) Turn(int k, float dx, float dy) =>
        (k & ((k & -k) << 1)) != 0 ? (-dy, dx) : (dy, -dx);
}
