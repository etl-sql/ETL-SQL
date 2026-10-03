using ETL_SQL.Reporting.Semantics;

namespace ETL_SQL.Tests.Reporting.AdvancedAuthoring;

public sealed class CartesianInterpolationTests
{
    private static readonly (decimal X, decimal Y) Before = (-6m, 12m);
    private static readonly (decimal X, decimal Y) Start = (0m, 0m);
    private static readonly (decimal X, decimal Y) End = (12m, 18m);
    private static readonly (decimal X, decimal Y) After = (24m, -6m);

    [Fact]
    public void CubicPassesThroughEndpointsAndKnownInteriorPoint()
    {
        var controls = CartesianInterpolation.CubicControls(Before, Start, End, After);
        Assert.Equal((3m, 1m), controls.First);
        Assert.Equal((8m, 19m), controls.Second);
        Assert.Equal(Start, CartesianInterpolation.CubicPoint(Start, controls.First, controls.Second, End, 0m));
        Assert.Equal(End, CartesianInterpolation.CubicPoint(Start, controls.First, controls.Second, End, 1m));
        Assert.Equal((5.625m, 9.75m), CartesianInterpolation.CubicPoint(Start, controls.First, controls.Second, End, .5m));
    }

    [Fact]
    public void RepeatedRunEndpointsUseOneSidedTangents()
    {
        var controls = CartesianInterpolation.CubicControls(Start, Start, End, End);
        Assert.Equal((2m, 3m), controls.First);
        Assert.Equal((10m, 15m), controls.Second);
        Assert.Equal((6m, 9m), CartesianInterpolation.CubicPoint(Start, controls.First, controls.Second, End, .5m));
        var coincident = CartesianInterpolation.CubicControls(End, End, End, End);
        Assert.Equal((End, End), coincident);
        Assert.Equal(End, CartesianInterpolation.CubicPoint(End, coincident.First, coincident.Second, End, .25m));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CubicCommutesWithAxisMappingTranslationAndTransposition(bool transpose, bool reverse)
    {
        (decimal X, decimal Y) Map((decimal X, decimal Y) point)
        {
            var mapped = (X: 40m + point.X * (reverse ? -2m : 2m), Y: 200m - point.Y * 3m);
            return transpose ? (mapped.Y, mapped.X) : mapped;
        }
        var semantic = CartesianInterpolation.CubicControls(Before, Start, End, After);
        var display = CartesianInterpolation.CubicControls(Map(Before), Map(Start), Map(End), Map(After));
        Assert.Equal(Map(semantic.First), display.First);
        Assert.Equal(Map(semantic.Second), display.Second);
        foreach (var t in new[] { 0m, .25m, .5m, .75m, 1m })
            Assert.Equal(Map(CartesianInterpolation.CubicPoint(Start, semantic.First, semantic.Second, End, t)),
                CartesianInterpolation.CubicPoint(Map(Start), display.First, display.Second, Map(End), t));
    }

    [Fact]
    public void ReversingBoundarySwapsCubicControlsAndParameterDirection()
    {
        var forward = CartesianInterpolation.CubicControls(Before, Start, End, After);
        var backward = CartesianInterpolation.CubicControls(After, End, Start, Before);
        Assert.Equal(forward.First, backward.Second);
        Assert.Equal(forward.Second, backward.First);
        foreach (var t in new[] { 0m, .25m, .5m, .75m, 1m })
            Assert.Equal(CartesianInterpolation.CubicPoint(Start, forward.First, forward.Second, End, t),
                CartesianInterpolation.CubicPoint(End, backward.First, backward.Second, Start, 1m - t));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StepDirectionSwapsWhenTransposingOrReversingBoundary(bool before)
    {
        static (decimal X, decimal Y) Swap((decimal X, decimal Y) point) => (point.Y, point.X);
        var corner = CartesianInterpolation.StepCorner(Start, End, before);
        Assert.Equal(before ? (0m, 18m) : (12m, 0m), corner);
        Assert.Equal(Swap(corner), CartesianInterpolation.StepCorner(Swap(Start), Swap(End), !before));
        Assert.Equal(corner, CartesianInterpolation.StepCorner(End, Start, !before));
    }
}
