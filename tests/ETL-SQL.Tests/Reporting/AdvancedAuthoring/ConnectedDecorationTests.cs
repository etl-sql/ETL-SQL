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

public sealed class ConnectedDecorationTests
{
    private static string Sql(string form = "LINE", string policy = "GAP", string coordinate = "CARTESIAN", bool grouped = false, bool facets = false) => $$"""
        CREATE VISUAL Route AS CUSTOM (SOURCE = #prepared, CHART (
          COORDINATE (TYPE = {{(coordinate == "ASPECT" ? "TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2" : coordinate)}}),
          SCALES (unusedX = BAND (CHANNEL = X), unusedY = LINEAR (CHANNEL = Y, MIN = 100, MAX = 200),
            horizontal = LINEAR (CHANNEL = X, MIN = 0, MAX = 10), vertical = LINEAR (CHANNEL = Y, MIN = -10, MAX = 10)),
          {{(facets ? "FACET (WRAP = Cohort, COLUMNS = 2), RESOLVE (X = INDEPENDENT, Y = INDEPENDENT)," : "")}}
          LAYERS (route = {{(form == "LINE" ? "LINE" : "AREA")}} (
            NULL_HANDLING = {{policy}}, {{(form == "AREA" ? "AREA_BASELINE = ZERO," : "")}}
            ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = horizontal),
              {{(form == "RIBBON" ? "Y_START = Lower (TYPE = QUANTITATIVE, SCALE = vertical), Y_END = Upper (TYPE = QUANTITATIVE, SCALE = vertical)" : "Y = Estimate (TYPE = QUANTITATIVE, SCALE = vertical)")}}
              {{(grouped ? ", COLOR = Series (TYPE = NOMINAL)" : "")}}),
            STYLE (INTERPOLATION = 'LINEAR', COLOR = '#112233'),
            CONDITIONS (COLOR WHEN Flag = 'first' THEN '#ff0000' ELSE '#0000ff', OPACITY WHEN Flag = 'first' THEN 0 ELSE 1,
              SIZE WHEN Flag = 'first' THEN 7, SIZE WHEN Flag = 'middle' THEN 99 ELSE -2,
              SHAPE WHEN Flag = 'first' THEN 'square' ELSE 'CIRCLE',
              TEXT WHEN Flag = 'first' THEN '<hot & high>', TEXT WHEN Flag = 'null' THEN NULL,
              TEXT WHEN Flag = 'middle' THEN '', TEXT WHEN Flag = 'last' THEN 42)))
        ));
        """;

    public static IEnumerable<object[]> Cases()
    {
        foreach (var form in new[] { "LINE", "AREA", "RIBBON" })
            foreach (var policy in new[] { "GAP", "CONNECT", "ZERO" })
                foreach (var coordinate in new[] { "CARTESIAN", "TRANSPOSED_CARTESIAN", "ASPECT" })
                    foreach (var grouped in new[] { false, true })
                        foreach (var facets in new[] { false, true })
                            foreach (var reverse in new[] { false, true }) yield return [form, policy, coordinate, grouped, facets, reverse];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void SymbolsAnnotationsAndPathsPreserveRawRowsAndPhysicalAxes(string form, string policy, string coordinate, bool grouped, bool facets, bool reverse)
    {
        var (spec, data) = Lower(Sql(form, policy, coordinate, grouped, facets));
        spec = spec with { Scales = spec.Scales.Select(scale => scale with { Reverse = reverse }).ToImmutableArray() };
        var resolver = new PlotPlanResolver();
        var plan = resolver.Resolve(spec, data, new PlotBounds(0m, 0m, 900m, 600m));
        Assert.Equal(9, spec.Version);
        Assert.Equal(12, plan.Version);
        Assert.Equal(new ResolvedCartesianAxes("horizontal", "vertical"), plan.CartesianAxes);
        Assert.Equal(8, plan.Layers.Sum(layer => layer.Data.Length));
        var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
        var symbols = Elements(svg, "plot-connection-symbol");
        Assert.Equal(form == "RIBBON" ? 6 : policy == "ZERO" ? 7 : 5, symbols.Length);
        var paths = svg.Descendants().Where(element => (string?)element.Attribute("class") is "plot-conditional-connection" or "plot-conditional-area").ToArray();
        Assert.Equal(plan.Layers.Sum(layer => layer.Connections.Length), paths.Length);
        Assert.All(plan.Layers, layer => Assert.True(layer.ConnectionDecorations));
        foreach (var layer in plan.Layers)
        {
            foreach (var datum in layer.Data)
            {
                var missing = datum.RowIndex == 2 || (form == "RIBBON" ? datum.RowIndex == 5 : policy != "ZERO" && datum.RowIndex is 1 or 5);
                if (missing)
                {
                    Assert.Null(datum.ConnectionDecoration);
                    Assert.DoesNotContain(symbols, symbol => Row(symbol) == datum.RowIndex);
                    continue;
                }
                var symbol = Assert.Single(symbols, symbol => Row(symbol) == datum.RowIndex);
                var decoration = Assert.IsType<ResolvedConnectionDecoration>(datum.ConnectionDecoration);
                var expectedX = Raw(data, "Distance", datum.RowIndex);
                var expectedY = Raw(data, form == "RIBBON" ? "Upper" : "Estimate", datum.RowIndex);
                Assert.Equal(expectedX, decoration.X);
                Assert.Equal(expectedY, decoration.Y);
                Assert.Equal(datum.RowIndex == 0 ? 7m : datum.RowIndex == 3 ? 30m : 2m, decoration.Radius);
                Assert.Equal(datum.RowIndex == 0 ? "SQUARE" : "CIRCLE", decoration.Shape);
                var panel = plan.Facets.FirstOrDefault(panel => panel.RowIndices.Contains(datum.RowIndex));
                var frame = panel?.CartesianViewport ?? panel?.Bounds ?? plan.CartesianViewport ?? plan.Bounds;
                var scales = panel?.Scales ?? plan.Scales;
                decimal Map(decimal value, string id, bool vertical)
                {
                    var scale = scales.Single(scale => scale.Id == id);
                    var minimum = PlotPlanResolver.Number(scale.Domain[0])!.Value;
                    var maximum = PlotPlanResolver.Number(scale.Domain[^1])!.Value;
                    var ratio = (value - minimum) / (maximum - minimum);
                    if (reverse) ratio = 1m - ratio;
                    return vertical ? 40m + (1m - ratio) * (frame.Height - 100m) : 60m + ratio * (frame.Width - 80m);
                }
                var transposed = coordinate != "CARTESIAN";
                var px = Map(transposed ? expectedY : expectedX, transposed ? "vertical" : "horizontal", false);
                var py = Map(transposed ? expectedX : expectedY, transposed ? "horizontal" : "vertical", true);
                var x = decimal.Parse(symbol.Attribute(datum.RowIndex == 0 ? "x" : "cx")!.Value, CultureInfo.InvariantCulture);
                var y = decimal.Parse(symbol.Attribute(datum.RowIndex == 0 ? "y" : "cy")!.Value, CultureInfo.InvariantCulture);
                Assert.Equal(N(px), N(x + (datum.RowIndex == 0 ? 7m : 0m)));
                Assert.Equal(N(py), N(y + (datum.RowIndex == 0 ? 7m : 0m)));
                Assert.Equal(datum.RowIndex == 0 ? "14" : N(decoration.Radius), symbol.Attribute(datum.RowIndex == 0 ? "width" : "r")!.Value);
                Assert.Equal(datum.RowIndex == 0 ? "0" : "1", (string?)symbol.Attribute("opacity") ?? "1");
                Assert.Contains("shape " + decoration.Shape, symbol.Value);
                if (datum.RowIndex is 1 or 5 && form != "RIBBON") Assert.Contains("null Y rendered at zero", symbol.Value);
            }
            foreach (var connection in layer.Connections)
            {
                var source = layer.Data[connection.SourceIndex];
                Assert.Equal(source.Encodings.Where(value => value.Channel is ConditionalEncodingChannel.Color or ConditionalEncodingChannel.Opacity).ToArray(), connection.Encodings.ToArray());
                var path = Assert.Single(paths, path => path.Value == ConnectedMarkResolver.Describe(connection, plan, layer, includeOwner: false));
                Assert.Contains("Layer " + layer.Id, path.Parent!.Attribute("aria-label")!.Value);
                Assert.Equal(source.RowIndex == 0 ? "#ff0000" : "#0000ff", path.Attribute(form == "LINE" ? "stroke" : "fill")!.Value);
                Assert.Equal(source.RowIndex == 0 ? "0" : "1", (string?)path.Attribute("opacity") ?? "1");
                Assert.DoesNotContain("Size:", path.Value);
                Assert.DoesNotContain("Text:", path.Value);
            }
        }
        var labels = svg.Descendants().Where(element => (string?)element.Attribute("class") is "plot-smart-label" or "plot-smart-label-occluded").ToArray();
        Assert.Equal(new[] { 0, 4, 6, 7 }, labels.Select(Row).Order().ToArray());
        Assert.Contains(labels, label => label.Value == "<hot & high>");
        Assert.DoesNotContain(labels, label => Row(label) is 1 or 3 or 5);
        var serialized = ChartContractSerializer.Serialize(plan);
        Assert.Equal(new SvgChartRenderer().Render(plan), new SvgChartRenderer().Render(ChartContractSerializer.DeserializePlotPlan(serialized)));
        var resized = resolver.Relayout(spec, data, plan, new PlotBounds(0m, 0m, 700m, 800m));
        Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, resized.Bounds)), ChartContractSerializer.Serialize(resized));
        var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 220).NormalizedText;
        Assert.Contains("symbol radius 7 px; shape SQUARE; text <hot & high>", terminal);
        Assert.Contains("text suppressed", terminal);
        Assert.Contains(plan.Fallback.Items, item => item.Detail?.Contains("text 42", StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData("LINE")]
    [InlineData("AREA")]
    [InlineData("RIBBON")]
    public async Task AuthoringLineageDesignerAndExportsPreserveDecorationIntent(string form)
    {
        var sql = Sql(form, "ZERO", "ASPECT", true, true);
        var statement = Parse(sql);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = sql + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Route)));";
        var design = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(design.Error);
        Assert.Equal(statement.ToSql(), Parse(new DesignerScriptPatcher().Patch(source, design.DesignState)).ToSql());
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(sql).Tokenize(), sql).Parse());
        foreach (var field in new[] { "Distance", "Flag", "Series", "Cohort" })
            Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Route" && entry.SourceColumns.Contains(field));
        var (spec, data) = Lower(sql);
        Assert.Equal(ChartContractSerializer.Serialize(spec), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(ChartContractSerializer.Serialize(spec))));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var report = new ReportManifest { Title = "Decorated", Source = "decorated.rptsql", Visuals = [new VisualManifest { Name = "Route", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = new SvgChartRenderer().Render(plan) }] };
        var markdown = new MarkdownRenderer().Render(report);
        Assert.Contains("shape SQUARE", markdown);
        Assert.Contains("text 42", markdown);
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await new PdfExporter().ExportAsync(report))[..4]);
        Assert.Contains(PlotPlanConformanceHarness.Evaluate(plan, [new LostDecorationsBackend()]).Issues, issue => issue.SemanticArea == "layers");
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("missing")]
    [InlineData("singleton")]
    public void EmptyMissingAndSingletonSourcesRetainDeclaredEnvelope(string scenario)
    {
        var (spec, data) = Lower(Sql());
        data = scenario == "empty" ? data with { RowCount = 0, Columns = data.Columns.Select(column => column with { Values = [], DisplayValues = [] }).ToImmutableArray() }
            : scenario == "singleton" ? data with { RowCount = 1, Columns = data.Columns.Select(column => column with { Values = [column.Values[0]], DisplayValues = [] }).ToImmutableArray() }
            : data with { Columns = data.Columns.Select(column => column.Name == "Distance" ? column with { Values = column.Values.Select(_ => ChartValue.Null()).ToImmutableArray() } : column).ToImmutableArray() };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(12, plan.Version);
        Assert.True(Assert.Single(plan.Layers).ConnectionDecorations);
        Assert.Empty(plan.Layers.SelectMany(layer => layer.Connections));
        Assert.Equal(scenario == "singleton" ? 1 : 0, Elements(XDocument.Parse(new SvgChartRenderer().Render(plan)), "plot-connection-symbol").Length);
        Assert.Throws<InvalidDataException>((spec with { Version = 8, Schema = ChartContractVersions.TransposedConnectedChartSpecSchema }).Validate);
        Assert.Throws<InvalidDataException>((plan with { Version = 11, Schema = ChartContractVersions.TransposedConnectedPlotPlanSchema }).Validate);
        Assert.Throws<InvalidDataException>((plan with { CartesianAxes = null }).Validate);
    }

    [Theory]
    [InlineData("anchor")]
    [InlineData("radius")]
    [InlineData("shape")]
    [InlineData("text")]
    [InlineData("missing")]
    [InlineData("undeclared")]
    [InlineData("unknown-channel")]
    [InlineData("duplicate-channel")]
    [InlineData("size-kind")]
    [InlineData("shape-kind")]
    public void ContractRejectsLostOrInventedDecorations(string mutation)
    {
        var (spec, data) = Lower(Sql());
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var layer = plan.Layers[0];
        var datum = layer.Data[0];
        var decoration = datum.ConnectionDecoration!;
        datum = mutation switch
        {
            "anchor" => datum with { ConnectionDecoration = decoration with { X = 999m } },
            "radius" => datum with { ConnectionDecoration = decoration with { Radius = 99m } },
            "shape" => datum with { ConnectionDecoration = decoration with { Shape = "HEART" } },
            "text" => datum with { ConnectionDecoration = decoration with { Text = ChartValue.From("lost") } },
            "missing" => datum with { ConnectionDecoration = null },
            "unknown-channel" => datum with { Encodings = datum.Encodings.Add(new((ConditionalEncodingChannel)99, ChartValue.From(2m))) },
            "duplicate-channel" => datum with { Encodings = datum.Encodings.Add(datum.Encodings[0]) },
            "size-kind" => datum with { Encodings = datum.Encodings.Select(value => value.Channel == ConditionalEncodingChannel.Size ? value with { Value = ChartValue.From("seven") } : value).ToImmutableArray() },
            "shape-kind" => datum with { Encodings = datum.Encodings.Select(value => value.Channel == ConditionalEncodingChannel.Shape ? value with { Value = ChartValue.From(3m) } : value).ToImmutableArray() },
            _ => datum
        };
        layer = layer with { Data = layer.Data.SetItem(0, datum), ConnectionDecorations = mutation != "undeclared" };
        Assert.Throws<InvalidDataException>((plan with { Layers = plan.Layers.SetItem(0, layer) }).Validate);
    }

    [Theory]
    [InlineData("7", "'seven'")]
    [InlineData("'square'", "'HEART'")]
    [InlineData("'square'", "7")]
    [InlineData("TYPE = CARTESIAN", "TYPE = POLAR")]
    [InlineData("'LINEAR'", "'MONOTONE'")]
    [InlineData("NULL_HANDLING = GAP", "NULL_HANDLING = SKIP")]
    public void UnsupportedAuthoringRemainsExplicit(string before, string after)
    {
        var statement = Parse(Sql().Replace(before, after, StringComparison.Ordinal));
        Assert.NotEmpty(AdvancedChartSemanticValidator.Validate(statement));
    }

    [Theory]
    [InlineData("CIRCLE")]
    [InlineData("SQUARE")]
    [InlineData("TRIANGLE")]
    [InlineData("DIAMOND")]
    [InlineData("CROSS")]
    [InlineData("STAR")]
    public void ShapeVocabularyProducesPortableGeometry(string shape)
    {
        Assert.Equal(PointShapeVocabulary.IsSupported(shape), ResolvedConnectionDecoration.IsSupportedShape(shape));
        var (spec, data) = Lower(Sql().Replace("'square'", "' " + shape.ToLowerInvariant() + " '", StringComparison.Ordinal));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var symbol = Assert.Single(Elements(XDocument.Parse(new SvgChartRenderer().Render(plan)), "plot-connection-symbol"), element => Row(element) == 0);
        Assert.Equal(shape, (string?)symbol.Attribute("data-symbol-shape") ?? "CIRCLE");
        Assert.Equal(shape == "CIRCLE" ? "circle" : shape == "SQUARE" ? "rect" : "polygon", symbol.Name.LocalName);
        if (shape == "STAR") Assert.Equal(10, symbol.Attribute("points")!.Value.Split(' ').Length);
    }

    [Theory]
    [InlineData("1e300", 30)]
    [InlineData("-1e300", 2)]
    [InlineData("NULL", 3)]
    public void SizeClampPrecedesDecimalConversionAndNullUsesDefault(string size, int expected)
    {
        var (spec, data) = Lower(Sql());
        spec = spec with { Layers = spec.Layers.Select(layer => layer with { Conditions = layer.Conditions.Select(condition => condition.Channel == ConditionalEncodingChannel.Size ? condition with { WhenTrue = size == "NULL" ? ChartValue.Null() : ChartValue.From(double.Parse(size, CultureInfo.InvariantCulture)), WhenFalse = ChartValue.Null() } : condition).ToImmutableArray() }).ToImmutableArray() };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(Convert.ToDecimal(expected), plan.Layers[0].Data[0].ConnectionDecoration!.Radius);
        var symbol = Assert.Single(Elements(XDocument.Parse(new SvgChartRenderer().Render(plan)), "plot-connection-symbol"), element => Row(element) == 0);
        Assert.Equal(N(expected * 2m), symbol.Attribute("width")!.Value);
    }

    [Fact]
    public void SymbolsOffRetainsExplicitAnnotationsAndNullSuppressesNumericLabels()
    {
        var (spec, data) = Lower(Sql());
        spec = spec with { Theme = spec.Theme with { Tokens = spec.Theme.Tokens.Add(new("SYMBOLS", "OFF")).Add(new("DATA_LABELS", "ON")) } };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
        Assert.Empty(Elements(svg, "plot-connection-symbol"));
        var labels = svg.Descendants().Where(element => (string?)element.Attribute("class") is "plot-smart-label" or "plot-smart-label-occluded").ToArray();
        Assert.Equal(new[] { 0, 4, 6, 7 }, labels.Select(Row).Order().ToArray());
        Assert.Contains(labels, label => label.Value == "<hot & high>");
        Assert.Contains(labels, label => Row(label) == 0 && (string?)label.Attribute("opacity") == "0");
    }

    [Theory]
    [InlineData("CARTESIAN")]
    [InlineData("TRANSPOSED_CARTESIAN")]
    [InlineData("ASPECT")]
    public void MixedPoliciesAndOrdinaryPointsKeepDecorationsInTheirOwnLayer(string coordinate)
    {
        var (spec, data) = Lower(Sql(coordinate: coordinate, grouped: true, facets: true));
        var line = spec.Layers[0];
        spec = spec with
        {
            Layers = [line,
            line with { Id = "area", Mark = MarkKind.Area, ZIndex = 1, Style = line.Style.Add(new("areaBaseline", "ZERO")).Select(token => token.Name == "nullHandling" ? token with { Value = "ZERO" } : token).ToImmutableArray() },
            line with { Id = "ribbon", Mark = MarkKind.Area, ZIndex = 2,
                Bindings = line.Bindings.Where(binding => binding.Channel != FieldChannel.Y).Append(new FieldBinding(FieldChannel.YStart, "Lower", DataSemanticKind.Quantitative, "vertical")).Append(new FieldBinding(FieldChannel.YEnd, "Upper", DataSemanticKind.Quantitative, "vertical")).ToImmutableArray(),
                Style = line.Style.Select(token => token.Name == "nullHandling" ? token with { Value = "CONNECT" } : token).ToImmutableArray() },
            line with { Id = "observations", Mark = MarkKind.Point, ZIndex = 3, Conditions = [] }]
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
        Assert.Equal(18, Elements(svg, "plot-connection-symbol").Length);
        Assert.Equal(32, plan.Layers.Sum(layer => layer.Data.Length));
        Assert.All(plan.Layers.Where(layer => layer.Mark == MarkKind.Point), layer =>
        {
            Assert.False(layer.ConnectionDecorations);
            Assert.All(layer.Data, datum => Assert.Null(datum.ConnectionDecoration));
        });
        Assert.All(plan.Layers.Where(layer => !layer.Connections.IsDefault), layer =>
        {
            Assert.True(layer.ConnectionDecorations);
            Assert.All(layer.Connections, connection => Assert.All(connection.Encodings, value => Assert.True(value.Channel is ConditionalEncodingChannel.Color or ConditionalEncodingChannel.Opacity)));
        });
    }

    [Theory]
    [InlineData("CARTESIAN")]
    [InlineData("TRANSPOSED_CARTESIAN")]
    [InlineData("ASPECT")]
    public void UnmatchedAndNullConditionsUseDefaultsWithoutInventingText(string coordinate)
    {
        var sql = Sql(coordinate: coordinate).Replace("SIZE WHEN Flag = 'first' THEN 7", "SIZE WHEN Flag = 'first' THEN NULL", StringComparison.Ordinal)
            .Replace("SIZE WHEN Flag = 'middle' THEN 99 ELSE -2", "SIZE WHEN Flag = 'never' THEN 99", StringComparison.Ordinal)
            .Replace("SHAPE WHEN Flag = 'first' THEN 'square' ELSE 'CIRCLE'", "SHAPE WHEN Flag = 'first' THEN NULL", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        spec = spec with { Theme = spec.Theme with { Tokens = spec.Theme.Tokens.Add(new("SYMBOL_SIZE", "8")).Add(new("SYMBOL_SHAPE", "STAR")) } };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var first = plan.Layers[0].Data[0];
        var last = plan.Layers[0].Data[^1];
        Assert.Equal(8m, first.ConnectionDecoration!.Radius);
        Assert.Equal("STAR", first.ConnectionDecoration.Shape);
        Assert.Equal(ChartValueKind.Null, first.Encodings.Single(value => value.Channel == ConditionalEncodingChannel.Size).Value.Kind);
        Assert.DoesNotContain(last.Encodings, value => value.Channel is ConditionalEncodingChannel.Size or ConditionalEncodingChannel.Shape);
        Assert.Equal(8m, last.ConnectionDecoration!.Radius);
        Assert.Equal("STAR", last.ConnectionDecoration.Shape);
        var middle = plan.Layers[0].Data[3];
        Assert.Equal(ChartValue.From(""), middle.ConnectionDecoration!.Text);
        Assert.Equal(ChartValue.From(42m), last.ConnectionDecoration.Text);
        var rawNull = plan.Layers[0].Data[1];
        Assert.Null(rawNull.ConnectionDecoration);
        var serialized = ChartContractSerializer.Serialize(plan);
        Assert.Contains("\"text\"", serialized);
        var symbols = Elements(XDocument.Parse(new SvgChartRenderer().Render(plan)), "plot-connection-symbol");
        Assert.All(symbols, symbol => Assert.Equal("STAR", symbol.Attribute("data-symbol-shape")!.Value));
    }

    [Theory]
    [InlineData("LINE", "E2D5C52042DAC1D3F757A3BB78B04CDA13A174A410E4543A83E233CFCDC6EB40", "66A1DAB3001B413A20E2217D5B2D5B221EB7AA94751D9890A7FBD58C5EC43EB8")]
    [InlineData("AREA", "A1FBFD88233C36E4AEB2102C248D6E41521ED38E9911987DB8DFC940F3BB3719", "18A6276986F05BC59509B967B8476FE345B0A3BAC4BDAB4D19FCFF0609DF7C50")]
    [InlineData("RIBBON", "1F81919F6D436B507418A8EB3E810E8A48A1CC4011E9CF1A900E0A30E446535E", "76F80BA99C1D2B0AAAA0A45B35E813A12B103F67DAB5BAB612A7ACAC60540705")]
    public void DecoratedPlanAndSvgHaveDeterministicFingerprints(string form, string expectedPlan, string expectedSvg)
    {
        var (spec, data) = Lower(Sql(form, "ZERO", "ASPECT", true, true));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n"))));
        var actualPlan = Hash(ChartContractSerializer.Serialize(plan));
        var actualSvg = Hash(new SvgChartRenderer().Render(plan));
        Assert.True(expectedPlan == actualPlan && expectedSvg == actualSvg, actualPlan + "\n" + actualSvg);
    }

    [Fact]
    public void FocusedHelpDecorationExampleLowersAndRenders()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        var text = File.ReadAllText(Path.Combine(directory!.FullName, "docs", "reference", "visuals-reporting", "visuals", "chart.md")).Replace("\r\n", "\n");
        var example = Regex.Matches(text, "```sql\\n(?<sql>.*?)\\n```", RegexOptions.Singleline).Cast<Match>()
            .Select(match => match.Groups["sql"].Value).Single(sql => sql.Contains("CREATE VISUAL DecoratedRoute", StringComparison.Ordinal));
        var (spec, data) = Lower(example);
        Assert.Equal(9, spec.Version);
        Assert.Equal(Parse(example).ToSql(), Parse(Parse(example).ToSql()).ToSql());
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(12, plan.Version);
        var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
        Assert.Equal(7, Elements(svg, "plot-connection-symbol").Length);
        Assert.Contains(svg.Descendants(), element => element.Value == "Check this endpoint");
    }

    private sealed class LostDecorationsBackend : IPlotPlanSemanticBackend
    {
        public string Name => "lost-decorations";
        public PlotSemanticProjection Project(PlotPlan plan) => PlotSemanticProjection.FromPlan(plan) with { Layers = PlotSemanticProjection.FromPlan(plan).Layers.Select(layer => layer with { Decorations = default }).ToImmutableArray() };
    }

    private static decimal Raw(ChartDataSet data, string field, int row) => PlotPlanResolver.Number(data.Columns.Single(column => column.Name == field).Values[row]) ?? 0m;
    private static string N(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static int Row(XElement element) => int.Parse(element.Attribute("data-row-index")!.Value, CultureInfo.InvariantCulture);
    private static XElement[] Elements(XDocument document, string cssClass) => document.Descendants().Where(element => (string?)element.Attribute("class") == cssClass).ToArray();
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
            Columns = ["Distance", "Estimate", "Lower", "Upper", "Flag", "Series", "Cohort"],
            Rows = [["5", "2", "1", "3", "first", "S", "A"], ["3", null, "2", "6", "null", "T", "A"],
                [null, "3", null, "4", "ignored", "S", "B"], ["1", "-4", "5", "3", "middle", "T", "B"],
                ["4", "1", "0", "2", "last", "S", "A"], ["6", null, "1", null, "null", "T", "A"],
                ["7", "0", "0", "0", "last", "S", "B"], ["8", "5", "3", "2", "last", "T", "B"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
