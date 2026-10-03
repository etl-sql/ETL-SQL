using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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

public sealed class ConnectedZeroPolicyTests
{
    private static string Sql(string form, bool grouped = false, string facet = "NONE") => $$"""
        CREATE VISUAL Route AS CUSTOM (SOURCE = #prepared, CHART (
          COORDINATE (TYPE = CARTESIAN),
          SCALES (unusedX = BAND (CHANNEL = X), unusedY = LINEAR (CHANNEL = Y, MIN = 100, MAX = 200),
            horizontal = LINEAR (CHANNEL = X, MIN = 0, MAX = 10), vertical = LINEAR (CHANNEL = Y, MIN = -10, MAX = 10)),
          {{(facet == "NONE" ? "" : facet == "GRID" ? "FACET (ROW = Cohort, COLUMN = Phase)," : "FACET (WRAP = Cohort, COLUMNS = 2),")}}
          {{(facet == "NONE" ? "" : "RESOLVE (X = INDEPENDENT, Y = INDEPENDENT),")}}
          LAYERS (route = {{(form == "LINE" ? "LINE" : "AREA")}} (
            NULL_HANDLING = ZERO, {{(form == "AREA" ? "AREA_BASELINE = ZERO," : "")}}
            ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = horizontal),
              {{(form == "RIBBON" ? "Y_START = Lower (TYPE = QUANTITATIVE, SCALE = vertical), Y_END = Upper (TYPE = QUANTITATIVE, SCALE = vertical)" : "Y = Estimate (TYPE = QUANTITATIVE, SCALE = vertical)")}}
              {{(grouped ? ", COLOR = Series (TYPE = NOMINAL)" : "")}}),
            STYLE (INTERPOLATION = 'LINEAR', COLOR = '#112233'),
            CONDITIONS (COLOR WHEN Flag = 'first' THEN '#ff0000',
              COLOR WHEN Estimate IS NULL THEN '#008000', COLOR WHEN Flag = 'middle' THEN '#0000ff',
              OPACITY WHEN Flag = 'first' THEN 0 ELSE 1)))
        ));
        """;

    public static IEnumerable<object[]> Cases()
    {
        foreach (var form in new[] { "LINE", "AREA", "RIBBON" })
            foreach (var grouped in new[] { false, true })
                foreach (var facet in new[] { "NONE", "WRAP", "GRID" })
                    foreach (var reverse in new[] { false, true }) yield return [form, grouped, facet, reverse];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ZeroGeometryPreservesRawValuesPartitionsAndSourcePresentation(string form, bool grouped, string facet, bool reverse)
    {
        var (spec, data) = Lower(Sql(form, grouped, facet));
        spec = spec with { Scales = spec.Scales.Select(scale => scale with { Reverse = reverse }).ToImmutableArray() };
        var resolver = new PlotPlanResolver();
        var plan = resolver.Resolve(spec, data);
        Assert.Equal(7, spec.Version);
        Assert.Equal(10, plan.Version);
        Assert.Equal(new ResolvedCartesianAxes("horizontal", "vertical"), plan.CartesianAxes);
        Assert.Equal(form == "RIBBON" ? new[] { 2, 5 } : new[] { 2 }, plan.Nulls.GapRows);
        Assert.Empty(plan.Nulls.SkippedRows);
        Assert.Equal(8, plan.Layers.Sum(layer => layer.Data.Length));
        foreach (var layer in plan.Layers)
        {
            Assert.Empty(layer.ConnectionSkippedRows);
            foreach (var datum in layer.Data)
            {
                foreach (var channel in datum.Channels.Where(value => value.Channel is FieldChannel.X or FieldChannel.Y or FieldChannel.YStart or FieldChannel.YEnd))
                {
                    var field = channel.Channel switch { FieldChannel.X => "Distance", FieldChannel.Y => "Estimate", FieldChannel.YStart => "Lower", _ => "Upper" };
                    Assert.Equal(data.Columns.Single(column => column.Name == field).Values[datum.RowIndex], channel.Value);
                }
                if (datum.IsGap) Assert.Null(datum.ConnectionCoordinates);
                else
                {
                    var coordinates = Assert.IsType<ResolvedConnectionCoordinates>(datum.ConnectionCoordinates);
                    if (form != "RIBBON" && datum.RowIndex is 1 or 5)
                    {
                        Assert.Equal(0m, coordinates.Y);
                        Assert.Equal("#008000", datum.Encodings.Single(encoding => encoding.Channel == ConditionalEncodingChannel.Color).Value.Text);
                    }
                    if (form == "RIBBON") Assert.Null(coordinates.Y);
                }
            }
            foreach (var connection in layer.Connections)
            {
                Assert.Equal(layer.Data[connection.SourceIndex].RowIndex, connection.SourceRowIndex);
                Assert.Equal(layer.Data[connection.SourceIndex].Encodings.ToArray(), connection.Encodings.ToArray());
                Assert.DoesNotContain(2, new[] { connection.SourceRowIndex, connection.DestinationRowIndex });
                if (form == "RIBBON") Assert.DoesNotContain(5, new[] { connection.SourceRowIndex, connection.DestinationRowIndex });
                connection.Validate(layer, plan.Facets.FirstOrDefault(panel => panel.Id == connection.FacetId)?.RowIndices.ToHashSet());
            }
        }
        var paths = Paths(plan);
        Assert.Equal(plan.Layers.Sum(layer => layer.Connections.Length), paths.Length);
        foreach (var layer in plan.Layers)
            foreach (var connection in layer.Connections)
            {
                var description = ConnectedMarkResolver.Describe(connection, plan, layer);
                var path = Assert.Single(paths, path => path.Value == description);
                var panel = plan.Facets.FirstOrDefault(panel => panel.Id == connection.FacetId);
                var frame = panel?.Bounds ?? plan.Bounds;
                var xScale = (panel?.Scales ?? plan.Scales).Single(scale => scale.Id == "horizontal");
                var yScale = (panel?.Scales ?? plan.Scales).Single(scale => scale.Id == "vertical");
                decimal Map(decimal value, ResolvedScale scale, bool vertical)
                {
                    var minimum = PlotPlanResolver.Number(scale.Domain[0])!.Value;
                    var maximum = PlotPlanResolver.Number(scale.Domain[^1])!.Value;
                    var ratio = (value - minimum) / (maximum - minimum);
                    if (scale.Reverse) ratio = 1m - ratio;
                    return vertical ? 40m + (1m - ratio) * (frame.Height - 100m) : 60m + ratio * (frame.Width - 80m);
                }
                string Point(ResolvedConnectionCoordinates coordinates, bool lower = false) =>
                    $"{N(Map(coordinates.X, xScale, false))} {N(Map(form == "RIBBON" ? (lower ? coordinates.YStart : coordinates.YEnd)!.Value : lower ? 0m : coordinates.Y!.Value, yScale, true))}";
                var source = layer.Data[connection.SourceIndex].ConnectionCoordinates!;
                var destination = layer.Data[connection.DestinationIndex].ConnectionCoordinates!;
                var expected = $"M {Point(source)} L {Point(destination)}";
                if (form != "LINE") expected += $" L {Point(destination, true)} L {Point(source, true)} Z";
                Assert.Equal(expected, path.Attribute("d")!.Value);
                Assert.Equal(connection.Encodings.FirstOrDefault(value => value.Channel == ConditionalEncodingChannel.Color)?.Value.Text ?? "#112233", path.Attribute(form == "LINE" ? "stroke" : "fill")!.Value);
            }
        var resized = resolver.Relayout(spec, data, plan, new PlotBounds(0m, 0m, 1000m, 650m));
        Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, resized.Bounds)), ChartContractSerializer.Serialize(resized));
        Assert.Equal(plan.Layers.SelectMany(layer => layer.Data).Select(datum => datum.ConnectionCoordinates), resized.Layers.SelectMany(layer => layer.Data).Select(datum => datum.ConnectionCoordinates));
        Assert.Equal(new SvgChartRenderer().Render(plan), new SvgChartRenderer().Render(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
        var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 220).NormalizedText;
        if (form != "RIBBON")
        {
            Assert.Contains("null Y rendered at zero", terminal);
            Assert.Contains(plan.Fallback.Items, item => item.Detail?.Contains("null Y rendered at zero", StringComparison.Ordinal) == true && item.Value != "0");
        }
        else
        {
            Assert.Contains("to null; gap", terminal);
            Assert.Contains(plan.Fallback.Items, item => item.Value.Contains("to null; gap", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("LINE", 0)]
    [InlineData("LINE", 1)]
    [InlineData("LINE", 8)]
    [InlineData("AREA", 8)]
    [InlineData("RIBBON", 8)]
    public void EmptySingletonAndAllNullValuesHaveExplicitGeometry(string form, int count)
    {
        var (spec, data) = Lower(Sql(form));
        data = data with
        {
            RowCount = count,
            Columns = data.Columns.Select(column => column with
            { Values = column.Values.Take(count).Select((value, index) => column.Name == "Distance" ? ChartValue.From((decimal)index) : column.Name is "Estimate" or "Lower" or "Upper" ? ChartValue.Null() : value).ToImmutableArray(), DisplayValues = [] }).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(10, plan.Version);
        var layer = Assert.Single(plan.Layers);
        Assert.Equal(form == "RIBBON" ? 0 : Math.Max(0, count - 1), layer.Connections.Length);
        Assert.All(layer.Data, datum => Assert.Equal(form == "RIBBON", datum.IsGap));
        if (form != "RIBBON") Assert.All(layer.Data, datum => Assert.Equal(0m, datum.ConnectionCoordinates!.Y));
        Assert.Equal(layer.Connections.Length, Paths(plan).Length);
        if (count == 1) Assert.Contains("row 1: null Y rendered at zero", TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 180).NormalizedText);
    }

    [Theory]
    [InlineData("LINE")]
    [InlineData("AREA")]
    [InlineData("RIBBON")]
    public void MixedPoliciesKeepTheirOwnRawGeometryAndGapDecisions(string form)
    {
        var (spec, data) = Lower(Sql(form, true, "WRAP"));
        var zero = spec.Layers[0];
        MarkLayerSpec Policy(string id, string policy, int z) => zero with { Id = id, ZIndex = z, Style = zero.Style.Select(token => token.Name == "nullHandling" ? token with { Value = policy } : token).ToImmutableArray() };
        spec = spec with { Layers = [zero, Policy("gapped", "GAP", 5), Policy("connected", "CONNECT", 10)] };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var zeroLayers = plan.Layers.Where(ResolvedMarkConnection.FillsNullsWithZero).ToArray();
        Assert.All(plan.Layers.Where(layer => !ResolvedMarkConnection.FillsNullsWithZero(layer)).SelectMany(layer => layer.Data), datum => Assert.Null(datum.ConnectionCoordinates));
        Assert.Contains(plan.Layers.Where(ResolvedMarkConnection.ConnectsAcrossNulls).SelectMany(layer => layer.ConnectionSkippedRows), row => row == (form == "RIBBON" ? 5 : 1));
        Assert.All(zeroLayers, layer => Assert.Empty(layer.ConnectionSkippedRows));
        Assert.Equal(24, plan.Layers.Sum(layer => layer.Data.Length));
        if (form != "RIBBON")
        {
            Assert.Contains(zeroLayers.SelectMany(layer => layer.Connections), connection => connection.SourceRowIndex == 1 && connection.DestinationRowIndex == 5);
            Assert.DoesNotContain(plan.Layers.Where(layer => !ResolvedMarkConnection.FillsNullsWithZero(layer)).SelectMany(layer => layer.Connections), connection => connection.SourceRowIndex == 1 && connection.DestinationRowIndex == 5);
        }
        Assert.Equal(plan.Layers.Sum(layer => layer.Connections.Length), Paths(plan).Length);
    }

    [Fact]
    public void GuardedMetadataRejectsDowngradesInventionsAndLostGeometry()
    {
        var (spec, data) = Lower(Sql("LINE", true, "WRAP"));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Throws<InvalidDataException>((spec with { Version = 6, Schema = ChartContractVersions.ConnectedCompositionChartSpecSchema }).Validate);
        Assert.Throws<InvalidDataException>((plan with { Version = 9, Schema = ChartContractVersions.ConnectedCompositionPlotPlanSchema }).Validate);
        var layer = plan.Layers[0];
        var datum = layer.Data[0];
        Assert.Throws<InvalidDataException>((plan with { Layers = plan.Layers.SetItem(0, layer with { Data = layer.Data.SetItem(0, datum with { ConnectionCoordinates = null }) }) }).Validate);
        Assert.Throws<InvalidDataException>((plan with { Layers = plan.Layers.SetItem(0, layer with { Data = layer.Data.SetItem(0, datum with { ConnectionCoordinates = datum.ConnectionCoordinates! with { X = 9m } }) }) }).Validate);
        var gap = layer.Data.Single(datum => datum.RowIndex == 2);
        Assert.Throws<InvalidDataException>((plan with { Layers = plan.Layers.SetItem(0, layer with { Data = layer.Data.Replace(gap, gap with { IsGap = false, ConnectionCoordinates = new(0m, 3m) }) }) }).Validate);
        Assert.Contains(PlotPlanConformanceHarness.Evaluate(plan, [new LostGeometryBackend()]).Issues, issue => issue.SemanticArea == "layers");
    }

    private sealed class LostGeometryBackend : IPlotPlanSemanticBackend
    {
        public string Name => "lost-zero-geometry";
        public PlotSemanticProjection Project(PlotPlan plan)
        {
            var projection = PlotSemanticProjection.FromPlan(plan);
            return projection with { Layers = projection.Layers.Select(layer => layer with { ConnectionCoordinates = default }).ToImmutableArray() };
        }
    }

    [Fact]
    public async Task AuthoringLineageAndExportsRetainRawNullIntent()
    {
        var sql = Sql("LINE", true, "GRID");
        var statement = Parse(sql);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = sql + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Route)));";
        var parsed = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(parsed.Error);
        Assert.Equal(statement.ToSql(), Parse(new DesignerScriptPatcher().Patch(source, parsed.DesignState)).ToSql());
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(sql).Tokenize(), sql).Parse());
        foreach (var field in new[] { "Distance", "Estimate", "Flag", "Series", "Cohort", "Phase" })
            Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Route" && entry.SourceColumns.Contains(field));
        var (spec, data) = Lower(sql);
        Assert.Equal(ChartContractSerializer.Serialize(spec), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(ChartContractSerializer.Serialize(spec))));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var svg = new SvgChartRenderer().Render(plan);
        var report = new ReportManifest { Title = "Zero", Source = "zero.rptsql", Visuals = [new VisualManifest { Name = "Route", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = svg }] };
        Assert.Contains("null Y rendered at zero", new MarkdownRenderer().Render(report));
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await new PdfExporter().ExportAsync(report))[..4]);
    }

    [Theory]
    [InlineData("MIN = -10, MAX = 10", "MIN = 1, MAX = 10")]
    [InlineData("MIN = -10, MAX = 10", "MIN = -10, MAX = -1")]
    public void ScalarZeroRequiresExplicitBoundsContainingZero(string before, string after)
    {
        var statement = Parse(Sql("LINE").Replace(before, after, StringComparison.Ordinal));
        Assert.Contains(AdvancedChartSemanticValidator.Validate(statement), diagnostic => diagnostic.Line > 0 && diagnostic.Column > 0 && diagnostic.Message.Contains("domain containing zero", StringComparison.Ordinal));
        var (spec, _) = Lower(Sql("LINE"));
        Assert.Throws<InvalidDataException>((spec with { Scales = spec.Scales.Select(scale => scale.Id == "vertical" ? scale with { DomainMinimum = ChartValue.From(1m) } : scale).ToImmutableArray() }).Validate);
    }

    [Theory]
    [InlineData("LINE", "1D61B8239E8A9F1111E872AEB6DB2D93BDD4BFED56A8248F474121B602C3E5C1", "1AC90D2DF8A2081F8D87F90F81C5695DAE375361AC337EE9C543B6670F3E17C1")]
    [InlineData("AREA", "08CAD8C9F951E4051D575828299961FB9B6A665F5C193030C3B122ACDC670CC4", "C3BE772D78D48F5C6A0B20667E71A1E17D7CBF9E319FD26513B7C10683FA2B22")]
    [InlineData("RIBBON", "C654193BEB6AF4EC71B99120853982E02A2E4FDAF7D8F978EB6699E181509288", "40A9AC770598A5E589DCC03409D2F1DEE622EB518C274020A5BF6EC4A3E488B4")]
    public void GoldenZeroGeometryAndSvg(string form, string expectedPlan, string expectedSvg)
    {
        var (spec, data) = Lower(Sql(form, true, "WRAP"));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        Assert.True(planHash == expectedPlan && svgHash == expectedSvg, $"{form}: Plan: {planHash}; SVG: {svgHash}");
    }

    [Theory]
    [InlineData("LINE", ChartValueKind.Integer)]
    [InlineData("LINE", ChartValueKind.FloatingPoint)]
    [InlineData("AREA", ChartValueKind.Integer)]
    [InlineData("AREA", ChartValueKind.FloatingPoint)]
    [InlineData("RIBBON", ChartValueKind.Integer)]
    [InlineData("RIBBON", ChartValueKind.FloatingPoint)]
    public void NumericSourceKindsPreserveNullsAndProduceEquivalentGeometry(string form, ChartValueKind kind)
    {
        var (spec, original) = Lower(Sql(form));
        var data = original with
        {
            Columns = original.Columns.Select(column => column.ValueKind != ChartValueKind.Decimal ? column : column with
            {
                ValueKind = kind,
                Values = column.Values.Select(value => value.Kind == ChartValueKind.Null ? value : kind == ChartValueKind.Integer ? ChartValue.From((long)value.Decimal!.Value) : ChartValue.From((double)value.Decimal!.Value)).ToImmutableArray()
            }).ToImmutableArray()
        };
        var resolver = new PlotPlanResolver();
        var plan = resolver.Resolve(spec, data);
        var expected = resolver.Resolve(spec, original);
        Assert.Equal(Paths(expected).Select(path => path.Attribute("d")!.Value), Paths(plan).Select(path => path.Attribute("d")!.Value));
        Assert.All(plan.Layers[0].Data.SelectMany(datum => datum.Channels).Where(channel => channel.Value.Kind != ChartValueKind.Null), channel => Assert.Equal(kind, channel.Value.Kind));
        Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
    }

    [Theory]
    [InlineData("LINE")]
    [InlineData("AREA")]
    [InlineData("RIBBON")]
    public void ZeroNeverCreatesMissingXOrRibbonBounds(string form)
    {
        var (spec, original) = Lower(Sql(form));
        var data = original with { Columns = original.Columns.Select(column => column.Name == "Distance" ? column with { Values = column.Values.Select(_ => ChartValue.Null()).ToImmutableArray() } : column).ToImmutableArray() };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(Enumerable.Range(0, data.RowCount), plan.Nulls.GapRows);
        Assert.All(plan.Layers[0].Data, datum => Assert.Null(datum.ConnectionCoordinates));
        Assert.Empty(Paths(plan));
        Assert.DoesNotContain(XDocument.Parse(new SvgChartRenderer().Render(plan)).Descendants(), element => (string?)element.Attribute("class") == "plot-line-symbol");
        Assert.All(plan.Fallback.Items.Where(item => item.Label.StartsWith("Row ", StringComparison.Ordinal)), item => Assert.Contains("null", item.Value + item.Detail));
    }

    [Fact]
    public void RawNullDisplayAndLabelsRemainDistinctFromObservedZero()
    {
        var (spec, data) = Lower(Sql("LINE"));
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name != "Estimate" ? column : column with
            { DisplayValues = column.Values.Select(value => value.Kind == ChartValueKind.Null ? "N/A" : PlotPlanResolver.Display(value)).ToImmutableArray() }).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        plan = plan with { Style = plan.Style.Add(new StyleToken("DATA_LABELS", "ON")) };
        var row = plan.Layers[0].Data.Single(datum => datum.RowIndex == 1);
        Assert.Equal(ChartValueKind.Null, row.Channels.Single(channel => channel.Channel == FieldChannel.Y).Value.Kind);
        Assert.Equal("N/A", row.Channels.Single(channel => channel.Channel == FieldChannel.Y).DisplayValue);
        Assert.Contains(plan.Fallback.Items, item => item.Order == 1 && item.Value == "N/A" && item.Detail?.StartsWith("null Y rendered at zero", StringComparison.Ordinal) == true);
        var decorations = XDocument.Parse(new SvgChartRenderer().Render(plan)).Descendants().Where(element => (string?)element.Attribute("class") is "plot-line-symbol" or "plot-smart-label" or "plot-smart-label-occluded").ToArray();
        Assert.Contains(decorations, element => (string?)element.Attribute("data-row-index") == "1" && element.Value == "null Y rendered at zero");
        Assert.Contains(decorations, element => (string?)element.Attribute("data-row-index") == "6" && element.Value == "0");
        Assert.DoesNotContain(decorations, element => (string?)element.Attribute("data-row-index") == "2");
    }

    [Fact]
    public void AbsentYChannelRemainsAGap()
    {
        var (spec, data) = Lower(Sql("LINE"));
        data = data with { Columns = data.Columns.Where(column => column.Name != "Estimate").ToImmutableArray() };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Empty(plan.Layers[0].Connections);
        Assert.All(plan.Layers[0].Data, datum => { Assert.True(datum.IsGap); Assert.Null(datum.ConnectionCoordinates); });
        Assert.Empty(Paths(plan));
    }

    [Fact]
    public void FocusedZeroExampleParsesLowersAndResolves()
    {
        var path = Path.Combine(TerminalSnapshotHarness.GetRepoRoot(), "docs", "reference", "visuals-reporting", "visuals", "chart.md");
        var example = Regex.Matches(File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal), @"```sql\s*(.*?)```", RegexOptions.Singleline)
            .Select(match => match.Groups[1].Value.Trim()).Single(block => block.StartsWith("CREATE VISUAL ZeroRoute ", StringComparison.Ordinal));
        var (spec, data) = Lower(example);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(10, plan.Version);
        Assert.Contains(plan.Layers[0].Connections, connection => connection.SourceRowIndex == 5 && connection.Encodings.Single(value => value.Channel == ConditionalEncodingChannel.Color).Value.Text == "#008000");
    }

    [Theory]
    [InlineData("TYPE = CARTESIAN", "TYPE = POLAR")]
    [InlineData("'LINEAR'", "'STEP'")]
    [InlineData("COLOR WHEN", "SHAPE WHEN")]
    [InlineData("SCALE = vertical)", "SCALE = vertical, STACK = ZERO)")]
    [InlineData("SCALE = vertical)", "SCALE = vertical, AXIS = SECONDARY)")]
    [InlineData("vertical = LINEAR", "vertical = LOGARITHMIC")]
    public void UnsupportedZeroCombinationsHavePositionedDiagnostics(string before, string after)
    {
        var statement = Parse(Sql("LINE").Replace(before, after, StringComparison.Ordinal));
        Assert.Contains(AdvancedChartSemanticValidator.Validate(statement), diagnostic => diagnostic.Line > 0 && diagnostic.Column > 0 && diagnostic.Message.Contains("Connected CONDITIONS", StringComparison.Ordinal));
    }

    [Fact]
    public void ExplicitGeometryCannotMoveToOrdinaryLayersOrChangeRawNullMeaning()
    {
        var (spec, data) = Lower(Sql("LINE"));
        var zero = spec.Layers[0];
        spec = spec with { Layers = [zero, zero with { Id = "observed", Mark = MarkKind.Point, Style = [], Conditions = [] }] };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var ordinaryIndex = plan.Layers.IndexOf(Assert.Single(plan.Layers, layer => layer.Mark == MarkKind.Point));
        var ordinary = plan.Layers[ordinaryIndex];
        var invalid = plan with { Layers = plan.Layers.SetItem(ordinaryIndex, ordinary with { Data = ordinary.Data.SetItem(0, ordinary.Data[0] with { ConnectionCoordinates = new(5m, 2m) }) }) };
        Assert.Contains("only to a ZERO", Assert.Throws<InvalidDataException>(invalid.Validate).Message);
        var layerIndex = plan.Layers.IndexOf(Assert.Single(plan.Layers, layer => layer.Mark == MarkKind.Line));
        var layer = plan.Layers[layerIndex];
        var datum = layer.Data[1];
        invalid = plan with { Layers = plan.Layers.SetItem(layerIndex, layer with { Data = layer.Data.SetItem(1, datum with { ConnectionCoordinates = datum.ConnectionCoordinates! with { Y = 1m } }) }) };
        Assert.Contains("fill only null scalar Y", Assert.Throws<InvalidDataException>(invalid.Validate).Message);
        var gapIndex = layer.Data.IndexOf(Assert.Single(layer.Data, value => value.IsGap));
        invalid = plan with { Layers = plan.Layers.SetItem(layerIndex, layer with { Data = layer.Data.SetItem(gapIndex, layer.Data[gapIndex] with { Encodings = [new(ConditionalEncodingChannel.Size, ChartValue.From(5m))] }) }) };
        Assert.Contains("including gap rows", Assert.Throws<InvalidDataException>(invalid.Validate).Message);
    }

    private static string N(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static XElement[] Paths(PlotPlan plan) => XDocument.Parse(new SvgChartRenderer().Render(plan)).Descendants().Where(element => (string?)element.Attribute("class") is "plot-conditional-connection" or "plot-conditional-area").ToArray();
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
            Columns = ["Distance", "Estimate", "Lower", "Upper", "Flag", "Series", "Cohort", "Phase"],
            Rows =
        [["5", "2", "1", "3", "first", "S", "A", "P"], ["3", null, "2", "6", "null", "T", "A", "P"],
         [null, "3", null, "4", "ignored", "S", "B", "P"], ["1", "-4", "5", "3", "middle", "T", "B", "P"],
         ["4", "1", "0", "2", "last", "S", "A", "P"], ["6", null, "1", null, "null", "T", "A", "P"],
         ["7", "0", "0", "0", "last", "S", "B", "P"], ["8", "5", "3", "2", "last", "T", "B", "P"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
