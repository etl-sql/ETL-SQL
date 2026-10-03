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

    public static IEnumerable<object[]> EmNudgeCases()
    {
        foreach (var reverse in new[] { false, true })
            foreach (var logarithmic in new[] { false, true })
                foreach (var facets in new[] { false, true })
                    foreach (var missing in new[] { "none", "Distance", "Estimate" }) yield return [reverse, logarithmic, facets, missing];
    }

    [Theory]
    [MemberData(nameof(EmNudgeCases))]
    public void EmNudgeTranslatesLineAndSymbolsWithoutChangingRawValues(bool reverse, bool logarithmic, bool facets, string missing)
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
        var nudged = Lower(sql.Replace("INHERIT_ENCODINGS = OFF,", "INHERIT_ENCODINGS = OFF, POSITION = NUDGE(X = 1, Y = -0.5, UNIT = EM),", StringComparison.Ordinal)).Spec;
        var resolver = new PlotPlanResolver();
        var initial = resolver.Resolve(nudged, data);
        foreach (var bounds in new[] { initial.Bounds, new PlotBounds(0m, 0m, 1000m, 650m) })
        {
            var original = resolver.Resolve(spec, data, bounds);
            var shifted = resolver.Relayout(nudged, data, initial, bounds);
            Assert.Equal(2, nudged.Version);
            Assert.Equal(3, shifted.Version);
            Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(nudged, data, bounds)), ChartContractSerializer.Serialize(shifted));
            Assert.Equal(ChartContractSerializer.Serialize(shifted), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(shifted))));
            Assert.Equal(original.Fallback.Items.ToArray(), shifted.Fallback.Items.ToArray());
            for (var index = 0; index < original.Layers[0].Data.Length; index++)
            {
                var datum = shifted.Layers[0].Data[index];
                Assert.Equal(original.Layers[0].Data[index].Channels.ToArray(), datum.Channels.ToArray());
                Assert.Equal(-6m, datum.DisplayOffsetX);
                Assert.Equal(-12m, datum.DisplayOffsetY);
            }
            var before = Elements(XDocument.Parse(new SvgChartRenderer().Render(original)), "plot-line-symbol");
            var afterDocument = XDocument.Parse(new SvgChartRenderer().Render(shifted));
            var after = Elements(afterDocument, "plot-line-symbol");
            Assert.Equal(missing == "none" ? 6 : 5, after.Length);
            foreach (var symbol in after)
            {
                var raw = Assert.Single(before, before => (string?)before.Attribute("data-row-index") == (string?)symbol.Attribute("data-row-index"));
                Assert.Equal(Read(raw, "cx") - 6m, Read(symbol, "cx"));
                Assert.Equal(Read(raw, "cy") - 12m, Read(symbol, "cy"));
            }
            var paths = Paths(afterDocument);
            var originalPaths = Paths(XDocument.Parse(new SvgChartRenderer().Render(original)));
            Assert.Equal(originalPaths.Length, paths.Length);
            for (var index = 0; index < paths.Length; index++)
            {
                var tokens = originalPaths[index].Attribute("d")!.Value.Split(' ');
                for (var point = 0; point < tokens.Length; point += 3)
                {
                    tokens[point + 1] = (decimal.Parse(tokens[point + 1], CultureInfo.InvariantCulture) - 6m).ToString("0.###", CultureInfo.InvariantCulture);
                    tokens[point + 2] = (decimal.Parse(tokens[point + 2], CultureInfo.InvariantCulture) - 12m).ToString("0.###", CultureInfo.InvariantCulture);
                }
                Assert.Equal(string.Join(" ", tokens), paths[index].Attribute("d")!.Value);
            }
            Assert.Equal(TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(original), 140).NormalizedText,
                TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(shifted), 140).NormalizedText);
        }
    }

    [Fact]
    public async Task EmNudgeAuthoringDesignerLineageLabelsAndPdfPreserveIntent()
    {
        var sql = Script.Replace("INHERIT_ENCODINGS = OFF,", "INHERIT_ENCODINGS = OFF, POSITION = NUDGE(X = -1, Y = 0.5, UNIT = EM),", StringComparison.Ordinal);
        var statement = Parse(sql);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = sql + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Route)));";
        var design = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(design.Error);
        Assert.Equal(statement.ToSql(), Parse(new DesignerScriptPatcher().Patch(source, design.DesignState)).ToSql());
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(sql).Tokenize(), sql).Parse());
        foreach (var field in new[] { "Distance", "Estimate" })
            Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Route" && entry.SourceColumns.Contains(field));
        var (spec, data) = Lower(sql);
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
        var report = new ReportManifest { Title = "Nudged", Source = "nudged.rptsql", Visuals = [new VisualManifest { Name = "Route", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = svg }] };
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await new PdfExporter().ExportAsync(report))[..4]);
    }

    [Fact]
    public void EmNudgePlanAndSvgHaveDeterministicFingerprints()
    {
        var (spec, data) = Lower(Script.Replace("INHERIT_ENCODINGS = OFF,", "INHERIT_ENCODINGS = OFF, POSITION = NUDGE(X = 1, Y = -0.5, UNIT = EM),", StringComparison.Ordinal));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        Assert.True(planHash == "BBD088F6DFB048B85531122B53EAB0806B32877495419007D8BE1EA181FA7977" && svgHash == "5E7D063772D58144532DF8E491CD1384D9428475629C2281B8DEA65FB8CF4102", $"Plan: {planHash}; SVG: {svgHash}");
    }

    [Theory]
    [InlineData("EM")]
    [InlineData("BAND")]
    [InlineData("DATA")]
    public void DisplayNudgeFocusedExamplesParseLowerAndResolve(string unit)
    {
        var path = Path.Combine(TerminalSnapshotHarness.GetRepoRoot(), "docs", "reference", "visuals-reporting", "visuals", "chart.md");
        var example = Regex.Matches(File.ReadAllText(path), @"```sql\s*(.*?)```", RegexOptions.Singleline)
            .Select(match => match.Groups[1].Value.Trim()).Single(block => unit == "DATA" ? block.StartsWith("CREATE VISUAL DataRoute AS CUSTOM", StringComparison.Ordinal)
                : unit == "BAND" ? block.StartsWith("CREATE VISUAL BandRoute AS CUSTOM", StringComparison.Ordinal)
                : block.StartsWith("CREATE VISUAL Route AS CUSTOM", StringComparison.Ordinal) && block.Contains("POSITION = NUDGE(X = 0.5, Y = -0.25, UNIT = EM)", StringComparison.Ordinal));
        var (spec, data) = Lower(example);
        Assert.Equal(Parse(example).ToSql(), Parse(Parse(example).ToSql()).ToSql());
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(unit == "EM" ? PositionAdjustmentUnit.Em : unit == "BAND" ? PositionAdjustmentUnit.Band : PositionAdjustmentUnit.Data, spec.Layers[0].Position!.Unit);
        Assert.All(plan.Layers[0].Data, datum =>
        {
            decimal Span(FieldChannel channel)
            {
                var scale = plan.Scales.Single(scale => scale.Channel == channel);
                return PlotPlanResolver.Number(scale.Domain[^1])!.Value - PlotPlanResolver.Number(scale.Domain[0])!.Value;
            }
            var dx = unit == "EM" ? -3m : (unit == "BAND" ? -0.03m : -0.5m / Span(FieldChannel.Y)) * (plan.CartesianViewport!.Width - 80m);
            var dy = unit == "EM" ? -6m : (unit == "BAND" ? -0.02m : -0.5m / Span(FieldChannel.X)) * (plan.CartesianViewport!.Height - 100m);
            Assert.InRange(Math.Abs(dx - datum.DisplayOffsetX), 0m, 0.000000001m);
            Assert.InRange(Math.Abs(dy - datum.DisplayOffsetY), 0m, 0.000000001m);
        });
    }

    [Theory]
    [InlineData("EM")]
    [InlineData("BAND")]
    [InlineData("DATA")]
    public void LineNudgesRetainExistingRepresentativeWorkloadBudgets(string unit)
    {
        const int rowCount = 600;
        var sql = Script.Replace("SCALES (", "FACET (WRAP = Cohort, COLUMNS = 4), RESOLVE (X = INDEPENDENT, Y = INDEPENDENT), SCALES (", StringComparison.Ordinal)
            .Replace("INHERIT_ENCODINGS = OFF,", $"INHERIT_ENCODINGS = OFF, POSITION = NUDGE(X = 0.02, Y = -0.03, UNIT = {unit}),", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        data = data with
        {
            RowCount = rowCount,
            Columns = data.Columns.Select(column => column with
            { Values = Enumerable.Range(0, rowCount).Select(row => column.Name == "Cohort" ? ChartValue.From("P" + row / 50) : column.Values[row % 6]).ToImmutableArray(), DisplayValues = [] }).ToImmutableArray()
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

    [Theory]
    [InlineData("NULL_HANDLING = GAP", "NULL_HANDLING = CONNECT")]
    [InlineData("NULL_HANDLING = GAP", "NULL_HANDLING = ZERO")]
    [InlineData("INTERPOLATION = 'LINEAR'", "INTERPOLATION = 'MONOTONE'")]
    [InlineData("X = Distance", "COLOR = Cohort (TYPE = NOMINAL), X = Distance")]
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
        var invalid = spec with { Layers = [spec.Layers[0] with { Bindings = spec.Layers[0].Bindings.Add(new FieldBinding(FieldChannel.Color, "Cohort", DataSemanticKind.Nominal)) }] };
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedPointsAndRectangles_PreserveOneLineAndRectangleIntervals(bool emNudge)
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
        if (emNudge) spec = spec with { Layers = spec.Layers.SetItem(1, spec.Layers[1] with { Position = new PositionAdjustmentSpec(PositionAdjustmentKind.Nudge, 1m, -0.5m, Unit: PositionAdjustmentUnit.Em) }) };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Single(plan.Layers, layer => layer.Mark == MarkKind.Line);
        Assert.Equal(6, plan.Layers.Single(layer => layer.Mark == MarkKind.Line).Data.Length);
        Assert.Single(Paths(XDocument.Parse(new SvgChartRenderer().Render(plan))));
        var symbols = Elements(XDocument.Parse(new SvgChartRenderer().Render(plan)), "plot-line-symbol");
        var pointSymbols = Elements(XDocument.Parse(new SvgChartRenderer().Render(plan)), "plot-point");
        foreach (var symbol in symbols)
        {
            var row = (string?)symbol.Attribute("data-row-index");
            var observation = Assert.Single(pointSymbols, point => (string?)point.Attribute("data-row-index") == row);
            Assert.Equal(Read(observation, "cx") + (emNudge ? -6m : 0m), Read(symbol, "cx"));
            Assert.Equal(Read(observation, "cy") + (emNudge ? -12m : 0m), Read(symbol, "cy"));
        }
        var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 140).NormalizedText;
        Assert.Contains("X = 2 to 8; Y = 1 to 4", terminal);
        Assert.Contains("Braille", terminal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstantCoordinates_PreserveCoincidentRows(bool dataNudge)
    {
        var sql = Script.Replace("X = Distance (", "X = DATUM(3) (", StringComparison.Ordinal)
            .Replace("Y = Estimate (", "Y = DATUM(5) (", StringComparison.Ordinal);
        if (dataNudge) sql = sql.Replace("INHERIT_ENCODINGS = OFF,", "INHERIT_ENCODINGS = OFF, POSITION = NUDGE(X = 0.5, Y = -0.5, UNIT = DATA),", StringComparison.Ordinal);
        Assert.Equal(Parse(sql).ToSql(), Parse(Parse(sql).ToSql()).ToSql());
        var (spec, data) = Lower(sql);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
        var symbols = Elements(svg, "plot-line-symbol");
        Assert.Equal(6, symbols.Length);
        Assert.Single(Paths(svg));
        Assert.Single(symbols.Select(element => (Read(element, "cx"), Read(element, "cy"))).Distinct());
        Assert.Equal(6, plan.Fallback.Items.Length);
        Assert.All(symbols, symbol => Assert.Equal("5", symbol.Value));
    }

    [Theory]
    [InlineData("nullHandling", "CONNECT")]
    [InlineData("nullHandling", "ZERO")]
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

    [Theory]
    [MemberData(nameof(EmNudgeCases))]
    public void BandNudgeUsesEachFittedViewportAndPreservesGaps(bool reverse, bool logarithmic, bool facets, string missing)
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
        var nudged = Lower(sql.Replace("INHERIT_ENCODINGS = OFF,", "INHERIT_ENCODINGS = OFF, POSITION = NUDGE(X = 0.02, Y = -0.03, UNIT = BAND),", StringComparison.Ordinal)).Spec;
        var resolver = new PlotPlanResolver();
        var initial = resolver.Resolve(nudged, data);
        foreach (var bounds in new[] { initial.Bounds, new PlotBounds(0m, 0m, 1000m, 650m) })
        {
            var original = resolver.Resolve(spec, data, bounds);
            var shifted = resolver.Relayout(nudged, data, initial, bounds);
            Assert.Equal(original.Fallback.Items.ToArray(), shifted.Fallback.Items.ToArray());
            Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(nudged, data, bounds)), ChartContractSerializer.Serialize(shifted));
            Assert.Equal(ChartContractSerializer.Serialize(shifted), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(shifted))));
            var before = Elements(XDocument.Parse(new SvgChartRenderer().Render(original)), "plot-line-symbol");
            var document = XDocument.Parse(new SvgChartRenderer().Render(shifted));
            var after = Elements(document, "plot-line-symbol");
            Assert.Equal(missing == "none" ? 6 : 5, after.Length);
            for (var index = 0; index < shifted.Layers[0].Data.Length; index++)
            {
                var datum = shifted.Layers[0].Data[index];
                var frame = shifted.Facets.FirstOrDefault(facet => facet.RowIndices.Contains(datum.RowIndex))?.CartesianViewport ?? shifted.CartesianViewport!;
                var dx = -0.03m * (frame.Width - 80m);
                var dy = -0.02m * (frame.Height - 100m);
                Assert.Equal(dx, datum.DisplayOffsetX);
                Assert.Equal(dy, datum.DisplayOffsetY);
                Assert.Equal(original.Layers[0].Data[index].Channels.ToArray(), datum.Channels.ToArray());
                if (missing != "none" && datum.RowIndex == 2) continue;
                var raw = Assert.Single(before, point => (string?)point.Attribute("data-row-index") == datum.RowIndex.ToString(CultureInfo.InvariantCulture));
                var point = Assert.Single(after, point => (string?)point.Attribute("data-row-index") == datum.RowIndex.ToString(CultureInfo.InvariantCulture));
                Assert.InRange(Math.Abs(Read(point, "cx") - Read(raw, "cx") - dx), 0m, 0.002m);
                Assert.InRange(Math.Abs(Read(point, "cy") - Read(raw, "cy") - dy), 0m, 0.002m);
            }
            foreach (var path in Paths(document))
            {
                var coordinates = path.Attribute("d")!.Value.Split(' ');
                for (var index = 0; index < coordinates.Length; index += 3)
                    Assert.Contains(after, point => Read(point, "cx") == decimal.Parse(coordinates[index + 1], CultureInfo.InvariantCulture) && Read(point, "cy") == decimal.Parse(coordinates[index + 2], CultureInfo.InvariantCulture));
            }
            Assert.Equal(TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(original), 140).NormalizedText,
                TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(shifted), 140).NormalizedText);
        }
    }

    [Fact]
    public async Task BandNudgeAuthoringDesignerLineageAndPdfPreserveIntent()
    {
        var sql = Script.Replace("INHERIT_ENCODINGS = OFF,", "INHERIT_ENCODINGS = OFF, POSITION = NUDGE(X = -0.02, Y = 0.03, UNIT = BAND),", StringComparison.Ordinal);
        var statement = Parse(sql);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = sql + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Route)));";
        var design = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(design.Error);
        Assert.Equal(statement.ToSql(), Parse(new DesignerScriptPatcher().Patch(source, design.DesignState)).ToSql());
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(sql).Tokenize(), sql).Parse());
        foreach (var field in new[] { "Distance", "Estimate" })
            Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Route" && entry.SourceColumns.Contains(field));
        var (spec, data) = Lower(sql);
        data = data with { RowCount = 1, Columns = data.Columns.Select(column => column with { Values = [column.Values[0]], DisplayValues = [] }).ToImmutableArray() };
        spec = spec with { Theme = spec.Theme with { Tokens = [.. spec.Theme.Tokens, new StyleToken("DATA_LABELS", "ON")] } };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var report = new ReportManifest { Title = "Nudged", Source = "nudged.rptsql", Visuals = [new VisualManifest { Name = "Route", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = new SvgChartRenderer().Render(plan) }] };
        var document = XDocument.Parse(report.Visuals[0].NativeSvg!);
        var symbol = Assert.Single(Elements(document, "plot-line-symbol"));
        var label = Assert.Single(Elements(document, "plot-smart-label"));
        Assert.Equal("1", label.Value);
        Assert.InRange(Math.Abs(Read(label, "x") - Read(symbol, "cx")), 0m, 20m);
        Assert.InRange(Math.Abs(Read(label, "y") - Read(symbol, "cy")), 0m, 20m);
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await new PdfExporter().ExportAsync(report))[..4]);
    }

    [Fact]
    public void BandNudgePlanAndSvgHaveDeterministicFingerprints()
    {
        var (spec, data) = Lower(Script.Replace("INHERIT_ENCODINGS = OFF,", "INHERIT_ENCODINGS = OFF, POSITION = NUDGE(X = 0.02, Y = -0.03, UNIT = BAND),", StringComparison.Ordinal));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        Assert.True(planHash == "A68ACF370D5525C410AAA7DB264A0FBE64596CB8E085970054CD82996AF69504" && svgHash == "E53B43BD710BD8312618FD071E800B41DC5628B05227F38ABA744ABE4A6D42C3", $"Plan: {planHash}; SVG: {svgHash}");
    }

    [Theory]
    [MemberData(nameof(EmNudgeCases))]
    public void DataNudgeMapsEachVertexThroughOriginalScalesAndPreservesRawValues(bool reverse, bool logarithmic, bool facets, string missing)
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
        var nudged = Lower(sql.Replace("INHERIT_ENCODINGS = OFF,", "INHERIT_ENCODINGS = OFF, POSITION = NUDGE(X = 0.5, Y = -0.5, UNIT = DATA),", StringComparison.Ordinal)).Spec;
        var resolver = new PlotPlanResolver();
        var initial = resolver.Resolve(nudged, data);
        foreach (var bounds in new[] { initial.Bounds, new PlotBounds(0m, 0m, 1000m, 650m) })
        {
            var original = resolver.Resolve(spec, data, bounds);
            var shifted = resolver.Relayout(nudged, data, initial, bounds);
            Assert.Equal(original.Scales.SelectMany(scale => scale.Domain), shifted.Scales.SelectMany(scale => scale.Domain));
            Assert.Equal(original.Fallback.Items.ToArray(), shifted.Fallback.Items.ToArray());
            Assert.Equal(original.Layers[0].Data.SelectMany(datum => datum.Channels), shifted.Layers[0].Data.SelectMany(datum => datum.Channels));
            Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(nudged, data, bounds)), ChartContractSerializer.Serialize(shifted));
            Assert.Equal(ChartContractSerializer.Serialize(shifted), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(shifted))));
            var mapped = shifted with
            {
                Layers = shifted.Layers.Select(layer => layer with
                {
                    Data = layer.Data.Select(datum => datum with
                    {
                        DisplayOffsetX = 0m,
                        DisplayOffsetY = 0m,
                        Channels = datum.Channels.Select(channel => !datum.IsGap && channel.Channel is (FieldChannel.X or FieldChannel.Y)
                            ? channel with { Value = ChartValue.From(PlotPlanResolver.Number(channel.Value)!.Value + (channel.Channel == FieldChannel.X ? 0.5m : -0.5m)) } : channel).ToImmutableArray()
                    }).ToImmutableArray()
                }).ToImmutableArray()
            };
            var actualDocument = XDocument.Parse(new SvgChartRenderer().Render(shifted));
            var expectedDocument = XDocument.Parse(new SvgChartRenderer().Render(mapped));
            var actual = Elements(actualDocument, "plot-line-symbol");
            var expected = Elements(expectedDocument, "plot-line-symbol");
            var raw = Elements(XDocument.Parse(new SvgChartRenderer().Render(original)), "plot-line-symbol");
            Assert.Equal(missing == "none" ? 6 : 5, actual.Length);
            for (var index = 0; index < actual.Length; index++)
            {
                Assert.InRange(Math.Abs(Read(actual[index], "cx") - Read(expected[index], "cx")), 0m, 0.002m);
                Assert.InRange(Math.Abs(Read(actual[index], "cy") - Read(expected[index], "cy")), 0m, 0.002m);
                Assert.Equal(raw[index].Value, actual[index].Value);
            }
            var actualPaths = Paths(actualDocument);
            var expectedPaths = Paths(expectedDocument);
            Assert.Equal(expectedPaths.Length, actualPaths.Length);
            for (var path = 0; path < actualPaths.Length; path++)
            {
                var coordinates = actualPaths[path].Attribute("d")!.Value.Split(' ');
                var target = expectedPaths[path].Attribute("d")!.Value.Split(' ');
                Assert.Equal(target.Length, coordinates.Length);
                for (var token = 0; token < coordinates.Length; token++)
                    if (token % 3 == 0) Assert.Equal(target[token], coordinates[token]);
                    else Assert.InRange(Math.Abs(decimal.Parse(coordinates[token], CultureInfo.InvariantCulture) - decimal.Parse(target[token], CultureInfo.InvariantCulture)), 0m, 0.002m);
            }
            if (missing != "none")
            {
                var gap = shifted.Layers[0].Data[2];
                Assert.True(gap.IsGap);
                Assert.Equal(0m, gap.DisplayOffsetX);
                Assert.Equal(0m, gap.DisplayOffsetY);
            }
            Assert.Equal(TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(original), 140).NormalizedText,
                TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(shifted), 140).NormalizedText);
        }
    }

    [Theory]
    [InlineData("X")]
    [InlineData("Y")]
    public void DataNudgeRejectsNonPositiveLogarithmicTargets(string channel)
    {
        var sql = Script.Replace("LINEAR (CHANNEL", "LOGARITHMIC (CHANNEL", StringComparison.Ordinal).Replace("MIN = 0,", "INCLUDE_ZERO = OFF, MIN = 1,", StringComparison.Ordinal)
            .Replace("INHERIT_ENCODINGS = OFF,", $"INHERIT_ENCODINGS = OFF, POSITION = NUDGE(X = {(channel == "X" ? "-10" : "0")}, Y = {(channel == "Y" ? "-10" : "0")}, UNIT = DATA),", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        Assert.Contains(channel + " outside the positive logarithmic domain", Assert.Throws<InvalidOperationException>(() => new PlotPlanResolver().Resolve(spec, data)).Message);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("missing")]
    [InlineData("singleton")]
    public void DataNudgeHandlesEmptyAllMissingAndSingletonInputs(string scenario)
    {
        var (spec, data) = Lower(Script.Replace("INHERIT_ENCODINGS = OFF,", "INHERIT_ENCODINGS = OFF, POSITION = NUDGE(X = 0.5, Y = -0.5, UNIT = DATA),", StringComparison.Ordinal));
        data = scenario == "missing" ? data with
        {
            Columns = data.Columns.Select(column => column.Name == "Distance" ? column with
            { Values = column.Values.Select(_ => ChartValue.Null()).ToImmutableArray(), DisplayValues = [] } : column).ToImmutableArray()
        }
            : data with
            {
                RowCount = scenario == "empty" ? 0 : 1,
                Columns = data.Columns.Select(column => column with
                { Values = column.Values.Take(scenario == "empty" ? 0 : 1).ToImmutableArray(), DisplayValues = [] }).ToImmutableArray()
            };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
        Assert.Empty(Paths(svg));
        Assert.Equal(scenario == "singleton" ? 1 : 0, Elements(svg, "plot-line-symbol").Length);
        if (scenario == "missing") Assert.All(plan.Layers[0].Data, datum =>
        {
            Assert.True(datum.IsGap);
            Assert.Equal(0m, datum.DisplayOffsetX);
            Assert.Equal(0m, datum.DisplayOffsetY);
        });
    }

    [Fact]
    public async Task DataNudgeAuthoringDesignerLineageAndPdfPreserveIntent()
    {
        var sql = Script.Replace("INHERIT_ENCODINGS = OFF,", "INHERIT_ENCODINGS = OFF, POSITION = NUDGE(X = -0.5, Y = 0.5, UNIT = DATA),", StringComparison.Ordinal);
        var statement = Parse(sql);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = sql + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Route)));";
        var design = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(design.Error);
        Assert.Equal(statement.ToSql(), Parse(new DesignerScriptPatcher().Patch(source, design.DesignState)).ToSql());
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(sql).Tokenize(), sql).Parse());
        foreach (var field in new[] { "Distance", "Estimate" }) Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Route" && entry.SourceColumns.Contains(field));
        var (spec, data) = Lower(sql);
        data = data with { RowCount = 1, Columns = data.Columns.Select(column => column with { Values = [column.Values[0]], DisplayValues = [] }).ToImmutableArray() };
        spec = spec with { Theme = spec.Theme with { Tokens = [.. spec.Theme.Tokens, new StyleToken("DATA_LABELS", "ON")] } };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var report = new ReportManifest { Title = "Nudged", Source = "nudged.rptsql", Visuals = [new VisualManifest { Name = "Route", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = new SvgChartRenderer().Render(plan) }] };
        var document = XDocument.Parse(report.Visuals[0].NativeSvg!);
        var symbol = Assert.Single(Elements(document, "plot-line-symbol"));
        var label = Assert.Single(Elements(document, "plot-smart-label"));
        Assert.Equal("1", label.Value);
        Assert.InRange(Math.Abs(Read(label, "x") - Read(symbol, "cx")), 0m, 20m);
        Assert.InRange(Math.Abs(Read(label, "y") - Read(symbol, "cy")), 0m, 20m);
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await new PdfExporter().ExportAsync(report))[..4]);
    }

    [Fact]
    public void DataNudgePlanAndSvgHaveDeterministicFingerprints()
    {
        var (spec, data) = Lower(Script.Replace("INHERIT_ENCODINGS = OFF,", "INHERIT_ENCODINGS = OFF, POSITION = NUDGE(X = 0.5, Y = -0.5, UNIT = DATA),", StringComparison.Ordinal));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        Assert.True(planHash == "86B7691CA2049B5C46B21C549904682F2A17DB9D64F9D04CCA22F69AFA09ABAC" && svgHash == "8B499900928C8578FB8332D50E97D6B34AF362C3773FABC9C9845743E09699FF", $"Plan: {planHash}; SVG: {svgHash}");
    }

    [Theory]
    [InlineData("LEFT", false)]
    [InlineData("RIGHT", false)]
    [InlineData("BOTTOM", false)]
    [InlineData("LEFT", true)]
    [InlineData("RIGHT", true)]
    [InlineData("BOTTOM", true)]
    public void DataNudgeUsesFinalSideLegendPlotAreaAndKeepsPointGroupsIndependent(string legend, bool logarithmic)
    {
        var sql = Script.Replace("INHERIT_ENCODINGS = OFF,", "INHERIT_ENCODINGS = OFF, POSITION = NUDGE(X = 0.5, Y = -0.5, UNIT = DATA),", StringComparison.Ordinal);
        if (logarithmic) sql = sql.Replace("LINEAR (CHANNEL", "LOGARITHMIC (CHANNEL", StringComparison.Ordinal).Replace("MIN = 0,", "INCLUDE_ZERO = OFF, MIN = 1,", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var point = spec.Layers[0] with
        {
            Id = "observations",
            Mark = MarkKind.Point,
            ZIndex = 1,
            Position = null,
            Style = [],
            Bindings = spec.Layers[0].Bindings.Add(new FieldBinding(FieldChannel.Color, "Cohort", DataSemanticKind.Nominal))
        };
        spec = spec with
        {
            Bindings = spec.Bindings.Add(point.Bindings[^1]),
            Layers = [spec.Layers[0], point],
            Theme = spec.Theme with { Tokens = spec.Theme.Tokens.Add(new("LEGEND_POSITION", legend)) }
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Single(plan.Layers, layer => layer.Mark == MarkKind.Line);
        Assert.True(plan.Legend.Length > 1);
        var target = plan with
        {
            Layers = plan.Layers.Select(layer => layer.Mark != MarkKind.Line ? layer : layer with
            {
                Data = layer.Data.Select(datum => datum with
                {
                    DisplayOffsetX = 0m,
                    DisplayOffsetY = 0m,
                    Channels = datum.Channels.Select(channel => channel.Channel is FieldChannel.X or FieldChannel.Y ? channel with
                    { Value = ChartValue.From(PlotPlanResolver.Number(channel.Value)!.Value + (channel.Channel == FieldChannel.X ? 0.5m : -0.5m)) } : channel).ToImmutableArray()
                }).ToImmutableArray()
            }).ToImmutableArray()
        };
        var actual = Elements(XDocument.Parse(new SvgChartRenderer().Render(plan)), "plot-line-symbol");
        var expected = Elements(XDocument.Parse(new SvgChartRenderer().Render(target)), "plot-line-symbol");
        Assert.Equal(6, actual.Length);
        for (var index = 0; index < actual.Length; index++)
        {
            Assert.InRange(Math.Abs(Read(actual[index], "cx") - Read(expected[index], "cx")), 0m, 0.002m);
            Assert.InRange(Math.Abs(Read(actual[index], "cy") - Read(expected[index], "cy")), 0m, 0.002m);
        }
        Assert.All(plan.Layers.Where(layer => layer.Mark == MarkKind.Point).SelectMany(layer => layer.Data), datum =>
        {
            Assert.Equal(0m, datum.DisplayOffsetX);
            Assert.Equal(0m, datum.DisplayOffsetY);
        });
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
