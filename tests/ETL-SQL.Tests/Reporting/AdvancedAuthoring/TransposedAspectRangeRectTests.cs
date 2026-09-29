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

[Trait("CompatBreak", "0.20")]
public sealed class TransposedAspectRangeRectTests
{
    private const string Script = """
        CREATE VISUAL Measurement AS CUSTOM (
          SOURCE = #prepared,
          CHART (
            COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
            SCALES (
              distances = LINEAR (CHANNEL = X, MIN = 0, MAX = 10),
              estimates = LINEAR (CHANNEL = Y, MIN = 0, MAX = 10)
            ),
            LAYERS (
              observations = POINT (ENCODINGS (
                X = Distance (TYPE = QUANTITATIVE, SCALE = distances),
                Y = Estimate (TYPE = QUANTITATIVE, SCALE = estimates),
                COLOR = Cohort (TYPE = NOMINAL)
              )),
              regions = RECT (
                Z_INDEX = 1,
                INHERIT_ENCODINGS = OFF,
                ENCODINGS (
                  X_START = StartX (TYPE = QUANTITATIVE, SCALE = distances),
                  X_END = EndX (TYPE = QUANTITATIVE, SCALE = distances),
                  Y_START = LowerBound (TYPE = QUANTITATIVE, SCALE = estimates),
                  Y_END = UpperBound (TYPE = QUANTITATIVE, SCALE = estimates)
                ),
                STYLE (COLOR = '#112233')
              )
            )
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
    public void Rectangles_MapBothCornersThroughFacetsAndResize(bool reverse, bool logarithmic, bool facets)
    {
        var sql = Script;
        if (reverse) sql = sql.Replace("MIN = 0,", "REVERSE = ON, MIN = 0,", StringComparison.Ordinal);
        if (logarithmic) sql = sql.Replace("LINEAR (CHANNEL", "LOGARITHMIC (CHANNEL", StringComparison.Ordinal).Replace("MIN = 0,", "INCLUDE_ZERO = OFF, MIN = 1,", StringComparison.Ordinal);
        if (facets) sql = sql.Replace("SCALES (", "FACET (WRAP = Cohort, COLUMNS = 2), RESOLVE (X = INDEPENDENT, Y = INDEPENDENT), SCALES (", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        spec = spec with { Theme = spec.Theme with { Tokens = [.. spec.Theme.Tokens, new StyleToken("LEGEND_POSITION", "LEFT")] } };
        var resolver = new PlotPlanResolver();
        var original = resolver.Resolve(spec, data, new PlotBounds(0m, 0m, 1000m, 650m));
        foreach (var bounds in new[] { original.Bounds, new PlotBounds(0m, 0m, 800m, 600m) })
        {
            var plan = resolver.Relayout(spec, data, original, bounds);
            var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
            var rectangles = Elements(svg, "plot-range-rect");
            Assert.Equal(3, rectangles.Length);
            var layer = Assert.Single(plan.Layers, layer => layer.Mark == MarkKind.Rect);
            Assert.Equal(MarkExtentAxis.None, layer.ExtentAxis);
            var corners = new List<XElement[]>();
            foreach (var end in new[] { false, true })
            {
                var x = end ? FieldChannel.XEnd : FieldChannel.XStart;
                var y = end ? FieldChannel.YEnd : FieldChannel.YStart;
                var mapped = plan with
                {
                    Layers = [layer with
                {
                    Mark = MarkKind.Point,
                    Data = layer.Data.Select(datum => datum with { Channels = datum.Channels
                        .Where(channel => channel.Channel == x || channel.Channel == y)
                        .Select(channel => channel with { Channel = channel.Channel == x ? FieldChannel.X : FieldChannel.Y }).ToImmutableArray() }).ToImmutableArray()
                }]
                };
                corners.Add(Elements(XDocument.Parse(new SvgChartRenderer().Render(mapped)), "plot-point"));
            }
            for (var row = 0; row < rectangles.Length; row++)
            {
                var x1 = Read(corners[0][row], "cx");
                var x2 = Read(corners[1][row], "cx");
                var y1 = Read(corners[0][row], "cy");
                var y2 = Read(corners[1][row], "cy");
                Assert.Equal(Math.Min(x1, x2), Read(rectangles[row], "x"));
                Assert.Equal(Math.Min(y1, y2), Read(rectangles[row], "y"));
                Assert.InRange(Read(rectangles[row], "width") - Math.Abs(x2 - x1), -.002m, .002m);
                Assert.InRange(Read(rectangles[row], "height") - Math.Abs(y2 - y1), -.002m, .002m);
            }
            Assert.Equal(0m, Read(rectangles[2], "width"));
            Assert.Equal(0m, Read(rectangles[2], "height"));
            var descriptions = new[] { "X = 2 to 8; Y = 1 to 4", "X = 9 to 1; Y = 7 to 2", "X = 4 to 4; Y = 6 to 6" };
            var fallback = plan.Fallback.Items.Where(item => item.Detail == "rectangle range").ToArray();
            Assert.Equal(descriptions, fallback.Select(item => item.Value));
            var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 140).NormalizedText;
            for (var row = 0; row < rectangles.Length; row++)
            {
                Assert.Equal(descriptions[row], rectangles[row].Value);
                Assert.Contains(descriptions[row], terminal);
            }
            Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, bounds)), ChartContractSerializer.Serialize(plan));
        }
    }

    [Theory]
    [InlineData("StartX")]
    [InlineData("EndX")]
    [InlineData("LowerBound")]
    [InlineData("UpperBound")]
    public void MissingEndpoint_SkipsRectangleAndReportsGap(string field)
    {
        var (spec, data) = Lower(Script);
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == field
            ? column with { Values = column.Values.SetItem(1, ChartValue.Null()), DisplayValues = [] } : column).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(2, Elements(XDocument.Parse(new SvgChartRenderer().Render(plan)), "plot-range-rect").Length);
        Assert.Contains(plan.Fallback.Items, item => item.Value == "gap" && item.Detail == "null gap");
        Assert.Contains("gap", TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 140).NormalizedText);
    }

    [Fact]
    public void SmallRanges_RetainSubpixelSizeAndBothDomainExtents()
    {
        var (spec, data) = Lower(Script);
        spec = spec with { Theme = spec.Theme with { Tokens = [.. spec.Theme.Tokens, new StyleToken("BAR_MIN_HEIGHT", "20")] } };
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name is "EndX" or "UpperBound"
            ? column with { Values = column.Values.SetItem(2, ChartValue.From(column.Name == "EndX" ? 4.001m : 6.001m)), DisplayValues = [] } : column).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var rectangle = Elements(XDocument.Parse(new SvgChartRenderer().Render(plan)), "plot-range-rect")[2];
        Assert.InRange(Read(rectangle, "width"), .001m, .999m);
        Assert.InRange(Read(rectangle, "height"), .001m, .999m);
        var (unbounded, unboundedData) = Lower(Script.Replace(", MIN = 0, MAX = 10", "", StringComparison.Ordinal)
            .Replace("SCALES (", "FACET (WRAP = Cohort, COLUMNS = 2), RESOLVE (X = INDEPENDENT, Y = INDEPENDENT), SCALES (", StringComparison.Ordinal));
        unboundedData = unboundedData with
        {
            Columns = unboundedData.Columns.Select(column => column.Name is "EndX" or "UpperBound"
            ? column with { Values = column.Values.SetItem(0, ChartValue.From(column.Name == "EndX" ? 20m : 30m)), DisplayValues = [] } : column).ToImmutableArray()
        };
        var resolved = new PlotPlanResolver().Resolve(unbounded, unboundedData);
        foreach (var scales in new[] { resolved.Scales, resolved.Facets[0].Scales })
        {
            Assert.True(PlotPlanResolver.Number(scales.Single(scale => scale.Channel == FieldChannel.X).Domain[^1]) >= 20m);
            Assert.True(PlotPlanResolver.Number(scales.Single(scale => scale.Channel == FieldChannel.Y).Domain[^1]) >= 30m);
        }
    }

    [Theory]
    [InlineData("POSITION = NUDGE(X = 0, Y = 1, UNIT = EM),")]
    [InlineData("POSITION = JITTER(X = 0.1, Y = 0, KEY = Distance, SEED = 3),")]
    [InlineData("CONDITIONS (COLOR WHEN Estimate > 0 THEN '#112233'),")]
    public void UnsupportedPresentation_HasPositionedDiagnostic(string option)
    {
        var statement = Parse(Script.Replace("Z_INDEX = 1,", "Z_INDEX = 1, " + option, StringComparison.Ordinal));
        Assert.Contains(AdvancedChartSemanticValidator.Validate(statement), diagnostic => diagnostic.Line > 0 && diagnostic.Column > 0 && diagnostic.Message.Contains("ASPECT_RATIO RECT", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("X_START", "X")]
    [InlineData("Y_START", "Y")]
    public void PrimaryCoordinates_CannotReplaceEndpointPairs(string before, string after)
    {
        var statement = Parse(Script.Replace(before + " =", after + " =", StringComparison.Ordinal));
        Assert.Contains(AdvancedChartSemanticValidator.Validate(statement), diagnostic => diagnostic.Message.Contains("ASPECT_RATIO RECT", StringComparison.Ordinal));
        var (spec, _) = Lower(Script);
        var oldChannel = before == "X_START" ? FieldChannel.XStart : FieldChannel.YStart;
        var newChannel = after == "X" ? FieldChannel.X : FieldChannel.Y;
        spec = spec with
        {
            Layers = [spec.Layers[1] with { Bindings = spec.Layers[1].Bindings.Select(binding =>
            binding.Channel == oldChannel ? binding with { Channel = newChannel } : binding).ToImmutableArray() }]
        };
        Assert.Contains("ASPECT_RATIO RECT", Assert.Throws<InvalidDataException>(spec.Validate).Message);
    }

    [Fact]
    public void Labels_KeepBothSemanticIntervalsAfterTransposition()
    {
        var (spec, data) = Lower(Script);
        spec = spec with { Theme = spec.Theme with { Tokens = [.. spec.Theme.Tokens, new StyleToken("DATA_LABELS", "ON")] } };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
        foreach (var value in plan.Fallback.Items.Where(item => item.Detail == "rectangle range").Select(item => item.Value))
            Assert.Contains(svg.Descendants(), element => element.Name.LocalName == "text" && element.Value == value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtraColorBinding_IsRejectedByAuthoringAndContract(bool inherited)
    {
        var sql = inherited
            ? Script.Replace("LAYERS (", "ENCODINGS (COLOR = Cohort (TYPE = NOMINAL)), LAYERS (", StringComparison.Ordinal)
                .Replace("INHERIT_ENCODINGS = OFF", "INHERIT_ENCODINGS = ON", StringComparison.Ordinal)
            : Script.Replace("X_START = StartX", "COLOR = Cohort (TYPE = NOMINAL), X_START = StartX", StringComparison.Ordinal);
        Assert.Contains(AdvancedChartSemanticValidator.Validate(Parse(sql)), diagnostic =>
            diagnostic.Line > 0 && diagnostic.Column > 0 && diagnostic.Message.Contains("ASPECT_RATIO RECT", StringComparison.Ordinal));
        var (spec, _) = Lower(Script);
        var rectangle = spec.Layers[1];
        spec = spec with { Layers = [rectangle with { Bindings = [.. rectangle.Bindings, new FieldBinding(FieldChannel.Color, "Cohort", DataSemanticKind.Nominal)] }] };
        Assert.Contains("ASPECT_RATIO RECT", Assert.Throws<InvalidDataException>(spec.Validate).Message);
    }

    [Theory]
    [InlineData("NOMINAL")]
    [InlineData("ORDINAL")]
    public void NonquantitativeEndpoint_IsRejectedByAuthoringAndContract(string kind)
    {
        var sql = Script.Replace("X_START = StartX (TYPE = QUANTITATIVE", "X_START = StartX (TYPE = " + kind, StringComparison.Ordinal);
        Assert.Contains(AdvancedChartSemanticValidator.Validate(Parse(sql)), diagnostic => diagnostic.Message.Contains("ASPECT_RATIO RECT", StringComparison.Ordinal));
        var (spec, _) = Lower(Script);
        var rectangle = spec.Layers[1];
        spec = spec with
        {
            Layers = [rectangle with { Bindings = rectangle.Bindings.Select(binding => binding.Channel == FieldChannel.XStart
            ? binding with { SemanticKind = kind == "NOMINAL" ? DataSemanticKind.Nominal : DataSemanticKind.Ordinal } : binding).ToImmutableArray() }]
        };
        Assert.Throws<InvalidDataException>(spec.Validate);
    }

    [Fact]
    public async Task ConstantsAuthoringContractsAndPdf_PreserveBothIntervals()
    {
        var sql = Script.Replace("StartX (", "DATUM(2) (", StringComparison.Ordinal).Replace("EndX (", "DATUM(8) (", StringComparison.Ordinal)
            .Replace("LowerBound (", "DATUM(1) (", StringComparison.Ordinal).Replace("UpperBound (", "DATUM(4) (", StringComparison.Ordinal);
        var statement = Parse(sql);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = sql + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Measurement)));";
        var parsed = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(parsed.Error);
        Assert.Equal(statement.ToSql(), Parse(new DesignerScriptPatcher().Patch(source, parsed.DesignState)).ToSql());
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(Script).Tokenize(), Script).Parse());
        foreach (var field in new[] { "StartX", "EndX", "LowerBound", "UpperBound" })
            Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Measurement" && entry.SourceColumns.Contains(field));
        var (spec, data) = Lower(sql);
        spec = spec with { Layers = [spec.Layers[1]] };
        Assert.Equal(ChartContractSerializer.Serialize(spec), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(ChartContractSerializer.Serialize(spec))));
        var invalid = spec with { Layers = [spec.Layers[0] with { Position = new PositionAdjustmentSpec(PositionAdjustmentKind.Nudge, 0m, 1m) }] };
        Assert.Contains("ASPECT_RATIO RECT", Assert.Throws<InvalidDataException>(invalid.Validate).Message);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(3, Elements(XDocument.Parse(new SvgChartRenderer().Render(plan)), "plot-range-rect").Length);
        Assert.Equal(3, plan.Fallback.Items.Length);
        Assert.All(plan.Fallback.Items, item => Assert.Equal("X = 2 to 8; Y = 1 to 4", item.Value));
        Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
        var report = new ReportManifest
        {
            Title = "Measurement",
            Source = "measurement.rptsql",
            Visuals = [new VisualManifest { Name = "Measurement", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = new SvgChartRenderer().Render(plan) }]
        };
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await new PdfExporter().ExportAsync(report))[..4]);
    }

    [Fact]
    public void Terminal_UnfacetedRulesKeepRowsOutsideFirstColorGroup()
    {
        var (spec, data) = Lower(Script);
        // This RULE composition was valid before RECT support. The first POINT series owns only rows 0 and 1.
        spec = spec with { Layers = spec.Layers.Select(layer => layer.Mark == MarkKind.Rect ? layer with { Mark = MarkKind.Rule } : layer).ToImmutableArray() };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(new[] { 0, 1 }, plan.Layers[0].Data.Select(datum => datum.RowIndex));
        Assert.Contains(plan.Layers.Single(layer => layer.Mark == MarkKind.Rule).Data, datum => datum.RowIndex == 2);
        var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 140).NormalizedText;
        Assert.Contains("X = 4 to 4; Y = 6 to 6", terminal);
        Assert.Contains("X = 2 to 8; Y = 1 to 4", terminal);
        Assert.Contains("X = 9 to 1; Y = 7 to 2", terminal);
    }

    [Fact]
    public void PlanAndSvg_MatchDeterministicGoldens()
    {
        var (spec, data) = Lower(Script);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        Assert.True(planHash == "68932913300EC5E4D82BF299FEE4EB30A55D1F9F18A504CC628AAC5F592CA179" && svgHash == "38260F65E0EB9522CE44EFCD41400E855EB3D6509BB4153FC04D7B165DEAECFD", $"Plan: {planHash}; SVG: {svgHash}");
    }

    private static XElement[] Elements(XDocument document, string name) => document.Descendants().Where(element => (string?)element.Attribute("class") == name).ToArray();
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
            Name = "Measurement",
            Columns = ["Distance", "Estimate", "Cohort", "LowerBound", "UpperBound", "StartX", "EndX"],
            Rows = [["3", "3", "A", "1", "4", "2", "8"], ["5", "5", "A", "7", "2", "9", "1"], ["7", "7", "B", "6", "6", "4", "4"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
