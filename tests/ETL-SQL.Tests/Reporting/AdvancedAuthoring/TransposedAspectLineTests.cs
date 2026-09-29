using System.Collections.Immutable;
using System.Globalization;
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

public sealed class TransposedAspectLineTests
{
    private const string Script = """
        CREATE VISUAL Route AS CUSTOM (
          SOURCE = #prepared,
          CHART (
            COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
            SCALES (distances = LINEAR (CHANNEL = X, MIN = 0, MAX = 10),
                    estimates = LINEAR (CHANNEL = Y, MIN = 0, MAX = 10)),
            LAYERS (route = LINE (
              INHERIT_ENCODINGS = OFF,
              NULL_HANDLING = GAP,
              ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = distances),
                         Y = Estimate (TYPE = QUANTITATIVE, SCALE = estimates)),
              STYLE (INTERPOLATION = 'LINEAR', COLOR = '#112233')
            ))
          )
        );
        """;

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void Line_FollowsPointGeometryAndSourceOrder(bool reverse, bool logarithmic, bool facets)
    {
        var sql = Script;
        if (reverse) sql = sql.Replace("MIN = 0,", "REVERSE = ON, MIN = 0,", StringComparison.Ordinal);
        if (logarithmic) sql = sql.Replace("LINEAR (CHANNEL", "LOGARITHMIC (CHANNEL", StringComparison.Ordinal).Replace("MIN = 0,", "INCLUDE_ZERO = OFF, MIN = 1,", StringComparison.Ordinal);
        if (facets) sql = sql.Replace("SCALES (", "FACET (WRAP = Cohort, COLUMNS = 2), RESOLVE (X = INDEPENDENT, Y = INDEPENDENT), SCALES (", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var resolver = new PlotPlanResolver();
        var original = resolver.Resolve(spec, data);
        foreach (var bounds in new[] { original.Bounds, new PlotBounds(0m, 0m, 1000m, 650m) })
        {
            var plan = resolver.Relayout(spec, data, original, bounds);
            var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
            var symbols = Elements(svg, "plot-line-symbol");
            var oracle = plan with { Layers = plan.Layers.Select(layer => layer with { Mark = MarkKind.Point }).ToImmutableArray() };
            var points = Elements(XDocument.Parse(new SvgChartRenderer().Render(oracle)), "plot-point");
            Assert.Equal(6, symbols.Length);
            for (var row = 0; row < symbols.Length; row++)
            {
                Assert.Equal(Read(points[row], "cx"), Read(symbols[row], "cx"));
                Assert.Equal(Read(points[row], "cy"), Read(symbols[row], "cy"));
                Assert.Equal(row.ToString(CultureInfo.InvariantCulture), (string?)symbols[row].Attribute("data-row-index"));
                Assert.Equal(new[] { "1", "7", "3", "8", "2", "6" }[row], symbols[row].Value);
            }
            var paths = Paths(svg);
            Assert.Equal(facets ? 2 : 1, paths.Length);
            for (var panel = 0; panel < paths.Length; panel++)
            {
                var selected = facets ? points.Skip(panel * 3).Take(3) : points;
                var expected = string.Join(" ", selected.Select((point, index) => $"{(index == 0 ? "M" : "L")} {point.Attribute("cx")!.Value} {point.Attribute("cy")!.Value}"));
                Assert.Equal(expected, (string?)paths[panel].Attribute("d"));
            }
            Assert.Equal(6, plan.Fallback.Items.Length);
            Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, bounds)), ChartContractSerializer.Serialize(plan));
            Assert.Contains("Braille line", TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 140).NormalizedText);
        }
    }

    [Theory]
    [InlineData("Distance")]
    [InlineData("Estimate")]
    public void MissingCoordinate_BreaksTheLine(string field)
    {
        var (spec, data) = Lower(Script);
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == field ? column with
            { Values = column.Values.SetItem(2, ChartValue.Null()), DisplayValues = [] } : column).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
        Assert.Equal(2, Paths(svg).Length);
        Assert.Equal(5, Elements(svg, "plot-line-symbol").Length);
        Assert.DoesNotContain(Elements(svg, "plot-line-symbol"), element => (string?)element.Attribute("data-row-index") == "2");
        Assert.Contains("1 gaps", TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 140).NormalizedText);
    }

    [Theory]
    [InlineData("NULL_HANDLING = GAP", "NULL_HANDLING = CONNECT")]
    [InlineData("NULL_HANDLING = GAP", "NULL_HANDLING = ZERO")]
    [InlineData("INTERPOLATION = 'LINEAR'", "INTERPOLATION = 'SMOOTH'")]
    [InlineData("INHERIT_ENCODINGS = OFF,", "INHERIT_ENCODINGS = OFF, POSITION = NUDGE(X = 1, Y = 0, UNIT = EM),")]
    [InlineData("X = Distance", "X_START = Distance")]
    public void UnsupportedForms_HavePositionedDiagnostics(string before, string after)
    {
        Assert.Contains(AdvancedChartSemanticValidator.Validate(Parse(Script.Replace(before, after, StringComparison.Ordinal))), diagnostic =>
            diagnostic.Line > 0 && diagnostic.Column > 0 && diagnostic.Message.Contains("ASPECT_RATIO LINE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AuthoringContractsLineageLabelsAndPdf_PreserveSemanticValues()
    {
        var statement = Parse(Script);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = Script + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Route)));";
        var parsed = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(parsed.Error);
        Assert.Equal(statement.ToSql(), Parse(new DesignerScriptPatcher().Patch(source, parsed.DesignState)).ToSql());
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(Script).Tokenize(), Script).Parse());
        foreach (var field in new[] { "Distance", "Estimate" })
            Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Route" && entry.SourceColumns.Contains(field));
        var (spec, data) = Lower(Script);
        Assert.Equal(ChartContractSerializer.Serialize(spec), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(ChartContractSerializer.Serialize(spec))));
        var invalid = spec with { Layers = [spec.Layers[0] with { Position = new PositionAdjustmentSpec(PositionAdjustmentKind.Nudge, 1m) }] };
        Assert.Contains("ASPECT_RATIO LINE", Assert.Throws<InvalidDataException>(invalid.Validate).Message);
        spec = spec with { Theme = spec.Theme with { Tokens = [.. spec.Theme.Tokens, new StyleToken("DATA_LABELS", "ON")] } };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var svg = new SvgChartRenderer().Render(plan);
        Assert.Equal(svg, new SvgChartRenderer().Render(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
        foreach (var value in new[] { "1", "7", "3", "8", "2", "6" })
            Assert.Contains(XDocument.Parse(svg).Descendants(), element => element.Name.LocalName == "text" && element.Value == value);
        var report = new ReportManifest
        {
            Title = "Route",
            Source = "route.rptsql",
            Visuals = [new VisualManifest { Name = "Route", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = svg }]
        };
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await new PdfExporter().ExportAsync(report))[..4]);
    }

    [Fact]
    public void PlanAndSvg_MatchDeterministicGoldens()
    {
        var (spec, data) = Lower(Script);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        Assert.True(planHash == "4FD2E875F762482C6CED62E639F2D4D91819E6A5335066F71612A333808C98D8" && svgHash == "ABBAD68D2102A2259DB50266B997F56C45FB9D1E5BDEB7AEEAD2088C4313A78A", $"Plan: {planHash}; SVG: {svgHash}");
    }

    [Fact]
    public void MixedPointsAndRectangles_PreserveOneLineAndRectangleIntervals()
    {
        var (spec, data) = Lower(Script);
        var point = spec.Layers[0] with
        {
            Id = "observations",
            Mark = MarkKind.Point,
            Style = [],
            Bindings = [.. spec.Layers[0].Bindings, new FieldBinding(FieldChannel.Color, "Cohort", DataSemanticKind.Nominal)]
        };
        var rectangle = new MarkLayerSpec("region", MarkKind.Rect, 2,
            [FieldBinding.Datum(FieldChannel.XStart, ChartValue.From(2m), DataSemanticKind.Quantitative),
             FieldBinding.Datum(FieldChannel.XEnd, ChartValue.From(8m), DataSemanticKind.Quantitative),
             FieldBinding.Datum(FieldChannel.YStart, ChartValue.From(1m), DataSemanticKind.Quantitative),
             FieldBinding.Datum(FieldChannel.YEnd, ChartValue.From(4m), DataSemanticKind.Quantitative)], []);
        spec = spec with { Bindings = [.. spec.Bindings, point.Bindings[^1]], Layers = [point, spec.Layers[0] with { ZIndex = 1 }, rectangle] };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Single(plan.Layers, layer => layer.Mark == MarkKind.Line);
        Assert.Equal(6, plan.Layers.Single(layer => layer.Mark == MarkKind.Line).Data.Length);
        Assert.Single(Paths(XDocument.Parse(new SvgChartRenderer().Render(plan))));
        var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 140).NormalizedText;
        Assert.Contains("X = 2 to 8; Y = 1 to 4", terminal);
        Assert.Contains("Braille", terminal);
    }

    [Fact]
    public void ConstantCoordinates_PreserveCoincidentRows()
    {
        var sql = Script.Replace("X = Distance (", "X = DATUM(3) (", StringComparison.Ordinal)
            .Replace("Y = Estimate (", "Y = DATUM(5) (", StringComparison.Ordinal);
        Assert.Equal(Parse(sql).ToSql(), Parse(Parse(sql).ToSql()).ToSql());
        var (spec, data) = Lower(sql);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
        var symbols = Elements(svg, "plot-line-symbol");
        Assert.Equal(6, symbols.Length);
        Assert.Single(Paths(svg));
        Assert.Single(symbols.Select(element => (Read(element, "cx"), Read(element, "cy"))).Distinct());
        Assert.Equal(6, plan.Fallback.Items.Length);
    }

    [Theory]
    [InlineData("nullHandling", "CONNECT")]
    [InlineData("nullHandling", "ZERO")]
    [InlineData("INTERPOLATION", "SMOOTH")]
    [InlineData("INTERPOLATION", "STEP_BEFORE")]
    public void ContractRejectsUnsupportedLinePolicies(string name, string value)
    {
        var (spec, _) = Lower(Script);
        spec = spec with
        {
            Layers = [spec.Layers[0] with { Style = spec.Layers[0].Style.Select(token =>
            token.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ? token with { Value = value } : token).ToImmutableArray() }]
        };
        Assert.Contains("ASPECT_RATIO LINE", Assert.Throws<InvalidDataException>(spec.Validate).Message);
    }

    private static XElement[] Elements(XDocument document, string name) => document.Descendants().Where(element => (string?)element.Attribute("class") == name).ToArray();
    private static XElement[] Paths(XDocument document) => document.Descendants().Where(element => element.Name.LocalName == "path" && (string?)element.Attribute("stroke") == "#112233" && (string?)element.Attribute("fill") == "none").ToArray();
    private static decimal Read(XElement element, string name) => decimal.Parse(element.Attribute(name)!.Value, CultureInfo.InvariantCulture);
    private static CreateVisualStatement Parse(string sql)
    {
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        return Assert.Single(script.Statements.OfType<CreateVisualStatement>());
    }
    private static (ChartSpec Spec, ChartDataSet Data) Lower(string sql)
    {
        var statement = Parse(sql);
        Assert.Empty(AdvancedChartSemanticValidator.Validate(statement));
        var manifest = new VisualManifest
        {
            Name = "Route",
            Columns = ["Distance", "Estimate", "Cohort"],
            Rows = [["8", "1", "A"], ["2", "7", "A"], ["5", "3", "A"], ["1", "8", "B"], ["9", "2", "B"], ["4", "6", "B"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
