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

public sealed class TransposedAspectOffsetTests
{
    private const string Script = """
        CREATE VISUAL Measurement AS CUSTOM (
          SOURCE = #prepared,
          CHART (
            COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
            SCALES (
              distances = LINEAR (CHANNEL = X, MIN = 0, MAX = 10),
              estimates = LINEAR (CHANNEL = Y, MIN = 0, MAX = 10),
              xGroups = BAND (CHANNEL = X_OFFSET, ORDER = ('lower', 'upper')),
              yGroups = BAND (CHANNEL = Y_OFFSET, ORDER = ('left', 'right'))
            ),
            LAYERS (observations = POINT (
              ENCODINGS (
                X = Distance (TYPE = QUANTITATIVE, SCALE = distances),
                Y = Estimate (TYPE = QUANTITATIVE, SCALE = estimates),
                ERROR_LOW = LowerBound (TYPE = QUANTITATIVE, SCALE = estimates),
                ERROR_HIGH = UpperBound (TYPE = QUANTITATIVE, SCALE = estimates),
                TEXT = Caption (TYPE = NOMINAL),
                X_OFFSET = VerticalGroup (TYPE = NOMINAL, SCALE = xGroups),
                Y_OFFSET = HorizontalGroup (TYPE = NOMINAL, SCALE = yGroups)
              )
            ))
          )
        );
        """;

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void OffsetChannels_MovePointsAndIntervalsTogetherWithoutChangingValues(bool facets, bool reverse, bool logarithmic)
    {
        var sql = Script;
        if (facets) sql = sql.Replace("SCALES (", "FACET (WRAP = Distance, COLUMNS = 2), RESOLVE (Y = INDEPENDENT), SCALES (", StringComparison.Ordinal)
            .Replace("estimates = LINEAR (CHANNEL = Y, MIN = 0, MAX = 10)", "estimates = LINEAR (CHANNEL = Y)", StringComparison.Ordinal);
        if (reverse) sql = sql.Replace("CHANNEL = X,", "CHANNEL = X, REVERSE = ON,", StringComparison.Ordinal)
            .Replace("CHANNEL = Y,", "CHANNEL = Y, REVERSE = ON,", StringComparison.Ordinal);
        if (logarithmic) sql = sql.Replace("LINEAR (CHANNEL", "LOGARITHMIC (CHANNEL", StringComparison.Ordinal)
            .Replace("MIN = 0,", "INCLUDE_ZERO = OFF, MIN = 1,", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var resolver = new PlotPlanResolver();
        var original = resolver.Resolve(spec, data, new PlotBounds(0m, 0m, 1000m, 600m));
        foreach (var bounds in new[] { original.Bounds, new PlotBounds(0m, 0m, 800m, 650m) })
        {
            var plan = resolver.Relayout(spec, data, original, bounds);
            var baseline = WithoutOffsets(plan);
            var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
            var baseSvg = XDocument.Parse(new SvgChartRenderer().Render(baseline));
            foreach (var markClass in new[] { "plot-point", "plot-error-bar-stem", "plot-error-bar-cap" })
            {
                var actual = Elements(svg, markClass);
                var expected = Elements(baseSvg, markClass);
                Assert.NotEmpty(actual);
                Assert.Equal(expected.Length, actual.Length);
                var attributes = markClass == "plot-point" ? new[] { "cx", "cy" } : ["x1", "y1", "x2", "y2"];
                for (var index = 0; index < actual.Length; index++)
                {
                    var row = markClass == "plot-error-bar-cap" ? index / 2 : index;
                    var datum = Assert.Single(plan.Layers[0].Data, datum => datum.RowIndex == row);
                    foreach (var attribute in attributes)
                        Assert.InRange(Read(actual[index], attribute) - Read(expected[index], attribute) -
                            (attribute.Contains('x') ? datum.DisplayOffsetX : datum.DisplayOffsetY), -.002m, .002m);
                }
            }
            foreach (var datum in plan.Layers[0].Data)
            {
                var viewport = facets ? Assert.Single(plan.Facets, facet => facet.RowIndices.Contains(datum.RowIndex)).CartesianViewport! : plan.CartesianViewport!;
                Assert.Equal((datum.RowIndex == 0 ? .25m : -.25m) * (viewport.Width - 80m), datum.DisplayOffsetX);
                Assert.Equal((datum.RowIndex == 0 ? -.25m : .25m) * (viewport.Height - 100m), datum.DisplayOffsetY);
            }
            Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, bounds)), ChartContractSerializer.Serialize(plan));
            Assert.Equal(TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(baseline), 120).NormalizedText,
                TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 120).NormalizedText);
        }
    }

    [Theory]
    [InlineData("IDENTITY")]
    [InlineData("NUDGE(X = 1, Y = -0.5, UNIT = EM)")]
    [InlineData("NUDGE(X = 0.02, Y = -0.03, UNIT = BAND)")]
    [InlineData("JITTER(X = 0.02, Y = 0.03, KEY = Id, SEED = 42)")]
    public void Placement_ComposesWithOffsetsWithoutChangingRawValues(string position)
    {
        var (spec, data) = Lower(Script.Replace("observations = POINT (", $"observations = POINT (POSITION = {position},", StringComparison.Ordinal));
        var resolver = new PlotPlanResolver();
        var positioned = resolver.Resolve(spec, data);
        var offsetsOnly = resolver.Resolve(spec with { Layers = [spec.Layers[0] with { Position = null }] }, data);
        var noGroupsData = data with
        {
            Columns = data.Columns.Select(column => column.Name is "VerticalGroup" or "HorizontalGroup"
            ? column with { Values = [ChartValue.Null(), ChartValue.Null()] } : column).ToImmutableArray()
        };
        var placementOnly = resolver.Resolve(spec, noGroupsData);
        for (var index = 0; index < positioned.Layers[0].Data.Length; index++)
        {
            var actual = positioned.Layers[0].Data[index];
            var offset = offsetsOnly.Layers[0].Data[index];
            var placement = placementOnly.Layers[0].Data[index];
            Assert.Equal(offset.DisplayOffsetX + placement.DisplayOffsetX, actual.DisplayOffsetX);
            Assert.Equal(offset.DisplayOffsetY + placement.DisplayOffsetY, actual.DisplayOffsetY);
            Assert.Equal(offset.Channels.ToArray(), actual.Channels.ToArray());
        }
        Assert.Equal(offsetsOnly.Scales.SelectMany(scale => scale.Domain), positioned.Scales.SelectMany(scale => scale.Domain));
        Assert.Equal(offsetsOnly.Fallback.Items.ToArray(), positioned.Fallback.Items.ToArray());
    }

    [Fact]
    public void OffsetScaleReversal_ReversesGroupingWithoutChangingPrimaryDomains()
    {
        var (spec, data) = Lower(Script);
        var resolver = new PlotPlanResolver();
        var original = resolver.Resolve(spec, data);
        var reversed = resolver.Resolve(spec with
        {
            Scales = spec.Scales.Select(scale =>
            scale.Channel is FieldChannel.XOffset or FieldChannel.YOffset ? scale with { Reverse = true } : scale).ToImmutableArray()
        }, data);
        for (var index = 0; index < original.Layers[0].Data.Length; index++)
        {
            Assert.Equal(-original.Layers[0].Data[index].DisplayOffsetX, reversed.Layers[0].Data[index].DisplayOffsetX);
            Assert.Equal(-original.Layers[0].Data[index].DisplayOffsetY, reversed.Layers[0].Data[index].DisplayOffsetY);
        }
        Assert.Equal(original.Scales.SelectMany(scale => scale.Domain), reversed.Scales.SelectMany(scale => scale.Domain));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NullOrSingletonGroups_DoNotShiftMarks(bool nullGroups)
    {
        var (spec, data) = Lower(Script);
        spec = spec with
        {
            Scales = spec.Scales.Select(scale => scale.Channel is FieldChannel.XOffset or FieldChannel.YOffset
            ? scale with { CategoryOrder = [] } : scale).ToImmutableArray()
        };
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name is "VerticalGroup" or "HorizontalGroup"
            ? column with { Values = nullGroups ? [ChartValue.Null(), ChartValue.Null()] : [ChartValue.From("only"), ChartValue.From("only")] }
            : column).ToImmutableArray()
        };
        Assert.All(new PlotPlanResolver().Resolve(spec, data).Layers[0].Data, datum =>
        {
            Assert.Equal(0m, datum.DisplayOffsetX);
            Assert.Equal(0m, datum.DisplayOffsetY);
        });
    }

    [Fact]
    public void QuantitativeOffsetBindings_AreRejectedByBothValidators()
    {
        var statement = Parse(Script.Replace("VerticalGroup (TYPE = NOMINAL", "VerticalGroup (TYPE = QUANTITATIVE", StringComparison.Ordinal));
        Assert.Contains(AdvancedChartSemanticValidator.Validate(statement), diagnostic => diagnostic.Line > 0 &&
            diagnostic.Column > 0 && diagnostic.Message.Contains("nominal", StringComparison.OrdinalIgnoreCase));
        var (spec, _) = Lower(Script);
        var invalid = spec with
        {
            Layers = [spec.Layers[0] with { Bindings = spec.Layers[0].Bindings.Select(binding =>
            binding.Channel == FieldChannel.XOffset ? binding with { SemanticKind = DataSemanticKind.Quantitative } : binding).ToImmutableArray() }]
        };
        Assert.Throws<InvalidDataException>(invalid.Validate);
    }

    [Fact]
    public async Task TextAndAuthoringContracts_PreserveOffsetGroups()
    {
        var statement = Parse(Script);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = Script + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Measurement)));";
        var parsed = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(parsed.Error);
        Assert.Equal(statement.ToSql(), Parse(new DesignerScriptPatcher().Patch(source, parsed.DesignState)).ToSql());
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(Script).Tokenize(), Script).Parse());
        Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Measurement" && entry.SourceColumns.Contains("VerticalGroup"));
        var (spec, data) = Lower(Script);
        var text = spec.Layers[0] with
        {
            Id = "labels",
            ZIndex = 1,
            Mark = MarkKind.Text,
            Bindings = spec.Layers[0].Bindings.Where(binding => binding.Channel is not (FieldChannel.ErrorLow or FieldChannel.ErrorHigh)).ToImmutableArray()
        };
        spec = spec with { Layers = [spec.Layers[0], text] };
        var json = ChartContractSerializer.Serialize(spec);
        Assert.Equal(json, ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(json)));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var baseline = WithoutOffsets(plan);
        var svg = new SvgChartRenderer().Render(plan);
        var labels = Elements(XDocument.Parse(svg), "plot-smart-label");
        Assert.Equal(2, labels.Length);
        var points = Elements(XDocument.Parse(svg), "plot-point");
        for (var index = 0; index < labels.Length; index++)
        {
            Assert.Equal(Read(points[index], "cx"), Read(labels[index], "x"));
            Assert.Equal(Read(points[index], "cy") - 7m, Read(labels[index], "y"));
        }
        Assert.All(plan.Fallback.Items, item => Assert.Contains("X_OFFSET:", item.Detail));
        var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 120).NormalizedText;
        Assert.Contains("upper", terminal);
        Assert.Contains("right", terminal);
        var planJson = ChartContractSerializer.Serialize(plan);
        Assert.Equal(planJson, ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(planJson)));
        Assert.Equal(TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(baseline), 100).NormalizedText,
            TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 100).NormalizedText);
        var report = new ReportManifest
        {
            Title = "Measurement",
            Source = "measurement.rptsql",
            Visuals = [new VisualManifest { Name = "Measurement", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = svg }]
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
        Assert.True(planHash == "B37ED92FDF28455E62D315615907DAF4EDDB301BA3CABABE2B636B0031AF7687" && svgHash == "415CF8B551ADF0EB4B42B52B6FB39120E78C1290C844D0D675D3E58E10DCDC7F", $"Plan: {planHash}; SVG: {svgHash}");
    }

    private static PlotPlan WithoutOffsets(PlotPlan plan) => plan with
    {
        Layers = plan.Layers.Select(layer => layer with
        {
            Data = layer.Data.Select(datum => datum with { DisplayOffsetX = 0m, DisplayOffsetY = 0m }).ToImmutableArray()
        }).ToImmutableArray()
    };

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
            Columns = ["Id", "Distance", "Estimate", "LowerBound", "UpperBound", "Caption", "VerticalGroup", "HorizontalGroup"],
            Rows = [["a", "3", "3", "2", "4", "alpha", "upper", "right"], ["b", "7", "7", "6", "8", "beta", "lower", "left"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
