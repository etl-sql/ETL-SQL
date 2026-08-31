using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using ETL_SQL.Reporting.Semantics;
using Xunit;

namespace ETL_SQL.Tests.Reporting.GrammarOfGraphics;

public sealed class GrammarOfGraphicsContractTests
{
    [Fact]
    public void ChartSpec_HasStableVersionedSerialization()
    {
        var value = GrammarOfGraphicsContractFixtures.ChartSpec();
        var json = ChartContractSerializer.Serialize(value);

        Assert.Equal("fb16b8da0dddd55dc577afdbf9a64391ff4c55a7fb5a3234b85119024c4794d7", Fingerprint(json));
        Assert.Equal(json, ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(json)));
    }

    [Fact]
    public void TypedChartData_PreservesRawTypesNullsAndDisplayValues()
    {
        var value = GrammarOfGraphicsContractFixtures.ChartData();
        var json = ChartContractSerializer.Serialize(value);
        var roundTrip = ChartContractSerializer.DeserializeChartData(json);

        Assert.Equal("cbe5f1d2b2de5652782072f8d3f65ccf50605e5116ef44e295a61c4f3c5df2e3", Fingerprint(json));
        Assert.Equal(json, ChartContractSerializer.Serialize(roundTrip));
        Assert.Equal(ChartValueKind.Decimal, roundTrip.Columns.Single(column => column.Name == "revenue").ValueKind);
        Assert.Equal(1234.50m, roundTrip.Columns.Single(column => column.Name == "revenue").Values[0].Decimal);
        Assert.Equal("$1,234.50", roundTrip.Columns.Single(column => column.Name == "revenue").DisplayValues[0]);
        Assert.Equal(ChartValueKind.Null, roundTrip.Columns.Single(column => column.Name == "ratio").Values[1].Kind);
        Assert.Equal(TimeSpan.FromHours(-5), roundTrip.Columns.Single(column => column.Name == "instant").Values[0].OffsetDateTime!.Value.Offset);
    }

    [Fact]
    public void PlotPlan_HasStableVersionedSerializationAndDeterministicOrdering()
    {
        var value = GrammarOfGraphicsContractFixtures.PlotPlan();
        var json = ChartContractSerializer.Serialize(value);

        Assert.Equal("597bb68a3043722b1da0057bb93de929f9df0870807f493417bd0edf14186214", Fingerprint(json));
        Assert.Equal(json, ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(json)));
        Assert.Equal(["North", "South"], value.Series.Select(series => series.Key));
        Assert.Equal(["revenue-bars", "target-rule"], value.Layers.Select(layer => layer.Id));
    }

    [Fact]
    public void Deserialization_RejectsUnknownSchemaAndVersion()
    {
        var json = ChartContractSerializer.Serialize(GrammarOfGraphicsContractFixtures.ChartSpec());

        Assert.Throws<InvalidDataException>(() => ChartContractSerializer.DeserializeChartSpec(
            json.Replace(ChartContractVersions.ChartSpecSchema, "https://example.invalid/chart-spec", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => ChartContractSerializer.DeserializeChartSpec(
            json.Replace("\"version\": 2", "\"version\": 3", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("CompatBreak", "0.19")]
    public void PlotPlanVersionTwo_MustBeRegeneratedAfterTooltipRemoval()
    {
        var json = ChartContractSerializer.Serialize(GrammarOfGraphicsContractFixtures.PlotPlan())
            .Replace(ChartContractVersions.PlotPlanSchema, ChartContractVersions.LegacyPlotPlanV2Schema, StringComparison.Ordinal)
            .Replace("\"version\": 3", "\"version\": 2", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => ChartContractSerializer.DeserializePlotPlan(json));
    }

    [Fact]
    public void Deserialization_MigratesVersionOneChartAndPlotContracts()
    {
        var chartJson = ChartContractSerializer.Serialize(GrammarOfGraphicsContractFixtures.ChartSpec())
            .Replace(ChartContractVersions.ChartSpecSchema, ChartContractVersions.LegacyChartSpecSchema, StringComparison.Ordinal)
            .Replace("\"version\": 2", "\"version\": 1", StringComparison.Ordinal);
        var plotJson = ChartContractSerializer.Serialize(GrammarOfGraphicsContractFixtures.PlotPlan())
            .Replace(ChartContractVersions.PlotPlanSchema, ChartContractVersions.LegacyPlotPlanSchema, StringComparison.Ordinal)
            .Replace("\"version\": 2", "\"version\": 1", StringComparison.Ordinal);

        Assert.Equal(ChartContractVersions.ChartSpecCurrent, ChartContractSerializer.DeserializeChartSpec(chartJson).Version);
        Assert.Equal(ChartContractVersions.PlotPlanCurrent, ChartContractSerializer.DeserializePlotPlan(plotJson).Version);
    }

    [Fact]
    public void VersionOneGlobalStack_IsExplicitlyMigratedOrRejectedAtTheResolvedBoundary()
    {
        var chart = GrammarOfGraphicsContractFixtures.ChartSpec() with
        {
            Theme = new ThemeSpec("legacy", [new StyleToken("STACKED", "ON")])
        };
        var chartJson = ChartContractSerializer.Serialize(chart)
            .Replace(ChartContractVersions.ChartSpecSchema, ChartContractVersions.LegacyChartSpecSchema, StringComparison.Ordinal)
            .Replace("\"version\": 2", "\"version\": 1", StringComparison.Ordinal);
        var migrated = ChartContractSerializer.DeserializeChartSpec(chartJson);
        Assert.DoesNotContain(migrated.Theme.Tokens, token => token.Name == "STACKED");
        Assert.Contains(migrated.Layers.SelectMany(layer => layer.Bindings), binding =>
            binding.Channel is FieldChannel.Y or FieldChannel.Y2 && binding.Stack == StackMode.Zero);

        var plot = GrammarOfGraphicsContractFixtures.PlotPlan() with
        {
            Style = [new StyleToken("STACKED", "ON")]
        };
        var plotJson = ChartContractSerializer.Serialize(plot)
            .Replace(ChartContractVersions.PlotPlanSchema, ChartContractVersions.LegacyPlotPlanSchema, StringComparison.Ordinal)
            .Replace("\"version\": 2", "\"version\": 1", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => ChartContractSerializer.DeserializePlotPlan(plotJson));
    }

    [Fact]
    public void TypedChartData_RejectsMismatchedColumnKindsAndDisplayLengths()
    {
        var wrongKind = ChartDataSet.Create("bad", 1,
            [new ChartColumn("value", ChartValueKind.Decimal, DataSemanticKind.Quantitative, [ChartValue.From(1L)], [])]);
        var wrongDisplay = ChartDataSet.Create("bad", 1,
            [new ChartColumn("value", ChartValueKind.Integer, DataSemanticKind.Quantitative, [ChartValue.From(1L)], ["1", "extra"])]);

        Assert.Throws<InvalidDataException>(() => wrongKind.Validate());
        Assert.Throws<InvalidDataException>(() => wrongDisplay.Validate());
    }

    [Fact]
    public void PlotPlan_RejectsNondeterministicSeriesOrder()
    {
        var value = GrammarOfGraphicsContractFixtures.PlotPlan();
        var reversed = value with { Series = value.Series.Reverse().ToImmutableArray() };

        Assert.Throws<InvalidDataException>(() => reversed.Validate());
    }

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
