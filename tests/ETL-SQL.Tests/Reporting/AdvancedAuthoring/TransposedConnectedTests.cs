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

public sealed class TransposedConnectedTests
{
    private static string Sql(string form, string policy, bool grouped = false, string facet = "NONE", bool aspect = false) => $$"""
        CREATE VISUAL Route AS CUSTOM (SOURCE = #prepared, CHART (
          COORDINATE (TYPE = TRANSPOSED_CARTESIAN{{(aspect ? ", ASPECT_RATIO = 2" : "")}}),
          SCALES (unusedX = BAND (CHANNEL = X), unusedY = LINEAR (CHANNEL = Y, MIN = 100, MAX = 200),
            horizontal = LINEAR (CHANNEL = X, MIN = 0, MAX = 10), vertical = LINEAR (CHANNEL = Y, MIN = -10, MAX = 10)),
          {{(facet == "NONE" ? "" : facet == "GRID" ? "FACET (ROW = Cohort, COLUMN = Phase)," : "FACET (WRAP = Cohort, COLUMNS = 2),")}}
          {{(facet == "NONE" ? "" : "RESOLVE (X = INDEPENDENT, Y = INDEPENDENT),")}}
          LAYERS (route = {{(form == "LINE" ? "LINE" : "AREA")}} (
            NULL_HANDLING = {{policy}}, {{(form == "AREA" ? "AREA_BASELINE = ZERO," : "")}}
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
            foreach (var policy in new[] { "GAP", "CONNECT", "ZERO" })
                foreach (var grouped in new[] { false, true })
                    foreach (var facet in new[] { "NONE", "WRAP", "GRID" })
                        foreach (var aspect in new[] { false, true })
                            foreach (var reverse in new[] { false, true }) yield return [form, policy, grouped, facet, aspect, reverse];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void GeometryPreservesSemanticChannelsOwnershipAndPartitions(string form, string policy, bool grouped, string facet, bool aspect, bool reverse)
    {
        var (spec, data) = Lower(Sql(form, policy, grouped, facet, aspect));
        spec = spec with { Scales = spec.Scales.Select(scale => scale with { Reverse = reverse }).ToImmutableArray() };
        var resolver = new PlotPlanResolver();
        var plan = resolver.Resolve(spec, data, new PlotBounds(0m, 0m, 900m, 600m));
        Assert.Equal(8, spec.Version);
        Assert.Equal(11, plan.Version);
        Assert.Equal(new ResolvedCartesianAxes("horizontal", "vertical"), plan.CartesianAxes);
        Assert.Equal(CoordinateKind.TransposedCartesian, PlotSemanticProjection.FromPlan(plan).ConnectedCoordinate);
        Assert.Equal(8, plan.Layers.Sum(layer => layer.Data.Length));
        Assert.All(plan.Layers.SelectMany(layer => layer.Data), datum =>
        {
            foreach (var channel in datum.Channels.Where(channel => channel.Channel is FieldChannel.X or FieldChannel.Y or FieldChannel.YStart or FieldChannel.YEnd))
            {
                var field = channel.Channel switch { FieldChannel.X => "Distance", FieldChannel.Y => "Estimate", FieldChannel.YStart => "Lower", _ => "Upper" };
                Assert.Equal(data.Columns.Single(column => column.Name == field).Values[datum.RowIndex], channel.Value);
            }
        });
        var paths = Paths(plan);
        Assert.Equal(plan.Layers.Sum(layer => layer.Connections.Length), paths.Length);
        foreach (var layer in plan.Layers)
        {
            var expected = new List<(int Source, int Destination, string? Facet)>();
            var partitions = plan.Facets.IsDefaultOrEmpty ? new[] { (Id: (string?)null, Rows: Enumerable.Range(0, data.RowCount).ToHashSet()) }
                : plan.Facets.Select(panel => (Id: (string?)panel.Id, Rows: panel.RowIndices.ToHashSet()));
            foreach (var partition in partitions)
            {
                int? previous = null;
                foreach (var datum in layer.Data.Where(datum => partition.Rows.Contains(datum.RowIndex)))
                {
                    var missing = datum.RowIndex == 2 || (form == "RIBBON" ? datum.RowIndex == 5 : policy != "ZERO" && datum.RowIndex is 1 or 5);
                    if (missing) { if (policy != "CONNECT") previous = null; continue; }
                    if (previous is { } source) expected.Add((source, datum.RowIndex, partition.Id));
                    previous = datum.RowIndex;
                }
            }
            Assert.Equal(expected, layer.Connections.Select(connection => (connection.SourceRowIndex, connection.DestinationRowIndex, connection.FacetId)));
            foreach (var connection in layer.Connections)
            {
                var source = layer.Data[connection.SourceIndex];
                var destination = layer.Data[connection.DestinationIndex];
                Assert.Equal(source.Encodings.ToArray(), connection.Encodings.ToArray());
                var description = ConnectedMarkResolver.Describe(connection, plan, layer);
                var path = Assert.Single(paths, path => path.Value == description);
                Assert.Contains("X vertical, Y horizontal", description);
                Assert.Contains(plan.Fallback.Items, item => item.Value == description);
                var panel = plan.Facets.FirstOrDefault(panel => panel.Id == connection.FacetId);
                var frame = panel?.CartesianViewport ?? panel?.Bounds ?? plan.CartesianViewport ?? plan.Bounds;
                var scales = panel?.Scales ?? plan.Scales;
                var xScale = scales.Single(scale => scale.Id == "horizontal");
                var yScale = scales.Single(scale => scale.Id == "vertical");
                decimal Map(decimal value, ResolvedScale scale, bool vertical)
                {
                    var minimum = PlotPlanResolver.Number(scale.Domain[0])!.Value;
                    var maximum = PlotPlanResolver.Number(scale.Domain[^1])!.Value;
                    var ratio = (value - minimum) / (maximum - minimum);
                    if (scale.Reverse) ratio = 1m - ratio;
                    return vertical ? 40m + (1m - ratio) * (frame.Height - 100m) : 60m + ratio * (frame.Width - 80m);
                }
                decimal Value(ResolvedDatum datum, FieldChannel channel) => PlotPlanResolver.Number(datum.Channels.Single(value => value.Channel == channel).Value) ?? 0m;
                string Point(ResolvedDatum datum, bool lower = false) => $"{N(Map(form == "RIBBON" ? Value(datum, lower ? FieldChannel.YStart : FieldChannel.YEnd) : lower ? 0m : Value(datum, FieldChannel.Y), yScale, false))} {N(Map(Value(datum, FieldChannel.X), xScale, true))}";
                var geometry = $"M {Point(source)} L {Point(destination)}";
                if (form != "LINE") geometry += $" L {Point(destination, true)} L {Point(source, true)} Z";
                Assert.Equal(geometry, path.Attribute("d")!.Value);
                Assert.Equal(source.Encodings.FirstOrDefault(value => value.Channel == ConditionalEncodingChannel.Color)?.Value.Text ?? "#112233", path.Attribute(form == "LINE" ? "stroke" : "fill")!.Value);
                Assert.Equal(source.Encodings.Single(value => value.Channel == ConditionalEncodingChannel.Opacity).Value.Decimal!.Value.ToString(CultureInfo.InvariantCulture), path.Attribute("opacity")!.Value);
                if (aspect)
                {
                    var xSpan = PlotPlanResolver.Number(xScale.Domain[^1])!.Value - PlotPlanResolver.Number(xScale.Domain[0])!.Value;
                    var ySpan = PlotPlanResolver.Number(yScale.Domain[^1])!.Value - PlotPlanResolver.Number(yScale.Domain[0])!.Value;
                    Assert.Equal(2m, Math.Round(((frame.Width - 80m) / ySpan) / ((frame.Height - 100m) / xSpan), 8));
                }
            }
        }
        var symbols = XDocument.Parse(new SvgChartRenderer().Render(plan)).Descendants().Where(element => (string?)element.Attribute("class") == "plot-line-symbol").ToArray();
        Assert.Equal(form == "LINE" ? policy == "ZERO" ? 7 : 5 : 0, symbols.Length);
        Assert.DoesNotContain(symbols, element => (string?)element.Attribute("data-row-index") == "2");
        if (form == "LINE" && policy == "ZERO") Assert.Contains(symbols, element => (string?)element.Attribute("data-row-index") == "1" && element.Value == "null Y rendered at zero");
        var resized = resolver.Relayout(spec, data, plan, new PlotBounds(0m, 0m, 700m, 800m));
        Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, resized.Bounds)), ChartContractSerializer.Serialize(resized));
        Assert.Equal(new SvgChartRenderer().Render(plan), new SvgChartRenderer().Render(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
        var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 220).NormalizedText;
        if (paths.Length > 0) Assert.Contains("X vertical, Y horizontal", terminal);
        if (form != "RIBBON" && policy == "ZERO") Assert.Contains("null Y rendered at zero", terminal);
    }

    [Theory]
    [InlineData("LINE", "GAP")]
    [InlineData("LINE", "CONNECT")]
    [InlineData("LINE", "ZERO")]
    [InlineData("AREA", "GAP")]
    [InlineData("AREA", "CONNECT")]
    [InlineData("AREA", "ZERO")]
    [InlineData("RIBBON", "GAP")]
    [InlineData("RIBBON", "CONNECT")]
    [InlineData("RIBBON", "ZERO")]
    public void AllMissingXAndEmptySourcesHaveNoInventedConnections(string form, string policy)
    {
        var (spec, data) = Lower(Sql(form, policy, true, "GRID", true));
        data = data with { Columns = data.Columns.Select(column => column.Name == "Distance" ? column with { Values = column.Values.Select(_ => ChartValue.Null()).ToImmutableArray() } : column).ToImmutableArray() };
        var resolver = new PlotPlanResolver();
        var plan = resolver.Resolve(spec, data);
        Assert.Empty(plan.Layers.SelectMany(layer => layer.Connections));
        Assert.Empty(Paths(plan));
        Assert.Equal(8, plan.Layers.Sum(layer => layer.Data.Length));
        data = data with { RowCount = 0, Columns = data.Columns.Select(column => column with { Values = [], DisplayValues = [] }).ToImmutableArray() };
        plan = resolver.Resolve(spec, data);
        Assert.Equal(11, plan.Version);
        Assert.Empty(plan.Layers.SelectMany(layer => layer.Data));
        Assert.Empty(Paths(plan));
    }

    [Theory]
    [InlineData("LINE")]
    [InlineData("AREA")]
    [InlineData("RIBBON")]
    public async Task AuthoringContractsLineageAndStaticExportsPreserveOrientation(string form)
    {
        var sql = Sql(form, "ZERO", true, "GRID", true);
        var statement = Parse(sql);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = sql + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Route)));";
        var design = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(design.Error);
        Assert.Equal(statement.ToSql(), Parse(new DesignerScriptPatcher().Patch(source, design.DesignState)).ToSql());
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(sql).Tokenize(), sql).Parse());
        foreach (var field in new[] { "Distance", "Estimate", "Flag", "Series", "Cohort", "Phase" })
            Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Route" && entry.SourceColumns.Contains(field));
        var (spec, data) = Lower(sql);
        Assert.Equal(ChartContractSerializer.Serialize(spec), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(ChartContractSerializer.Serialize(spec))));
        Assert.Throws<InvalidDataException>((spec with { Schema = ChartContractVersions.ZeroConnectedChartSpecSchema, Version = 7 }).Validate);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Throws<InvalidDataException>((plan with { Schema = ChartContractVersions.ZeroConnectedPlotPlanSchema, Version = 10 }).Validate);
        Assert.Throws<InvalidDataException>((plan with { CartesianAxes = null }).Validate);
        Assert.Throws<InvalidDataException>((plan with { CartesianAxes = new("unusedX", "vertical") }).Validate);
        Assert.Contains(PlotPlanConformanceHarness.Evaluate(plan, [new LostOrientationBackend()]).Issues, issue => issue.SemanticArea == "coordinates");
        var report = new ReportManifest { Title = "Transposed", Source = "transposed.rptsql", Visuals = [new VisualManifest { Name = "Route", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = new SvgChartRenderer().Render(plan) }] };
        Assert.Contains("X vertical, Y horizontal", new MarkdownRenderer().Render(report));
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await new PdfExporter().ExportAsync(report))[..4]);
    }

    public static IEnumerable<object[]> NullEndpointCases()
    {
        foreach (var form in new[] { "LINE", "AREA", "RIBBON" })
            foreach (var policy in new[] { "GAP", "CONNECT", "ZERO" })
                foreach (var singleton in new[] { false, true }) yield return [form, policy, singleton];
    }

    [Theory]
    [MemberData(nameof(NullEndpointCases))]
    public void AllNullAndSingletonYValuesRetainRawMeaning(string form, string policy, bool singleton)
    {
        var (spec, data) = Lower(Sql(form, policy, aspect: true));
        data = data with
        {
            RowCount = singleton ? 1 : data.RowCount,
            Columns = data.Columns.Select(column => column with
            {
                Values = column.Values.Take(singleton ? 1 : data.RowCount).Select(value => column.Name is "Estimate" or "Lower" or "Upper" ? ChartValue.Null() : value).ToImmutableArray(),
                DisplayValues = []
            }).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var layer = Assert.Single(plan.Layers);
        Assert.All(layer.Data, datum => Assert.All(datum.Channels.Where(value => value.Channel is FieldChannel.Y or FieldChannel.YStart or FieldChannel.YEnd), value => Assert.Equal(ChartValueKind.Null, value.Value.Kind)));
        var zero = form != "RIBBON" && policy == "ZERO";
        Assert.Equal(zero && !singleton ? 5 : 0, layer.Connections.Length);
        Assert.Equal(layer.Connections.Length, Paths(plan).Length);
        var symbols = XDocument.Parse(new SvgChartRenderer().Render(plan)).Descendants().Where(element => (string?)element.Attribute("class") == "plot-line-symbol").ToArray();
        Assert.Equal(zero && form == "LINE" ? singleton ? 1 : 7 : 0, symbols.Length);
        Assert.All(symbols, symbol => Assert.Equal("null Y rendered at zero", symbol.Value));
        Assert.Equal(zero ? singleton ? 1 : 7 : 0, layer.Data.Count(datum => datum.ConnectionCoordinates is not null));
        Assert.Contains(plan.Fallback.Items, item => item.Value.Contains("null", StringComparison.Ordinal));
        var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 220).NormalizedText;
        Assert.Contains(form == "RIBBON" ? "Y null to null" : "Y null", terminal);
        if (zero) Assert.Contains("null Y rendered at zero", terminal);
    }

    [Fact]
    public void LineDashAndSymbolsUseAuthoredPresentation()
    {
        var (spec, data) = Lower(Sql("LINE", "CONNECT", aspect: true).Replace("COLOR = '#112233'", "COLOR = '#112233', LINE_DASH = 'DASHED'", StringComparison.Ordinal));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.All(Paths(plan), path => Assert.Equal("7 5", path.Attribute("stroke-dasharray")!.Value));
    }

    [Fact]
    public void FocusedTransposedConnectedExampleParsesLowersAndResolves()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ETL-SQL.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var markdown = File.ReadAllText(Path.Combine(directory.FullName, "docs", "reference", "visuals-reporting", "visuals", "chart.md")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var example = Regex.Matches(markdown, @"```sql\s*(.*?)```", RegexOptions.Singleline).Select(match => match.Groups[1].Value.Trim())
            .Single(block => block.StartsWith("CREATE VISUAL TransposedRoute ", StringComparison.Ordinal));
        var (spec, data) = Lower(example);
        Assert.Equal(Parse(example).ToSql(), Parse(Parse(example).ToSql()).ToSql());
        Assert.Equal(8, spec.Version);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(11, plan.Version);
        Assert.NotEmpty(Paths(plan));
    }

    private sealed class LostOrientationBackend : IPlotPlanSemanticBackend
    {
        public string Name => "lost-transposition";
        public PlotSemanticProjection Project(PlotPlan plan) => PlotSemanticProjection.FromPlan(plan) with { ConnectedCoordinate = CoordinateKind.Cartesian };
    }

    [Theory]
    [InlineData("'LINEAR'", "'MONOTONE'")]
    [InlineData("COLOR WHEN", "SIZE WHEN")]
    [InlineData("SCALE = vertical)", "SCALE = vertical, STACK = ZERO)")]
    [InlineData("SCALE = vertical)", "SCALE = vertical, AXIS = SECONDARY)")]
    [InlineData("vertical = LINEAR", "vertical = LOGARITHMIC")]
    [InlineData("NULL_HANDLING = ZERO", "POSITION = JITTER(X = 0.1, Y = 0.1, KEY = Distance, SEED = 2), NULL_HANDLING = ZERO")]
    public void UnsupportedCombinationsHavePositionedDiagnostics(string before, string after)
    {
        var statement = Parse(Sql("LINE", "ZERO", true, "GRID", true).Replace(before, after, StringComparison.Ordinal));
        Assert.Contains(AdvancedChartSemanticValidator.Validate(statement), diagnostic => diagnostic.Line > 0 && diagnostic.Column > 0 && diagnostic.Message.Contains("Connected CONDITIONS", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("LINE", "E32BCE86DE93EE4FF3C38D2F44AFD31F423BF082FC95C5944A9CA202FFFEDD27", "EF0E4A0F8BFC3A793C07E0E8680F48B5BDF491E173A7CF8C809EED98ED0B1584")]
    [InlineData("AREA", "F10BBB7DEEB07A3F9D2EBFFB1247B0CD067D91DD5F83E01287627256CEBF3581", "27F69D31A5B2EE32FB721642E8AE14E96E246AABA1DD066B31EDA6E71F662E80")]
    [InlineData("RIBBON", "28F80C23DE455288183596948B75592CCCD2880762932B63824524CEE3AB1D6C", "2F165F24BE092959DB329D7D5CF87577E374F46D94236EEC01BDA054CD52F4E5")]
    public void DeterministicTransposedPlanAndSvg(string form, string expectedPlan, string expectedSvg)
    {
        var (spec, data) = Lower(Sql(form, "ZERO", true, "GRID", true));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        Assert.True(planHash == expectedPlan && svgHash == expectedSvg, $"{form}: Plan: {planHash}; SVG: {svgHash}");
    }

    public static IEnumerable<object[]> MixedCases()
    {
        foreach (var form in new[] { "LINE", "AREA", "RIBBON" })
            foreach (var aspect in new[] { false, true })
                foreach (var unit in new[] { PositionAdjustmentUnit.Em, PositionAdjustmentUnit.Band, PositionAdjustmentUnit.Data }) yield return [form, aspect, unit];
    }

    [Theory]
    [MemberData(nameof(MixedCases))]
    public void MixedPoliciesAndOrdinaryPointPlacementUseTheSamePhysicalAxes(string form, bool aspect, PositionAdjustmentUnit unit)
    {
        var (spec, data) = Lower(Sql(form, "ZERO", true, "GRID", aspect));
        var zero = spec.Layers[0];
        MarkLayerSpec Policy(string id, string policy, int z) => zero with { Id = id, ZIndex = z, Style = zero.Style.Select(token => token.Name == "nullHandling" ? token with { Value = policy } : token).ToImmutableArray() };
        var observed = new MarkLayerSpec("observed", MarkKind.Point, 10,
            [FieldBinding.Datum(FieldChannel.X, ChartValue.From(2m), DataSemanticKind.Quantitative, "horizontal"),
             FieldBinding.Datum(FieldChannel.Y, ChartValue.From(4m), DataSemanticKind.Quantitative, "vertical"),
             new(FieldChannel.ErrorLow, "Lower", DataSemanticKind.Quantitative, "vertical"),
             new(FieldChannel.ErrorHigh, "Upper", DataSemanticKind.Quantitative, "vertical")],
            [new("nullHandling", "GAP"), new("color", "#778899")])
        { Position = new(PositionAdjustmentKind.Nudge, 0.1m, 0.2m, Unit: unit) };
        spec = spec with { Layers = [zero, Policy("gapped", "GAP", 3), Policy("joined", "CONNECT", 5), observed], Theme = spec.Theme with { Tokens = spec.Theme.Tokens.Add(new("LEGEND_POSITION", "RIGHT")) } };
        var resolver = new PlotPlanResolver();
        var plan = resolver.Resolve(spec, data, new PlotBounds(0m, 0m, 1200m, 900m));
        Assert.Equal(32, plan.Layers.Sum(layer => layer.Data.Length));
        Assert.All(plan.Layers.Where(layer => ResolvedMarkConnection.FillsNullsWithZero(layer)), layer => Assert.Empty(layer.ConnectionSkippedRows));
        Assert.Contains(plan.Layers.Where(ResolvedMarkConnection.ConnectsAcrossNulls).SelectMany(layer => layer.ConnectionSkippedRows), row => row == (form == "RIBBON" ? 5 : 1));
        var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
        Assert.Equal(plan.Layers.Sum(layer => layer.Connections.IsDefault ? 0 : layer.Connections.Length), Paths(plan).Length);
        var points = svg.Descendants().Where(element => (string?)element.Attribute("class") == "plot-point").ToArray();
        Assert.Equal(8, points.Length);
        foreach (var point in points)
        {
            var row = int.Parse(point.Attribute("data-row-index")!.Value, CultureInfo.InvariantCulture);
            var panel = plan.Facets.Single(panel => panel.RowIndices.Contains(row));
            var frame = panel.CartesianViewport ?? panel.Bounds;
            var area = TransposedAspectLayout.Resolve(frame, plan.Style, plan.Layers, plan.Legend.Length);
            var xScale = panel.Scales.Single(scale => scale.Id == "horizontal");
            var yScale = panel.Scales.Single(scale => scale.Id == "vertical");
            decimal Span(ResolvedScale scale) => PlotPlanResolver.Number(scale.Domain[^1])!.Value - PlotPlanResolver.Number(scale.Domain[0])!.Value;
            var deltaX = unit == PositionAdjustmentUnit.Em ? 2.4m : unit == PositionAdjustmentUnit.Band ? 0.2m * (frame.Width - 80m) : 0.2m / Span(yScale) * area.Width;
            var deltaY = unit == PositionAdjustmentUnit.Em ? -1.2m : unit == PositionAdjustmentUnit.Band ? -0.1m * (frame.Height - 100m) : -0.1m / Span(xScale) * area.Height;
            var expectedX = area.X + (4m - PlotPlanResolver.Number(yScale.Domain[0])!.Value) / Span(yScale) * area.Width + deltaX;
            var expectedY = area.Y + (1m - (2m - PlotPlanResolver.Number(xScale.Domain[0])!.Value) / Span(xScale)) * area.Height + deltaY;
            Assert.Equal(N(expectedX), point.Attribute("cx")!.Value);
            Assert.Equal(N(expectedY), point.Attribute("cy")!.Value);
            var errorBars = svg.Descendants().Where(element => (string?)element.Attribute("class") == "plot-error-bar" && (string?)element.Attribute("data-row-index") == row.ToString(CultureInfo.InvariantCulture)).ToArray();
            if (row is 2 or 5)
            {
                Assert.Empty(errorBars);
                continue;
            }
            var stem = Assert.Single(errorBars).Elements().First(element => (string?)element.Attribute("class") == "plot-error-bar-stem");
            Assert.Equal(N(expectedY), stem.Attribute("y1")!.Value);
            Assert.Equal(N(expectedY), stem.Attribute("y2")!.Value);
        }
        var resized = resolver.Relayout(spec, data, plan, new PlotBounds(0m, 0m, 1000m, 800m));
        Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, resized.Bounds)), ChartContractSerializer.Serialize(resized));
    }

    [Fact]
    public void LabelsKeepRawYAndFinalSourcePresentation()
    {
        var (spec, data) = Lower(Sql("LINE", "ZERO"));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        plan = plan with { Style = plan.Style.Add(new("DATA_LABELS", "ON")) };
        var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
        Assert.Contains(svg.Descendants(), element => (string?)element.Attribute("class") is "plot-smart-label" or "plot-smart-label-occluded" && (string?)element.Attribute("data-row-index") == "1" && element.Value == "null Y rendered at zero");
        var final = Assert.Single(svg.Descendants(), element => (string?)element.Attribute("class") == "plot-line-symbol" && (string?)element.Attribute("data-row-index") == "7");
        Assert.Equal("5", final.Value);
        Assert.Equal("#112233", final.Attribute("fill")!.Value);
        Assert.DoesNotContain(plan.Layers[0].Connections, connection => connection.SourceRowIndex == 7);
    }

    [Theory]
    [InlineData("LINE", false)]
    [InlineData("AREA", false)]
    [InlineData("RIBBON", false)]
    [InlineData("LINE", true)]
    [InlineData("AREA", true)]
    [InlineData("RIBBON", true)]
    public void OrdinaryStraightLayersShareThePhysicalFrame(string form, bool aspect)
    {
        var (spec, data) = Lower(Sql(form, "GAP", facet: "WRAP", aspect: aspect));
        var ordinary = spec.Layers[0] with { Id = "ordinary", ZIndex = 2, Conditions = [], Style = spec.Layers[0].Style.Select(token => token.Name.Equals("color", StringComparison.OrdinalIgnoreCase) ? token with { Value = "#445566" } : token).ToImmutableArray() };
        spec = spec with { Layers = spec.Layers.Add(ordinary) };
        spec.Validate();
        var resolver = new PlotPlanResolver();
        var plan = resolver.Resolve(spec, data);
        var glyphs = XDocument.Parse(new SvgChartRenderer().Render(plan)).Descendants()
            .Where(element => (string?)element.Attribute("class") is "plot-area" or "plot-ribbon" || element.Name.LocalName == "path" && (string?)element.Attribute("stroke") == "#445566").ToArray();
        Assert.NotEmpty(glyphs);
        Assert.All(glyphs, glyph => Assert.False(string.IsNullOrWhiteSpace(glyph.Attribute("d")!.Value)));
        if (form == "AREA") Assert.True(plan.Scales.Single(scale => scale.Id == "vertical").IncludesZero);
        var resized = resolver.Relayout(spec, data, plan, new PlotBounds(0m, 0m, 1000m, 800m));
        Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, resized.Bounds)), ChartContractSerializer.Serialize(resized));
    }

    [Theory]
    [InlineData(false, "SMOOTH")]
    [InlineData(false, "STEP_BEFORE")]
    [InlineData(false, "STEP_AFTER")]
    [InlineData(false, "BASELINE")]
    [InlineData(true, "BASELINE")]
    [InlineData(false, "CONFIDENCE")]
    public void UnsupportedOrdinaryAreaFormsAreRejectedWithOrWithoutAspect(bool aspect, string unsupported)
    {
        var sql = Sql("LINE", "GAP", aspect: aspect).Replace("OPACITY WHEN Flag = 'first' THEN 0 ELSE 1)))", """
            OPACITY WHEN Flag = 'first' THEN 0 ELSE 1)),
            ordinary = AREA (NULL_HANDLING = GAP, AREA_BASELINE = ZERO,
              ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = horizontal), Y = Estimate (TYPE = QUANTITATIVE, SCALE = vertical)),
              STYLE (INTERPOLATION = 'LINEAR')))
            """, StringComparison.Ordinal);
        var (spec, _) = Lower(sql);
        var bad = unsupported switch
        {
            "SMOOTH" or "STEP_BEFORE" or "STEP_AFTER" => sql.Replace("STYLE (INTERPOLATION = 'LINEAR')))", $"STYLE (INTERPOLATION = '{unsupported}')))", StringComparison.Ordinal),
            "BASELINE" => sql.Replace("AREA_BASELINE = ZERO", "AREA_BASELINE = 5", StringComparison.Ordinal),
            _ => Regex.Replace(sql.Replace("AREA_BASELINE = ZERO,", "", StringComparison.Ordinal), @"(ordinary = AREA .*?ENCODINGS \(X = Distance .*?), Y = Estimate \(TYPE = QUANTITATIVE, SCALE = vertical\)", "$1, CONFIDENCE_LOW = Lower (TYPE = QUANTITATIVE, SCALE = vertical), CONFIDENCE_HIGH = Upper (TYPE = QUANTITATIVE, SCALE = vertical)", RegexOptions.Singleline)
        };
        Assert.Contains(AdvancedChartSemanticValidator.Validate(Parse(bad)), diagnostic => diagnostic.Line > 0 && diagnostic.Column > 0 && diagnostic.Message.Contains("connected composition AREA", StringComparison.Ordinal));
        var ordinary = spec.Layers[1];
        ordinary = unsupported switch
        {
            "SMOOTH" or "STEP_BEFORE" or "STEP_AFTER" => ordinary with { Style = ordinary.Style.Select(token => token.Name == "INTERPOLATION" ? token with { Value = unsupported } : token).ToImmutableArray() },
            "BASELINE" => ordinary with { Style = ordinary.Style.Select(token => token.Name == "areaBaseline" ? token with { Value = "5" } : token).ToImmutableArray() },
            _ => ordinary with { Bindings = [ordinary.Bindings[0], new(FieldChannel.ConfidenceLow, "Lower", DataSemanticKind.Quantitative, "vertical"), new(FieldChannel.ConfidenceHigh, "Upper", DataSemanticKind.Quantitative, "vertical")], Style = ordinary.Style.Where(token => token.Name != "areaBaseline").ToImmutableArray() }
        };
        Assert.Throws<InvalidDataException>((spec with { Layers = spec.Layers.SetItem(1, ordinary) }).Validate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryRulesAndTextKeepAllSemanticValuesWithoutRequiringAspect(bool aspect)
    {
        var (spec, data) = Lower(Sql("LINE", "GAP", facet: "WRAP", aspect: aspect));
        var rules = new MarkLayerSpec("thresholds", MarkKind.Rule, 2, [new(FieldChannel.Y, "Estimate", DataSemanticKind.Quantitative, "vertical")], [new("label", "Threshold")]);
        var text = new MarkLayerSpec("notes", MarkKind.Text, 3,
            [new(FieldChannel.X, "Distance", DataSemanticKind.Quantitative, "horizontal"), new(FieldChannel.Y, "Estimate", DataSemanticKind.Quantitative, "vertical"), new(FieldChannel.Text, "Flag", DataSemanticKind.Nominal)], []);
        spec = spec with { Layers = spec.Layers.Add(rules).Add(text) };
        spec.Validate();
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
        Assert.Equal(6, svg.Descendants().Count(element => (string?)element.Attribute("class") == "plot-reference-rule"));
        Assert.Equal(5, svg.Descendants().Count(element => (string?)element.Attribute("class") is "plot-smart-label" or "plot-smart-label-occluded"));
        var references = plan.Fallback.Items.Where(item => item.Group == "Reference").ToArray();
        Assert.Equal(new[] { "2", "3", "-4", "1", "0", "5" }, references.Select(item => item.Value));
        Assert.All(references, item => Assert.Contains("vertical reference rule", item.Detail));
        var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 220).NormalizedText;
        foreach (var value in new[] { "2", "3", "-4", "1", "0", "5" }) Assert.Contains("Threshold: Y = " + value, terminal);
        Assert.Contains("Annotation", terminal);
        Assert.Contains("ignored", terminal);
        Assert.Contains("middle", terminal);
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
