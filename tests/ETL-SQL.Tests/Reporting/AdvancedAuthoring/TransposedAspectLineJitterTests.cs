using System.Buffers.Binary;
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

public sealed class TransposedAspectLineJitterTests
{
    private const string Script = """
        CREATE VISUAL Route AS CUSTOM (SOURCE = #prepared, CHART (
          COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
          SCALES (distances = LINEAR (CHANNEL = X, MIN = 0, MAX = 10),
                  estimates = LINEAR (CHANNEL = Y, MIN = 0, MAX = 10)),
          LAYERS (route = LINE (
            INHERIT_ENCODINGS = OFF,
            NULL_HANDLING = GAP,
            POSITION = JITTER(X = 0.02, Y = 0.03, KEY = Id, SEED = 42),
            ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = distances),
                       Y = Estimate (TYPE = QUANTITATIVE, SCALE = estimates)),
            STYLE (INTERPOLATION = 'LINEAR', COLOR = '#112233')
          ))
        ));
        """;

    public static IEnumerable<object[]> GeometryCases()
    {
        foreach (var reverse in new[] { false, true })
            foreach (var logarithmic in new[] { false, true })
                foreach (var facets in new[] { false, true })
                    foreach (var missing in new[] { "none", "Distance", "Estimate" }) yield return [reverse, logarithmic, facets, missing];
    }

    [Theory]
    [MemberData(nameof(GeometryCases))]
    public void SeededOffsetsMoveEachVertexAndSymbolWithoutChangingValues(bool reverse, bool logarithmic, bool facets, string missing)
    {
        var sql = Script;
        if (reverse) sql = sql.Replace("MIN = 0,", "REVERSE = ON, MIN = 0,", StringComparison.Ordinal);
        if (logarithmic) sql = sql.Replace("LINEAR (CHANNEL", "LOGARITHMIC (CHANNEL", StringComparison.Ordinal).Replace("MIN = 0,", "INCLUDE_ZERO = OFF, MIN = 1,", StringComparison.Ordinal);
        if (facets) sql = sql.Replace("SCALES (", "FACET (WRAP = Cohort, COLUMNS = 2), RESOLVE (X = INDEPENDENT, Y = INDEPENDENT), SCALES (", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        if (missing != "none") data = data with
        {
            Columns = data.Columns.Select(column => column.Name == missing ? column with
            { Values = column.Values.SetItem(2, ChartValue.Null()), DisplayValues = [] } : column).ToImmutableArray()
        };
        var resolver = new PlotPlanResolver();
        var initial = resolver.Resolve(spec, data);
        foreach (var bounds in new[] { initial.Bounds, new PlotBounds(0m, 0m, 1000m, 650m) })
        {
            var plan = resolver.Relayout(spec, data, initial, bounds);
            var baseline = resolver.Resolve(spec with { Layers = [spec.Layers[0] with { Position = null }] }, data, bounds);
            Assert.Equal(2, spec.Version);
            Assert.Equal(3, plan.Version);
            Assert.Equal(baseline.Scales.SelectMany(scale => scale.Domain), plan.Scales.SelectMany(scale => scale.Domain));
            Assert.Equal(baseline.Fallback.Items.ToArray(), plan.Fallback.Items.ToArray());
            Assert.Equal(baseline.Layers[0].Data.SelectMany(datum => datum.Channels), plan.Layers[0].Data.SelectMany(datum => datum.Channels));
            foreach (var datum in plan.Layers[0].Data)
            {
                var viewport = facets ? Assert.Single(plan.Facets, panel => panel.RowIndices.Contains(datum.RowIndex)).CartesianViewport! : plan.CartesianViewport!;
                var key = "K" + datum.RowIndex;
                Assert.Equal(Hash(key, "y", facets) * 0.03m * (viewport.Width - 80m), datum.DisplayOffsetX);
                Assert.Equal(-Hash(key, "x", facets) * 0.02m * (viewport.Height - 100m), datum.DisplayOffsetY);
            }
            var document = XDocument.Parse(new SvgChartRenderer().Render(plan));
            var symbols = Elements(document, "plot-line-symbol");
            var before = Elements(XDocument.Parse(new SvgChartRenderer().Render(baseline)), "plot-line-symbol");
            Assert.Equal(missing == "none" ? 6 : 5, symbols.Length);
            for (var index = 0; index < symbols.Length; index++)
            {
                var row = int.Parse(symbols[index].Attribute("data-row-index")!.Value, CultureInfo.InvariantCulture);
                var datum = Assert.Single(plan.Layers[0].Data, datum => datum.RowIndex == row);
                Assert.InRange(Math.Abs(Read(symbols[index], "cx") - Read(before[index], "cx") - datum.DisplayOffsetX), 0m, 0.002m);
                Assert.InRange(Math.Abs(Read(symbols[index], "cy") - Read(before[index], "cy") - datum.DisplayOffsetY), 0m, 0.002m);
                Assert.Equal(before[index].Value, symbols[index].Value);
            }
            var paths = Paths(document);
            Assert.Equal(facets ? 2 : missing == "none" ? 1 : 2, paths.Length);
            var vertices = paths.SelectMany(path => path.Attribute("d")!.Value.Split(' ').Chunk(3)).ToArray();
            // A singleton before a facet-local gap has a symbol but no path.
            Assert.All(vertices, vertex => Assert.Contains(symbols, symbol => symbol.Attribute("cx")!.Value == vertex[1] && symbol.Attribute("cy")!.Value == vertex[2]));
            Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, bounds)), ChartContractSerializer.Serialize(plan));
            Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
            Assert.Equal(TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(baseline), 140).NormalizedText,
                TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 140).NormalizedText);
        }
    }

    [Fact]
    public void KeysKeepOffsetsThroughRowAndFieldRenamesWhileSeedChangesPattern()
    {
        var (spec, data) = Lower(Script);
        var resolver = new PlotPlanResolver();
        var original = resolver.Resolve(spec, data);
        var order = new[] { 5, 3, 1, 4, 0, 2 };
        var reordered = data with { Columns = data.Columns.Select(column => column with { Values = order.Select(row => column.Values[row]).ToImmutableArray(), DisplayValues = [] }).ToImmutableArray() };
        var changed = resolver.Resolve(spec, reordered);
        for (var index = 0; index < order.Length; index++)
        {
            Assert.Equal(original.Layers[0].Data[order[index]].DisplayOffsetX, changed.Layers[0].Data[index].DisplayOffsetX);
            Assert.Equal(original.Layers[0].Data[order[index]].DisplayOffsetY, changed.Layers[0].Data[index].DisplayOffsetY);
        }
        var document = XDocument.Parse(new SvgChartRenderer().Render(changed));
        var expectedPath = string.Join(" ", Elements(document, "plot-line-symbol").Select((symbol, index) => $"{(index == 0 ? "M" : "L")} {symbol.Attribute("cx")!.Value} {symbol.Attribute("cy")!.Value}"));
        Assert.Equal(expectedPath, Assert.Single(Paths(document)).Attribute("d")!.Value);
        var renamed = Lower(Script.Replace("route = LINE", "renamed = LINE", StringComparison.Ordinal).Replace("distances", "renamedScale", StringComparison.Ordinal).Replace("Distance", "RenamedDistance", StringComparison.Ordinal)).Spec;
        var renamedData = data with { Columns = data.Columns.Select(column => column.Name == "Distance" ? column with { Name = "RenamedDistance" } : column).ToImmutableArray() };
        Assert.Equal(original.Layers[0].Data.Select(datum => (datum.DisplayOffsetX, datum.DisplayOffsetY)), resolver.Resolve(renamed, renamedData).Layers[0].Data.Select(datum => (datum.DisplayOffsetX, datum.DisplayOffsetY)));
        var renamedKey = spec with { Layers = [spec.Layers[0] with { Position = spec.Layers[0].Position! with { StableKeyField = "RenamedId" } }] };
        var keyData = data with { Columns = data.Columns.Select(column => column.Name == "Id" ? column with { Name = "RenamedId" } : column).ToImmutableArray() };
        Assert.Equal(original.Layers[0].Data.Select(datum => (datum.DisplayOffsetX, datum.DisplayOffsetY)), resolver.Resolve(renamedKey, keyData).Layers[0].Data.Select(datum => (datum.DisplayOffsetX, datum.DisplayOffsetY)));
        var seeded = spec with { Layers = [spec.Layers[0] with { Position = spec.Layers[0].Position! with { Seed = 43 } }] };
        Assert.NotEqual(original.Layers[0].Data[0].DisplayOffsetX, resolver.Resolve(seeded, data).Layers[0].Data[0].DisplayOffsetX);
    }

    [Theory]
    [InlineData("LEFT")]
    [InlineData("RIGHT")]
    [InlineData("BOTTOM")]
    public void OtherLayersColorGroupsAndSideLegendsKeepOneSourceOrderedLine(string legend)
    {
        var (spec, data) = Lower(Script);
        var point = spec.Layers[0] with
        {
            Id = "observations",
            Mark = MarkKind.Point,
            Position = null,
            Style = [],
            Bindings = spec.Layers[0].Bindings.Add(new FieldBinding(FieldChannel.Color, "Cohort", DataSemanticKind.Nominal))
        };
        var rectangle = new MarkLayerSpec("region", MarkKind.Rect, 2,
            [FieldBinding.Datum(FieldChannel.XStart, ChartValue.From(2m), DataSemanticKind.Quantitative),
             FieldBinding.Datum(FieldChannel.XEnd, ChartValue.From(8m), DataSemanticKind.Quantitative),
             FieldBinding.Datum(FieldChannel.YStart, ChartValue.From(1m), DataSemanticKind.Quantitative),
             FieldBinding.Datum(FieldChannel.YEnd, ChartValue.From(4m), DataSemanticKind.Quantitative)], []);
        spec = spec with
        {
            Bindings = spec.Bindings.Add(point.Bindings[^1]),
            Layers = [point, spec.Layers[0] with { ZIndex = 1 }, rectangle],
            Theme = spec.Theme with { Tokens = [.. spec.Theme.Tokens, new StyleToken("LEGEND_POSITION", legend)] }
        };
        foreach (var bounds in new[] { new PlotBounds(0m, 0m, 800m, 600m), new PlotBounds(0m, 0m, 1000m, 650m) })
        {
            var plan = new PlotPlanResolver().Resolve(spec, data, bounds);
            var document = XDocument.Parse(new SvgChartRenderer().Render(plan));
            Assert.Single(Paths(document));
            var points = Elements(document, "plot-point");
            var symbols = Elements(document, "plot-line-symbol");
            Assert.Equal(6, symbols.Length);
            Assert.All(plan.Layers.Where(layer => layer.Mark != MarkKind.Line).SelectMany(layer => layer.Data), datum =>
            {
                Assert.Equal(0m, datum.DisplayOffsetX);
                Assert.Equal(0m, datum.DisplayOffsetY);
            });
            foreach (var datum in plan.Layers.Single(layer => layer.Mark == MarkKind.Line).Data)
            {
                var symbol = Assert.Single(symbols, symbol => symbol.Attribute("data-row-index")!.Value == datum.RowIndex.ToString(CultureInfo.InvariantCulture));
                var observation = Assert.Single(points, point => point.Attribute("data-row-index")!.Value == datum.RowIndex.ToString(CultureInfo.InvariantCulture));
                Assert.InRange(Math.Abs(Read(symbol, "cx") - Read(observation, "cx") - datum.DisplayOffsetX), 0m, 0.002m);
                Assert.InRange(Math.Abs(Read(symbol, "cy") - Read(observation, "cy") - datum.DisplayOffsetY), 0m, 0.002m);
            }
            Assert.Contains("X = 2 to 8; Y = 1 to 4", TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 140).NormalizedText);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("duplicate")]
    public void KeyValidationIncludesRowsWhoseCoordinatesAreMissing(string invalid)
    {
        var (spec, data) = Lower(Script);
        data = data with { Columns = data.Columns.Select(column => column.Name == "Distance" ? column with { Values = column.Values.SetItem(2, ChartValue.Null()), DisplayValues = [] } : column).ToImmutableArray() };
        data = data with
        {
            Columns = invalid == "missing" ? data.Columns.Where(column => column.Name != "Id").ToImmutableArray()
            : data.Columns.Select(column => column.Name == "Id" ? column with { Values = column.Values.SetItem(2, invalid == "null" ? ChartValue.Null() : column.Values[0]), DisplayValues = [] } : column).ToImmutableArray()
        };
        Assert.Contains(invalid == "missing" ? "does not exist" : invalid == "null" ? "contains nulls" : "duplicate", Assert.Throws<InvalidOperationException>(() => new PlotPlanResolver().Resolve(spec, data)).Message);
    }

    [Theory]
    [InlineData(-0.01, 0.03)]
    [InlineData(0.02, 1.01)]
    public void InvalidAmplitudesFailAuthoringAndContractValidation(double x, double y)
    {
        var sql = Script.Replace("X = 0.02, Y = 0.03", $"X = {x.ToString(CultureInfo.InvariantCulture)}, Y = {y.ToString(CultureInfo.InvariantCulture)}", StringComparison.Ordinal);
        Assert.Contains(AdvancedChartSemanticValidator.Validate(Parse(sql)), diagnostic => diagnostic.Line > 0 && diagnostic.Column > 0 && diagnostic.Message.Contains("amplitudes", StringComparison.Ordinal));
        var (spec, _) = Lower(Script);
        var invalid = spec with { Layers = [spec.Layers[0] with { Position = spec.Layers[0].Position! with { X = (decimal)x, Y = (decimal)y } }] };
        Assert.Contains("amplitudes", Assert.Throws<InvalidDataException>(invalid.Validate).Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EmptySingletonAndAllMissingInputsHaveNoSpuriousPaths(int profile)
    {
        var (spec, data) = Lower(Script);
        data = data with
        {
            RowCount = profile == 0 ? 0 : 1,
            Columns = data.Columns.Select(column => column with
            { Values = profile == 0 ? [] : [profile == 2 && column.Name == "Distance" ? ChartValue.Null() : column.Values[0]], DisplayValues = [] }).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var document = XDocument.Parse(new SvgChartRenderer().Render(plan));
        Assert.Empty(Paths(document));
        Assert.Equal(profile == 1 ? 1 : 0, Elements(document, "plot-line-symbol").Length);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    public void BoundaryAmplitudesAndConstantCoordinatesRetainSeparateVertices(int x, int y)
    {
        var (spec, data) = Lower(Script.Replace("X = 0.02, Y = 0.03", $"X = {x}, Y = {y}", StringComparison.Ordinal).Replace("X = Distance", "X = DATUM(5)", StringComparison.Ordinal).Replace("Y = Estimate", "Y = DATUM(5)", StringComparison.Ordinal));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var document = XDocument.Parse(new SvgChartRenderer().Render(plan));
        Assert.Equal(6, Elements(document, "plot-line-symbol").Length);
        Assert.Equal(18, Assert.Single(Paths(document)).Attribute("d")!.Value.Split(' ').Length);
        Assert.All(plan.Layers[0].Data, datum =>
        {
            Assert.InRange(Math.Abs(datum.DisplayOffsetX), 0m, y * (plan.CartesianViewport!.Width - 80m));
            Assert.InRange(Math.Abs(datum.DisplayOffsetY), 0m, x * (plan.CartesianViewport!.Height - 100m));
        });
    }

    [Fact]
    public async Task AuthoringDesignerLineageLabelsAndPdfRetainRawIntent()
    {
        var statement = Parse(Script);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = Script + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Route)));";
        var design = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(design.Error);
        Assert.Equal(statement.ToSql(), Parse(new DesignerScriptPatcher().Patch(source, design.DesignState)).ToSql());
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(Script).Tokenize(), Script).Parse());
        foreach (var field in new[] { "Distance", "Estimate", "Id" })
            Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Route" && entry.SourceColumns.Contains(field));
        var (spec, data) = Lower(Script);
        Assert.Equal(ChartContractSerializer.Serialize(spec), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(ChartContractSerializer.Serialize(spec))));
        data = data with { RowCount = 1, Columns = data.Columns.Select(column => column with { Values = [column.Values[0]], DisplayValues = [] }).ToImmutableArray() };
        spec = spec with { Theme = spec.Theme with { Tokens = [.. spec.Theme.Tokens, new StyleToken("DATA_LABELS", "ON")] } };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var svg = new SvgChartRenderer().Render(plan);
        var document = XDocument.Parse(svg);
        var symbol = Assert.Single(Elements(document, "plot-line-symbol"));
        var label = Assert.Single(Elements(document, "plot-smart-label"));
        Assert.Equal("1", label.Value);
        Assert.InRange(Math.Abs(Read(label, "x") - Read(symbol, "cx")), 0m, 20m);
        Assert.InRange(Math.Abs(Read(label, "y") - Read(symbol, "cy")), 0m, 20m);
        var report = new ReportManifest { Title = "Jittered", Source = "jittered.rptsql", Visuals = [new VisualManifest { Name = "Route", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = svg }] };
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await new PdfExporter().ExportAsync(report))[..4]);
    }

    [Fact]
    public void PlanAndSvgHaveDeterministicFingerprints()
    {
        var (spec, data) = Lower(Script);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Fingerprint(ChartContractSerializer.Serialize(plan));
        var svgHash = Fingerprint(new SvgChartRenderer().Render(plan));
        Assert.True(planHash == "B4C248F1761BC76361BB4553482B1E3A0C42B87CCB1702AA1AD375415DDD545F" && svgHash == "BAD820C5EAF77E48DF9B00C478906779BD0E20EE595DA452A456CFF8AA12F843", $"Plan: {planHash}; SVG: {svgHash}");
    }

    [Fact]
    public void RepresentativeWorkloadRetainsExistingBudgets()
    {
        const int rowCount = 600;
        var (spec, data) = Lower(Script.Replace("SCALES (", "FACET (WRAP = Cohort, COLUMNS = 4), RESOLVE (X = INDEPENDENT, Y = INDEPENDENT), SCALES (", StringComparison.Ordinal));
        data = data with
        {
            RowCount = rowCount,
            Columns = data.Columns.Select(column => column with
            { Values = Enumerable.Range(0, rowCount).Select(row => column.Name == "Id" ? ChartValue.From("K" + row) : column.Name == "Cohort" ? ChartValue.From("P" + row / 50) : column.Values[row % 6]).ToImmutableArray(), DisplayValues = [] }).ToImmutableArray()
        };
        var before = GC.GetAllocatedBytesForCurrentThread();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0m, 0m, 1200m, 800m));
        var allocation = GC.GetAllocatedBytesForCurrentThread() - before;
        var resolution = clock.Elapsed;
        clock.Restart();
        var svg = new SvgChartRenderer().Render(plan);
        Assert.Equal(12, plan.Facets.Length);
        Assert.Equal(rowCount, Elements(XDocument.Parse(svg), "plot-line-symbol").Length);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "LINE SVG rendering exceeded 5 seconds."); // flaky-time-bound-ok: 5 seconds bounds a 600-row, 12-panel native workload with ample headroom.
        Assert.True(resolution < TimeSpan.FromSeconds(5), "LINE resolution exceeded 5 seconds."); // flaky-time-bound-ok: 5 seconds bounds a 600-row, 12-panel native workload with ample headroom.
        Assert.True(allocation < 16L * 1024 * 1024, $"LINE resolution allocated {allocation:N0} bytes.");
        Assert.InRange(Encoding.UTF8.GetByteCount(ChartContractSerializer.Serialize(plan)), 1, 6 * 1024 * 1024);
        Assert.InRange(Encoding.UTF8.GetByteCount(svg), 1, 600 * 1024);
    }

    [Fact]
    public void PublishedJitterExampleParsesLowersAndResolves()
    {
        var path = Path.Combine(TerminalSnapshotHarness.GetRepoRoot(), "docs", "reference", "visuals-reporting", "visuals", "chart.md");
        var example = Regex.Matches(File.ReadAllText(path), @"```sql\s*(.*?)```", RegexOptions.Singleline)
            .Select(match => match.Groups[1].Value.Trim()).Single(block => block.StartsWith("CREATE VISUAL JitteredRoute AS CUSTOM", StringComparison.Ordinal));
        var (spec, data) = Lower(example);
        Assert.Equal(Parse(example).ToSql(), Parse(Parse(example).ToSql()).ToSql());
        Assert.Equal(PositionAdjustmentKind.Jitter, spec.Layers[0].Position!.Kind);
        Assert.Equal(6, new PlotPlanResolver().Resolve(spec, data).Layers[0].Data.Length);
    }

    private static decimal Hash(string key, string axis, bool facets)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"Route\u001fLine|0|X,Y{(facets ? ",Wrap" : "")}\u001f{key}\u001f{axis}\u001f42"));
        return (decimal)BinaryPrimitives.ReadUInt64BigEndian(bytes) / ulong.MaxValue * 2m - 1m;
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
            Columns = ["Distance", "Estimate", "Cohort", "Id"],
            Rows = [["8", "1", "A", "K0"], ["2", "7", "A", "K1"], ["5", "3", "A", "K2"], ["1", "8", "B", "K3"], ["9", "2", "B", "K4"], ["4", "6", "B", "K5"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
