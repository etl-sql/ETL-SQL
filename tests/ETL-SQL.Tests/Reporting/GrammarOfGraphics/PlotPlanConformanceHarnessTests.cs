using System.Collections.Immutable;
using ETL_SQL.Reporting.Semantics;
using Xunit;

namespace ETL_SQL.Tests.Reporting.GrammarOfGraphics;

public sealed class PlotPlanConformanceHarnessTests
{
    [Fact]
    public void RepresentativeBackendProbes_ConformToOneSemanticProjection()
    {
        var plan = GrammarOfGraphicsContractFixtures.PlotPlan();
        var report = PlotPlanConformanceHarness.Evaluate(plan,
        [
            new DelegateBackend("echarts", PlotSemanticProjection.FromPlan),
            new DelegateBackend("native-svg", PlotSemanticProjection.FromPlan),
            new DelegateBackend("terminal", PlotSemanticProjection.FromPlan)
        ]);

        Assert.True(report.IsConformant);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void SemanticDrift_IsAttributedToBackendAndArea()
    {
        var plan = GrammarOfGraphicsContractFixtures.PlotPlan();
        var report = PlotPlanConformanceHarness.Evaluate(plan,
        [
            new DelegateBackend("native-svg", value =>
            {
                var projection = PlotSemanticProjection.FromPlan(value);
                return projection with { SeriesOrder = ["South", "North"] };
            })
        ]);

        var issue = Assert.Single(report.Issues);
        Assert.Equal("native-svg", issue.Backend);
        Assert.Equal("series-order", issue.SemanticArea);
    }

    [Fact]
    public void DuplicateBackendNames_AreRejected()
    {
        var plan = GrammarOfGraphicsContractFixtures.PlotPlan();

        Assert.Throws<InvalidDataException>(() => PlotPlanConformanceHarness.Evaluate(plan,
        [
            new DelegateBackend("svg", PlotSemanticProjection.FromPlan),
            new DelegateBackend("SVG", PlotSemanticProjection.FromPlan)
        ]));
    }

    private sealed record DelegateBackend(
        string Name,
        Func<PlotPlan, PlotSemanticProjection> Projection) : IPlotPlanSemanticBackend
    {
        public PlotSemanticProjection Project(PlotPlan plan) => Projection(plan);
    }
}
