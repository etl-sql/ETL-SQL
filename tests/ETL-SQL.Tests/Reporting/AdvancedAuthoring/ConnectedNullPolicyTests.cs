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

[Trait("CompatBreak", "0.20.0")]
public sealed class ConnectedNullPolicyTests
{
    private static string Sql(string form) => $$"""
        CREATE VISUAL Route AS CUSTOM (SOURCE = #prepared, CHART (
          COORDINATE (TYPE = CARTESIAN),
          SCALES (horizontal = LINEAR (CHANNEL = X, MIN = 0, MAX = 10),
            vertical = LINEAR (CHANNEL = Y, MIN = 0, MAX = 10)),
          LAYERS (route = {{(form == "LINE" ? "LINE" : "AREA")}} (NULL_HANDLING = CONNECT,
            {{(form == "AREA" ? "AREA_BASELINE = ZERO," : "")}}
            ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = horizontal),
              {{(form == "RIBBON" ? "Y_START = Lower (TYPE = QUANTITATIVE, SCALE = vertical), Y_END = Upper (TYPE = QUANTITATIVE, SCALE = vertical)" : "Y = Estimate (TYPE = QUANTITATIVE, SCALE = vertical)")}}),
            STYLE (INTERPOLATION = 'LINEAR'),
            CONDITIONS (COLOR WHEN Flag = 'first' THEN '#ff0000',
              COLOR WHEN Flag = 'middle' THEN '#0000ff' ELSE '#00ff00',
              OPACITY WHEN Flag = 'first' THEN 0 ELSE 1)))
        ));
        """;

    [Theory]
    [InlineData("LINE", false)]
    [InlineData("LINE", true)]
    [InlineData("AREA", false)]
    [InlineData("AREA", true)]
    [InlineData("RIBBON", false)]
    [InlineData("RIBBON", true)]
    public void ConnectSkipsIncompleteCrossSectionsAndKeepsSourceOwnership(string form, bool reverse)
    {
        var (spec, data) = Lower(form);
        spec = spec with { Scales = spec.Scales.Select(scale => scale with { Reverse = reverse }).ToImmutableArray() };
        data = Missing(data, 1, "Distance");
        data = Missing(data, 2, form == "RIBBON" ? "Lower" : "Estimate");
        var resolver = new PlotPlanResolver();
        var plan = resolver.Resolve(spec, data);
        var layer = Assert.Single(plan.Layers);
        Assert.Equal(7, plan.Version);
        Assert.Equal(5, layer.Data.Length);
        Assert.All(layer.Data, datum => Assert.False(datum.IsGap));
        Assert.Equal(new[] { 1, 2 }, plan.Nulls.SkippedRows);
        Assert.Empty(plan.Nulls.GapRows);
        Assert.Equal(new[] { (0, 3), (3, 4) }, layer.Connections.Select(connection => (connection.SourceIndex, connection.DestinationIndex)));
        Assert.Equal(new[] { "#ff0000", "#0000ff" }, layer.Connections.Select(connection => connection.Encodings.Single(value => value.Channel == ConditionalEncodingChannel.Color).Value.Text));
        Assert.Equal("#00ff00", layer.Data[^1].Encodings.Single(value => value.Channel == ConditionalEncodingChannel.Color).Value.Text);
        Assert.All(layer.Connections, connection => connection.Validate(layer));
        var gapSpec = Parse(Sql(form).Replace("NULL_HANDLING = CONNECT", "NULL_HANDLING = GAP"));
        var gap = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(gapSpec, Manifest());
        var gapPlan = resolver.Resolve(gap, data);
        Assert.Single(gapPlan.Layers[0].Connections);
        foreach (var scale in plan.Scales)
            Assert.Equal(gapPlan.Scales.Single(value => value.Id == scale.Id).Domain.ToArray(), scale.Domain.ToArray());
        var eligible = data with
        {
            RowCount = 3,
            Columns = data.Columns.Select(column => column with { Values = new[] { 0, 3, 4 }.Select(index => column.Values[index]).ToImmutableArray(), DisplayValues = [] }).ToImmutableArray()
        };
        foreach (var candidate in new[] { plan, resolver.Relayout(spec, data, plan, new PlotBounds(0m, 0m, 850m, 500m)) })
        {
            var expected = resolver.Resolve(spec, eligible, candidate.Bounds);
            var paths = Paths(candidate);
            Assert.Equal(Paths(expected).Select(path => path.Attribute("d")!.Value), paths.Select(path => path.Attribute("d")!.Value));
            Assert.Equal(new[] { "0", "1" }, paths.Select(path => path.Attribute("opacity")!.Value));
            var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(candidate), 180).NormalizedText;
            foreach (var connection in layer.Connections)
            {
                var description = ConnectedMarkResolver.Describe(connection);
                Assert.Contains(description, terminal);
                Assert.Contains(candidate.Fallback.Items, item => item.Value == description);
                Assert.Contains(paths, path => path.Value == description);
            }
            Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, candidate.Bounds)), ChartContractSerializer.Serialize(candidate));
        }
    }

    [Theory]
    [InlineData("LINE", "Distance")]
    [InlineData("LINE", "Estimate")]
    [InlineData("AREA", "Estimate")]
    [InlineData("RIBBON", "Lower")]
    [InlineData("RIBBON", "Upper")]
    public void LeadingTrailingAndAllNullRowsNeverBecomeInventedCoordinates(string form, string field)
    {
        var (spec, data) = Lower(form);
        var resolver = new PlotPlanResolver();
        var plan = resolver.Resolve(spec, Missing(Missing(data, 0, field), 4, field));
        Assert.Equal(new[] { (1, 2), (2, 3) }, plan.Layers[0].Connections.Select(connection => (connection.SourceIndex, connection.DestinationIndex)));
        for (var index = 0; index < data.RowCount; index++) data = Missing(data, index, field);
        var emptyConnections = resolver.Resolve(spec, data);
        Assert.Empty(emptyConnections.Layers[0].Connections);
        Assert.Empty(Paths(emptyConnections));
        Assert.Equal(5, emptyConnections.Layers[0].Data.Length);
        Assert.Equal(Enumerable.Range(0, 5), emptyConnections.Nulls.SkippedRows);
        var emptyData = data with { RowCount = 0, Columns = data.Columns.Select(column => column with { Values = [], DisplayValues = [] }).ToImmutableArray() };
        Assert.Empty(resolver.Resolve(spec, emptyData).Layers[0].Connections);
    }

    [Theory]
    [InlineData("LINE")]
    [InlineData("AREA")]
    [InlineData("RIBBON")]
    public async Task AuthoringCompatibilityLineageAndStaticExport(string form)
    {
        var sql = Sql(form);
        var statement = Parse(sql);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = sql + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Route)));";
        var design = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(design.Error);
        Assert.Equal(statement.ToSql(), Parse(new DesignerScriptPatcher().Patch(source, design.DesignState)).ToSql());
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(sql).Tokenize(), sql).Parse());
        Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Route" && entry.SourceColumns.Contains("Flag"));
        var (spec, data) = Lower(form);
        Assert.Equal(4, spec.Version);
        Assert.Throws<InvalidDataException>(() => (spec with { Schema = ChartContractVersions.ConnectedChartSpecSchema, Version = 3 }).Validate());
        Assert.Equal(ChartContractSerializer.Serialize(spec), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(ChartContractSerializer.Serialize(spec))));
        var plan = new PlotPlanResolver().Resolve(spec, Missing(data, 1, "Distance"));
        Assert.Throws<InvalidDataException>(() => (plan with { Schema = ChartContractVersions.ConnectedPlotPlanSchema, Version = 5 }).Validate());
        Assert.Throws<InvalidDataException>(() => (plan with { Schema = ChartContractVersions.ScaledRibbonPlotPlanSchema, Version = 6 }).Validate());
        Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
        var layer = plan.Layers[0];
        Assert.Throws<InvalidDataException>(() => (plan with { Layers = [layer with { Connections = layer.Connections.RemoveAt(0) }] }).Validate());
        Assert.Throws<InvalidDataException>(() => (plan with { Nulls = plan.Nulls with { SkippedRows = [] } }).Validate());
        Assert.Throws<InvalidDataException>(() => (plan with { Layers = [layer with { Connections = layer.Connections.SetItem(0, layer.Connections[0] with { DestinationIndex = 1, DestinationRowIndex = 1 }) }] }).Validate());
        Assert.Throws<InvalidDataException>(() => (plan with { Layers = [layer with { Connections = layer.Connections.SetItem(0, layer.Connections[0] with { DestinationIndex = 4, DestinationRowIndex = 4 }) }] }).Validate());
        var projection = PlotPlanConformanceHarness.Evaluate(plan, [new LostConnectionBackend()]);
        Assert.Equal("layers", Assert.Single(projection.Issues).SemanticArea);
        var report = new ReportManifest
        {
            Title = "Route",
            Source = "route.rptsql",
            Visuals =
            [new VisualManifest { Name = "Route", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = new SvgChartRenderer().Render(plan) }]
        };
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await new PdfExporter().ExportAsync(report))[..4]);
    }

    [Theory]
    [InlineData("LINE")]
    [InlineData("AREA")]
    [InlineData("RIBBON")]
    public void Goldens(string form)
    {
        var (spec, data) = Lower(form);
        var plan = new PlotPlanResolver().Resolve(spec, Missing(data, 1, "Distance"));
        static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        var expected = form switch
        {
            "AREA" => ("BE681CC0A2D9FEEFEF7ABF945D255F4AFD91712E6C1C63333B178104CCEFD321", "6A6DD9F521C80FEDD5926136292F968AF4A4D7923688AA7F520948D76F3D71C1"),
            "RIBBON" => ("2C31C202E368EABA1A9F417FA633FB2D66E8FEB6F84B83222A75989F4A6AC7CD", "78015F449893AABE89C68400053B850A2BF35F6E21F4FDECE7F19FFA2F73E3E0"),
            _ => ("ACA3E00146145EA0D0BC18EB72C5F878C0E00AFD58279F0C016F80E7440D49DE", "8795C4F426E60719DB60B14CC3D7C286AD29D2168C951229B940106D0C735DBA")
        };
        Assert.True(planHash == expected.Item1 && svgHash == expected.Item2, $"{form}: Plan: {planHash}; SVG: {svgHash}");
    }

    [Theory]
    [InlineData("LINE")]
    [InlineData("AREA")]
    [InlineData("RIBBON")]
    public void MissingSourceColorDoesNotBorrowSurvivingDestinationColor(string form)
    {
        var sql = Sql(form).Replace("COLOR WHEN Flag = 'first' THEN '#ff0000',", "")
            .Replace(" ELSE '#00ff00'", "");
        var statement = Parse(sql);
        var manifest = Manifest();
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        var data = new VisualChartDataBuilder().Build(spec, manifest);
        data = Missing(Missing(data, 1, "Distance"), 2, "Distance");
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.DoesNotContain(plan.Layers[0].Connections[0].Encodings, encoding => encoding.Channel == ConditionalEncodingChannel.Color);
        Assert.Equal("#0000ff", plan.Layers[0].Connections[1].Encodings.Single(encoding => encoding.Channel == ConditionalEncodingChannel.Color).Value.Text);
        Assert.NotEqual("#0000ff", (string?)Paths(plan)[0].Attribute(form == "LINE" ? "stroke" : "fill"));
    }

    [Theory]
    [InlineData("LINE")]
    [InlineData("AREA")]
    [InlineData("RIBBON")]
    public void CoincidentEligibleRowsRetainDistinctConnections(string form)
    {
        var (spec, data) = Lower(form);
        data = Missing(data, 1, "Distance");
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == "Flag" ? column :
            column with { Values = column.Values.SetItem(4, column.Values[3]) }).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(new[] { (0, 2), (2, 3), (3, 4) }, plan.Layers[0].Connections.Select(connection => (connection.SourceIndex, connection.DestinationIndex)));
        Assert.Equal(3, Paths(plan).Length);
        Assert.Equal(4, plan.Layers[0].Connections[^1].DestinationRowIndex);
        if (form == "LINE")
        {
            var path = Paths(plan)[^1].Attribute("d")!.Value.Split(' ');
            Assert.Equal(path[1..3], path[4..6]);
        }
    }

    [Theory]
    [InlineData("TYPE = CARTESIAN", "TYPE = POLAR")]
    [InlineData("'LINEAR'", "'MONOTONE'")]
    [InlineData("OPACITY WHEN", "SHAPE WHEN")]
    public void UnsupportedCombinationsStayRejected(string before, string after)
    {
        var statement = Parse(Sql("LINE").Replace(before, after, StringComparison.Ordinal));
        Assert.Contains(AdvancedChartSemanticValidator.Validate(statement), diagnostic => diagnostic.Message.Contains("Connected CONDITIONS", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingXHasNoInventedSymbolOrLabel()
    {
        var (spec, data) = Lower("LINE");
        var plan = new PlotPlanResolver().Resolve(spec, Missing(data, 1, "Distance"));
        var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
        var decorations = svg.Descendants().Where(element => (string?)element.Attribute("class") is "plot-line-symbol" or "plot-data-label").ToArray();
        Assert.DoesNotContain(decorations, element => (string?)element.Attribute("data-row-index") == "1");
        Assert.Contains(decorations, element => (string?)element.Attribute("data-row-index") == "0");
        Assert.Equal(new[] { (0, 2), (2, 3), (3, 4) }, plan.Layers[0].Connections.Select(connection => (connection.SourceIndex, connection.DestinationIndex)));
        Assert.Equal(5, plan.Layers[0].Data.Length);
    }

    private sealed record LostConnectionBackend : IPlotPlanSemanticBackend
    {
        public string Name => "lost-connectivity";
        public PlotSemanticProjection Project(PlotPlan plan)
        {
            var projection = PlotSemanticProjection.FromPlan(plan);
            return projection with { Layers = projection.Layers.Select(layer => layer with { Connections = [] }).ToImmutableArray() };
        }
    }

    private static XElement[] Paths(PlotPlan plan) => XDocument.Parse(new SvgChartRenderer().Render(plan)).Descendants()
        .Where(element => (string?)element.Attribute("class") is "plot-conditional-connection" or "plot-conditional-area").ToArray();
    private static ChartDataSet Missing(ChartDataSet data, int index, string field) => data with
    { Columns = data.Columns.Select(column => column.Name == field ? column with { Values = column.Values.SetItem(index, ChartValue.Null()) } : column).ToImmutableArray() };
    private static CreateVisualStatement Parse(string sql)
    {
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        return Assert.Single(script.Statements.OfType<CreateVisualStatement>());
    }
    private static VisualManifest Manifest() => new()
    {
        Name = "Route",
        Columns = ["Distance", "Estimate", "Lower", "Upper", "Flag"],
        Rows =
        [["5", "2", "1", "3", "first"], ["3", "5", "2", "6", "ignored"], ["2", "3", "2", "4", "ignored"],
         ["1", "4", "5", "3", "middle"], ["4", "1", "0", "2", "last"]]
    };
    private static (ChartSpec Spec, ChartDataSet Data) Lower(string form)
    {
        var statement = Parse(Sql(form));
        Assert.Empty(AdvancedChartSemanticValidator.Validate(statement));
        var manifest = Manifest();
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
