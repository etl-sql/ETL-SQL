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

        Assert.Equal("5e2806d46818b8a16a4d2d5ee46446e5e68f49ebc2d9e09d7fc30de0cc41ff90", Fingerprint(json));
        Assert.Equal(json, ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(json)));
    }

    [Fact]
    public void TypedChartData_PreservesRawTypesNullsAndDisplayValues()
    {
        var value = GrammarOfGraphicsContractFixtures.ChartData();
        var json = ChartContractSerializer.Serialize(value);
        var roundTrip = ChartContractSerializer.DeserializeChartData(json);

        Assert.Equal("d5a71c729f99833df2597784c7f33bd3f71b1b0c87615c2a09ccb4da522bbed7", Fingerprint(json));
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

        Assert.Equal("1e5a2d959955bbb62e623d67aaf6bf7cd5b17e03760d9dd9c22b4d95d806b0bd", Fingerprint(json));
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
            .Replace(ChartContractVersions.PlotPlanSchema, "https://etl-sql.org/schemas/reporting/plot-plan/v2", StringComparison.Ordinal)
            .Replace("\"version\": 3", "\"version\": 2", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => ChartContractSerializer.DeserializePlotPlan(json));
    }

    [Fact]
    public void Deserialization_RejectsVersionOneChartAndPlotContracts()
    {
        var chartJson = ChartContractSerializer.Serialize(GrammarOfGraphicsContractFixtures.ChartSpec())
            .Replace(ChartContractVersions.ChartSpecSchema, "https://etl-sql.org/schemas/reporting/chart-spec/v1", StringComparison.Ordinal)
            .Replace("\"version\": 2", "\"version\": 1", StringComparison.Ordinal);
        var plotJson = ChartContractSerializer.Serialize(GrammarOfGraphicsContractFixtures.PlotPlan())
            .Replace(ChartContractVersions.PlotPlanSchema, "https://etl-sql.org/schemas/reporting/plot-plan/v1", StringComparison.Ordinal)
            .Replace("\"version\": 3", "\"version\": 1", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => ChartContractSerializer.DeserializeChartSpec(chartJson));
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

    // Line endings normalized: indented JSON uses the platform newline.
    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal)))).ToLowerInvariant();
}
