using System.Collections.Immutable;
using System.Globalization;
using System.Xml.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using ETL_SQL.Tests.Reporting.TerminalSemantics;

namespace ETL_SQL.Tests.Reporting.AdvancedAuthoring;

public sealed class TransposedAspectSegmentDataNudgeTests
{
    public static IEnumerable<object[]> Geometries()
    {
        foreach (var shape in new[] { "X_RANGE", "Y_RANGE", "DIAGONAL" })
            foreach (var logarithmic in new[] { false, true })
                foreach (var reverse in new[] { false, true })
                    foreach (var facets in new[] { false, true })
                        yield return [shape, logarithmic, reverse, facets];
    }

    [Theory]
    [MemberData(nameof(Geometries))]
    public void StartAnchor_MapsThroughOriginalScalesAndTranslatesWholeSegment(string shape, bool logarithmic, bool reverse, bool facets)
    {
        var (spec, data) = Lower(shape, logarithmic, reverse, facets);
        var baselineSpec = spec with { Layers = spec.Layers.Select(layer => layer with { Position = null }).ToImmutableArray() };
        var resolver = new PlotPlanResolver();
        var original = resolver.Resolve(spec, data, new PlotBounds(0m, 0m, 1000m, 650m));
        foreach (var bounds in new[] { original.Bounds, new PlotBounds(0m, 0m, 800m, 600m) })
        {
            var plan = resolver.Relayout(spec, data, original, bounds);
            var baseline = resolver.Resolve(baselineSpec, data, bounds);
            Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(plan with
            {
                Scales = baseline.Scales,
                Facets = baseline.Facets,
                Fallback = baseline.Fallback
            }));
            var rule = Assert.Single(plan.Layers, layer => layer.Mark == MarkKind.Rule);
            var baselineRule = Assert.Single(baseline.Layers, layer => layer.Mark == MarkKind.Rule);
            Assert.Equal(baselineRule.Data.SelectMany(datum => datum.Channels), rule.Data.SelectMany(datum => datum.Channels));
            // Shift only the authored start on the original resolved scales. The oracle has no offsets.
            var mapped = baseline with
            {
                Layers = baseline.Layers.Select(layer => layer.Mark != MarkKind.Rule ? layer : layer with
                {
                    Data = layer.Data.Select(datum => datum with
                    {
                        Channels = datum.Channels.Select(channel => channel.Channel is FieldChannel.X or FieldChannel.XStart or FieldChannel.Y or FieldChannel.YStart
                            ? channel with
                            {
                                Value = ChartValue.From(PlotPlanResolver.Number(channel.Value)!.Value +
                                (channel.Channel is FieldChannel.X or FieldChannel.XStart ? -.5m : .5m))
                            } : channel).ToImmutableArray()
                    }).ToImmutableArray()
                }).ToImmutableArray()
            };
            var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
            var actual = Elements(svg, "plot-range-rule");
            var expected = Elements(XDocument.Parse(new SvgChartRenderer().Render(mapped)), "plot-range-rule");
            var unshifted = Elements(XDocument.Parse(new SvgChartRenderer().Render(baseline)), "plot-range-rule");
            var labels = Elements(svg, "plot-reference-rule-label");
            Assert.Equal(3, actual.Length);
            Assert.Equal(3, expected.Length);
            Assert.Equal(3, labels.Length);
            for (var row = 0; row < actual.Length; row++)
            {
                foreach (var axis in new[] { "x", "y" })
                {
                    Assert.InRange(Read(actual[row], axis + "1") - Read(expected[row], axis + "1"), -.002m, .002m);
                    var oldSpan = Read(unshifted[row], axis + "2") - Read(unshifted[row], axis + "1");
                    var newSpan = Read(actual[row], axis + "2") - Read(actual[row], axis + "1");
                    Assert.InRange(newSpan - oldSpan, -.002m, .002m);
                }
                Assert.InRange(Read(labels[row], "x") - Read(actual[row], "x2") - 4m, -.002m, .002m);
                Assert.InRange(Read(labels[row], "y") - Read(actual[row], "y2") + 4m, -.002m, .002m);
            }
            Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, bounds)), ChartContractSerializer.Serialize(plan));
            Assert.Equal(TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(baseline), 140).NormalizedText,
                TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 140).NormalizedText);
        }
    }

    [Theory]
    [InlineData("X_RANGE", true)]
    [InlineData("X_RANGE", false)]
    [InlineData("Y_RANGE", true)]
    [InlineData("Y_RANGE", false)]
    [InlineData("DIAGONAL", true)]
    [InlineData("DIAGONAL", false)]
    public void LogarithmicAnchorAndTarget_MustStayPositive(string shape, bool xAxis)
    {
        var (spec, data) = Lower(shape, logarithmic: true);
        spec = spec with
        {
            Layers = spec.Layers.Select(layer => layer.Mark != MarkKind.Rule ? layer : layer with
            {
                Position = new PositionAdjustmentSpec(PositionAdjustmentKind.Nudge, xAxis ? -2m : 0m,
                    xAxis ? 0m : -1m, Unit: PositionAdjustmentUnit.Data)
            }).ToImmutableArray()
        };
        var error = Assert.Throws<InvalidOperationException>(() => new PlotPlanResolver().Resolve(spec, data));
        Assert.Contains($"moves {(xAxis ? "X" : "Y")} outside the positive logarithmic domain", error.Message);
    }

    [Theory]
    [InlineData("X_RANGE", "StartX")]
    [InlineData("X_RANGE", "StartY")]
    [InlineData("Y_RANGE", "StartX")]
    [InlineData("Y_RANGE", "StartY")]
    [InlineData("DIAGONAL", "StartX")]
    [InlineData("DIAGONAL", "StartY")]
    public void NonPositiveLogarithmicAnchor_IsRejected(string shape, string field)
    {
        var (spec, data) = Lower(shape, logarithmic: true);
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == field
                ? column with { Values = column.Values.SetItem(0, ChartValue.From(0m)), DisplayValues = [] } : column).ToImmutableArray()
        };
        var error = Assert.Throws<InvalidOperationException>(() => new PlotPlanResolver().Resolve(spec, data));
        Assert.Equal($"Logarithmic scale '{(field == "StartX" ? "distances" : "estimates")}' requires positive values and domain bounds.", error.Message);
    }

    [Theory]
    [InlineData("X_RANGE")]
    [InlineData("Y_RANGE")]
    [InlineData("DIAGONAL")]
    public void MissingEnd_SkipsNudgeEvenWhenAnchorTargetWouldBeInvalid(string shape)
    {
        var (spec, data) = Lower(shape, logarithmic: true);
        spec = spec with
        {
            Layers = spec.Layers.Select(layer => layer.Mark != MarkKind.Rule ? layer : layer with
            {
                Position = new PositionAdjustmentSpec(PositionAdjustmentKind.Nudge, -100m, -100m, Unit: PositionAdjustmentUnit.Data)
            }).ToImmutableArray()
        };
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == (shape == "Y_RANGE" ? "EndY" : "EndX")
                ? column with { Values = [ChartValue.Null(), ChartValue.Null(), ChartValue.Null()], DisplayValues = [] } : column).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Empty(Elements(XDocument.Parse(new SvgChartRenderer().Render(plan)), "plot-range-rule"));
        Assert.DoesNotContain(plan.Fallback.Items, item => item.Group == "Reference");
        Assert.All(plan.Layers.Single(layer => layer.Mark == MarkKind.Rule).Data, datum =>
        {
            Assert.Equal(0m, datum.DisplayOffsetX);
            Assert.Equal(0m, datum.DisplayOffsetY);
        });
    }

    [Fact]
    public void DiagonalEnd_IsTranslatedWithoutApplyingDataNudgeToItsValue()
    {
        var (spec, data) = Lower("DIAGONAL", logarithmic: true);
        // EndX + X nudge is negative, but only the start defines the translation.
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == "EndX"
                ? column with { Values = [ChartValue.From(.25m), ChartValue.From(.25m), ChartValue.From(.25m)], DisplayValues = [] } : column).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(3, Elements(XDocument.Parse(new SvgChartRenderer().Render(plan)), "plot-range-rule").Length);
        Assert.All(plan.Fallback.Items.Where(item => item.Group == "Reference"), item => Assert.Contains("to 0.25", item.Value));
    }

    private static (ChartSpec Spec, ChartDataSet Data) Lower(string shape, bool logarithmic = false, bool reverse = false, bool facets = false)
    {
        var bindings = shape switch
        {
            "X_RANGE" => "X_START = StartX (TYPE = QUANTITATIVE, SCALE = distances), X_END = EndX (TYPE = QUANTITATIVE, SCALE = distances), Y = StartY (TYPE = QUANTITATIVE, SCALE = estimates)",
            "Y_RANGE" => "X = StartX (TYPE = QUANTITATIVE, SCALE = distances), Y_START = StartY (TYPE = QUANTITATIVE, SCALE = estimates), Y_END = EndY (TYPE = QUANTITATIVE, SCALE = estimates)",
            _ => "X_START = StartX (TYPE = QUANTITATIVE, SCALE = distances), X_END = EndX (TYPE = QUANTITATIVE, SCALE = distances), Y_START = StartY (TYPE = QUANTITATIVE, SCALE = estimates), Y_END = EndY (TYPE = QUANTITATIVE, SCALE = estimates)"
        };
        var scale = logarithmic ? "LOGARITHMIC" : "LINEAR";
        var sql = $$"""
            CREATE VISUAL Measurement AS CUSTOM (
              SOURCE = #prepared,
              CHART (
                COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
                {{(facets ? "FACET (WRAP = Cohort, COLUMNS = 2), RESOLVE (X = INDEPENDENT, Y = INDEPENDENT)," : "")}}
                SCALES (
                  distances = {{scale}} (CHANNEL = X, INCLUDE_ZERO = OFF, {{(facets ? "" : "MIN = 0.1, MAX = 10,")}} REVERSE = {{(reverse ? "ON" : "OFF")}}),
                  estimates = {{scale}} (CHANNEL = Y, INCLUDE_ZERO = OFF, {{(facets ? "" : "MIN = 0.1, MAX = 10,")}} REVERSE = {{(reverse ? "ON" : "OFF")}})
                ),
                LAYERS (
                  observations = POINT (ENCODINGS (
                    X = StartX (TYPE = QUANTITATIVE, SCALE = distances),
                    Y = StartY (TYPE = QUANTITATIVE, SCALE = estimates),
                    COLOR = Cohort (TYPE = NOMINAL)
                  )),
                  threshold = RULE (
                    INHERIT_ENCODINGS = OFF,
                    POSITION = NUDGE(X = -0.5, Y = 0.5, UNIT = DATA),
                    ENCODINGS ({{bindings}}),
                    STYLE (LABEL = '<target>', COLOR = '#112233')
                  )
                )
              )
            );
            """;
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        Assert.Empty(AdvancedChartSemanticValidator.Validate(statement));
        var manifest = new VisualManifest
        {
            Name = "Measurement",
            Columns = ["StartX", "EndX", "StartY", "EndY", "Cohort"],
            Rows = [["2", "8", "1", "4", "A"], ["9", "1", "7", "2", "A"], ["4", "4", "6", "6", "B"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        spec = spec with { Theme = spec.Theme with { Tokens = [.. spec.Theme.Tokens, new StyleToken("LEGEND_POSITION", "LEFT")] } };
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }

    private static XElement[] Elements(XDocument document, string name) => document.Descendants().Where(element => (string?)element.Attribute("class") == name).ToArray();
    private static decimal Read(XElement element, string name) => decimal.Parse(element.Attribute(name)!.Value, CultureInfo.InvariantCulture);
}
