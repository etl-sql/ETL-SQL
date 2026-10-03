namespace ETL_SQL.Reporting.Semantics;

/// <summary>
/// Cartesian curve arithmetic. All inputs must use the same coordinate space; callers own scale
/// mapping, placement, run boundaries and the direction of step interpolation after transposition.
/// </summary>
public static class CartesianInterpolation
{
    public static (decimal X, decimal Y) StepCorner(
        (decimal X, decimal Y) start, (decimal X, decimal Y) end, bool before) =>
        before ? (start.X, end.Y) : (end.X, start.Y);

    /// <summary>Catmull-Rom controls for one segment; repeat endpoints at run boundaries.</summary>
    public static ((decimal X, decimal Y) First, (decimal X, decimal Y) Second) CubicControls(
        (decimal X, decimal Y) before, (decimal X, decimal Y) start,
        (decimal X, decimal Y) end, (decimal X, decimal Y) after) =>
        ((start.X + (end.X - before.X) / 6m, start.Y + (end.Y - before.Y) / 6m),
         (end.X - (after.X - start.X) / 6m, end.Y - (after.Y - start.Y) / 6m));

    /// <summary>Evaluates a cubic Bezier segment at a normalized parameter.</summary>
    public static (decimal X, decimal Y) CubicPoint(
        (decimal X, decimal Y) start, (decimal X, decimal Y) first,
        (decimal X, decimal Y) second, (decimal X, decimal Y) end, decimal t)
    {
        var u = 1m - t;
        return (u * u * u * start.X + 3m * u * u * t * first.X + 3m * u * t * t * second.X + t * t * t * end.X,
            u * u * u * start.Y + 3m * u * u * t * first.Y + 3m * u * t * t * second.Y + t * t * t * end.Y);
    }
}
