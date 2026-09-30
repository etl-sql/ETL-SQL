using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Portal.Services;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using ETL_SQL.Tests.Reporting.TerminalSemantics;

namespace ETL_SQL.Tests.Reporting.AdvancedAuthoring;

public sealed class ConnectedLinePlanTests
{
    private const string Sql = """
        CREATE VISUAL Route AS CUSTOM (SOURCE = #prepared, CHART (
          COORDINATE (TYPE = CARTESIAN),
          LAYERS (route = LINE (NULL_HANDLING = GAP,
            ENCODINGS (X = Distance (TYPE = QUANTITATIVE), Y = Estimate (TYPE = QUANTITATIVE)),
            STYLE (INTERPOLATION = 'LINEAR'),
            CONDITIONS (COLOR WHEN Distance = 1 THEN '#ff0000',
              COLOR WHEN Distance = 2 THEN '#0000ff' ELSE '#00ff00',
              OPACITY WHEN Distance = 1 THEN 0 ELSE 1)))
        ));
        """;

    [Fact]
    public void GuardedPlanPreservesConnectionsAcrossSerializationAndBackends()
    {
        var plan = Create();
        var json = ChartContractSerializer.Serialize(plan);
        var restored = ChartContractSerializer.DeserializePlotPlan(json);
        Assert.Equal(json, ChartContractSerializer.Serialize(restored));
        Assert.Equal(5, restored.Version);
        var paths = XDocument.Parse(new SvgChartRenderer().Render(restored)).Descendants()
            .Where(element => (string?)element.Attribute("class") == "plot-conditional-connection").ToArray();
        Assert.Equal(2, paths.Length);
        Assert.Equal(new[] { "#ff0000", "#0000ff" }, paths.Select(path => (string?)path.Attribute("stroke")));
        Assert.Equal(new[] { "0", "1" }, paths.Select(path => (string?)path.Attribute("opacity")));
        Assert.Equal(new[] { "0", "1" }, paths.Select(path => (string?)path.Attribute("data-source-index")));
        Assert.Equal(new[] { "1", "2" }, paths.Select(path => (string?)path.Attribute("data-destination-index")));
        // The destination of one segment is the source of the next, including coincident endpoints.
        var firstCoordinates = paths[0].Attribute("d")!.Value.Split(' ');
        var secondCoordinates = paths[1].Attribute("d")!.Value.Split(' ');
        Assert.Equal(firstCoordinates[4..6], secondCoordinates[1..3]);
        var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(restored), 180).NormalizedText;
        foreach (var connection in restored.Layers[0].Connections)
        {
            var description = ConnectedMarkResolver.Describe(connection);
            Assert.Contains(description, terminal);
            Assert.Contains(paths, path => path.Value == description);
            Assert.Contains(restored.Fallback.Items, item => item.Value == description);
        }
    }

    [Fact]
    public void GuardRejectsDowngradesOmissionsReorderingAndMissingCoordinates()
    {
        var plan = Create();
        var layer = plan.Layers[0];
        Assert.Throws<InvalidDataException>(() => (plan with
        { Schema = ChartContractVersions.PlotPlanSchema, Version = ChartContractVersions.PlotPlanCurrent }).Validate());
        Assert.Throws<InvalidDataException>(() => (plan with
        { Layers = [layer with { Connections = layer.Connections.RemoveAt(0) }] }).Validate());
        Assert.Throws<InvalidDataException>(() => (plan with
        { Layers = [layer with { Connections = layer.Connections.Reverse().ToImmutableArray() }] }).Validate());
        Assert.Throws<InvalidDataException>(() => (plan with
        { Layers = [layer with { Data = layer.Data.SetItem(0, layer.Data[0] with { Channels = [] }) }] }).Validate());
    }

    [Fact]
    public void GapsHaveNoVisibleOrSemanticConnection()
    {
        var plan = Create(true);
        Assert.Empty(plan.Layers[0].Connections);
        Assert.DoesNotContain("plot-conditional-connection", new SvgChartRenderer().Render(plan));
        Assert.DoesNotContain(plan.Fallback.Items, item => item.Detail == "outgoing connection");
        Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(
            ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
    }

    [Fact]
    public async Task AuthoringRoundTripsLineageRelayoutAndPdf()
    {
        var statement = Parse(Sql);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = Sql + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Route)));";
        var design = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(design.Error);
        Assert.Equal(statement.ToSql(), Parse(new DesignerScriptPatcher().Patch(source, design.DesignState)).ToSql());
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(Sql).Tokenize(), Sql).Parse());
        foreach (var field in new[] { "Distance", "Estimate" })
            Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Route" && entry.SourceColumns.Contains(field));
        var (spec, data) = Lower();
        Assert.Equal(3, spec.Version);
        var json = ChartContractSerializer.Serialize(spec);
        Assert.Equal(json, ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(json)));
        Assert.Throws<InvalidDataException>(() => (spec with { Schema = ChartContractVersions.ChartSpecSchema, Version = 2 }).Validate());
        var resolver = new PlotPlanResolver();
        var plan = resolver.Resolve(spec, data);
        var bounds = new PlotBounds(0m, 0m, 800m, 500m);
        Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, bounds)),
            ChartContractSerializer.Serialize(resolver.Relayout(spec, data, plan, bounds)));
        var report = new ReportManifest
        {
            Title = "Route",
            Source = "route.rptsql",
            Visuals = [new VisualManifest { Name = "Route", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = new SvgChartRenderer().Render(plan) }]
        };
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await new PdfExporter().ExportAsync(report))[..4]);
    }

    [Theory]
    [InlineData("LINE (", "AREA (")]
    [InlineData("TYPE = CARTESIAN", "TYPE = TRANSPOSED_CARTESIAN")]
    [InlineData("NULL_HANDLING = GAP", "NULL_HANDLING = ZERO")]
    [InlineData("'LINEAR'", "'MONOTONE'")]
    [InlineData("OPACITY WHEN", "SIZE WHEN")]
    public void UnsupportedAuthoringIsRejected(string before, string after)
    {
        var statement = Parse(Sql.Replace(before, after, StringComparison.Ordinal));
        Assert.Contains(AdvancedChartSemanticValidator.Validate(statement), diagnostic => diagnostic.Message.Contains("Connected CONDITIONS", StringComparison.Ordinal));
    }

    [Fact]
    public void Goldens()
    {
        var plan = Create();
        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        Assert.True(planHash == "E8A5AC229323BC3878BE212D2167C275BA094925D48D74EE69186AF1C8AAAD61" && svgHash == "5FD9DCE58C95E27A9E1408723E2A9EF5066F6657ED2990994052BEFBDE7686D9", $"Plan: {planHash}; SVG: {svgHash}");
    }

    [Fact]
    public void DirectContractsRejectUnsupportedCombinations()
    {
        var (spec, _) = Lower();
        var layer = spec.Layers[0];
        var invalid = new[]
        {
            spec with { Coordinate = spec.Coordinate with { Kind = CoordinateKind.TransposedCartesian } },
            spec with { Layers = [layer, layer with { Id = "second" }] },
            spec with { Layers = [layer with { Conditions = layer.Conditions.Select(condition => condition with { Channel = ConditionalEncodingChannel.Size }).ToImmutableArray() }] },
            spec with { Layers = [layer with { Style = layer.Style.Where(token => !token.Name.Equals("INTERPOLATION", StringComparison.OrdinalIgnoreCase)).ToImmutableArray() }] },
            spec with { NullHandling = spec.NullHandling with { Default = NullValuePolicy.Zero } }
        };
        Assert.All(invalid, candidate => Assert.Throws<InvalidDataException>(candidate.Validate));
    }

    [Fact]
    public void RepeatedAndDescendingXRemainInSourceOrder()
    {
        var (spec, data) = Lower();
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == "Distance"
            ? column with { Values = [ChartValue.From(3m), ChartValue.From(1m), ChartValue.From(1m)], DisplayValues = [] } : column).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var layer = Assert.Single(plan.Layers);
        Assert.Equal(new[] { 0, 1, 2 }, layer.Data.Select(datum => datum.RowIndex));
        Assert.Equal(new[] { "#00ff00", "#ff0000" }, layer.Connections.Select(connection =>
            connection.Encodings.Single(encoding => encoding.Channel == ConditionalEncodingChannel.Color).Value.Text));
        Assert.Equal(2, XDocument.Parse(new SvgChartRenderer().Render(plan)).Descendants()
            .Count(element => (string?)element.Attribute("class") == "plot-conditional-connection"));
    }

    private static CreateVisualStatement Parse(string sql)
    {
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        return Assert.Single(script.Statements.OfType<CreateVisualStatement>());
    }

    private static PlotPlan Create(bool gap = false)
    {
        var (spec, data) = Lower();
        if (gap) data = data with
        {
            Columns = data.Columns.Select(column => column.Name == "Estimate"
            ? column with { Values = column.Values.SetItem(1, ChartValue.Null()) } : column).ToImmutableArray()
        };
        return new PlotPlanResolver().Resolve(spec, data);
    }

    private static (ChartSpec Spec, ChartDataSet Data) Lower()
    {
        var statement = Parse(Sql);
        Assert.Empty(AdvancedChartSemanticValidator.Validate(statement));
        var manifest = new VisualManifest
        {
            Name = "Route",
            Columns = ["Distance", "Estimate"],
            Rows = [["1", "2"], ["2", "4"], ["3", "3"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
