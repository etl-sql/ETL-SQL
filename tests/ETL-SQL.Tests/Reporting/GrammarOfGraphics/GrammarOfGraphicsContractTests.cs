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

        Assert.Equal("9f59d59e9642fd299607c8f32cf18ba9d68e9c71b6a43c8d56667f7aa9738efc", Fingerprint(json));
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

        Assert.Equal("71c2f75282e17975a7a16bbb74c022a1c4aeb6cc9599b9912ecfb10a3d5e6f76", Fingerprint(json));
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
            json.Replace("\"version\": 1", "\"version\": 2", StringComparison.Ordinal)));
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
