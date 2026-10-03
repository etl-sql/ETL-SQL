using System.Text.Json.Nodes;
using ETL_SQL.Reporting.Semantics;

namespace ETL_SQL.Tests.Reporting.GrammarOfGraphics;

[Trait("CompatBreak", "0.20.0")]
public sealed class EnvelopeMigrationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MigrationChangesOnlyAnExactLegacyEnvelope(bool plot, bool legacy)
    {
        var text = plot ? ChartContractVersions.LegacyPlotPlanSchema : ChartContractVersions.LegacyChartSpecSchema;
        var chart = GrammarOfGraphicsContractFixtures.ChartSpec() with { Title = text };
        var plan = GrammarOfGraphicsContractFixtures.PlotPlan() with { AccessibleSummary = text };
        var envelope = JsonNode.Parse(plot ? ChartContractSerializer.Serialize(plan) : ChartContractSerializer.Serialize(chart))!;
        if (legacy)
        {
            envelope["schema"] = text;
            envelope["version"] = 1;
        }
        var json = envelope.ToJsonString();
        if (plot)
        {
            var result = ChartContractSerializer.DeserializePlotPlan(json);
            Assert.Equal(ChartContractVersions.PlotPlanCurrent, result.Version);
            Assert.Equal(text, result.AccessibleSummary);
        }
        else
        {
            var result = ChartContractSerializer.DeserializeChartSpec(json);
            Assert.Equal(ChartContractVersions.ChartSpecCurrent, result.Version);
            Assert.Equal(text, result.Title);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MismatchedLegacyVersionRemainsRejected(bool plot)
    {
        var envelope = JsonNode.Parse(plot
            ? ChartContractSerializer.Serialize(GrammarOfGraphicsContractFixtures.PlotPlan())
            : ChartContractSerializer.Serialize(GrammarOfGraphicsContractFixtures.ChartSpec()))!;
        envelope["schema"] = plot ? ChartContractVersions.LegacyPlotPlanSchema : ChartContractVersions.LegacyChartSpecSchema;
        if (plot) Assert.Throws<InvalidDataException>(() => ChartContractSerializer.DeserializePlotPlan(envelope.ToJsonString()));
        else Assert.Throws<InvalidDataException>(() => ChartContractSerializer.DeserializeChartSpec(envelope.ToJsonString()));
    }
}
