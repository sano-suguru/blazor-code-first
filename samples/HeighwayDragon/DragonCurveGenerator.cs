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
    /// time, so the caller preallocates exactly that many <see cref="Point"/>s and this fills them
    /// by enumerating <see cref="GeneratePoints"/> -- one pass, no growable intermediate collection.
    /// </summary>
    public static void FillPoints(Span<Point> destination, int order)
    {
        var expected = VertexCount(order);
        if (destination.Length != expected)
        {
            throw new ArgumentException(
                $"destination must have length {expected} for order {order}, had {destination.Length}.",
                nameof(destination));
        }

        var i = 0;
        foreach (var point in GeneratePoints(order))
        {
            destination[i] = point;
            i++;
        }
    }
}
