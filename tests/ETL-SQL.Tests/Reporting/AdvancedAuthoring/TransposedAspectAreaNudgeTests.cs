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

public sealed class TransposedAspectAreaNudgeTests
{
    private static string Script(bool ribbon, string unit) => $$"""
        CREATE VISUAL Envelope AS CUSTOM (SOURCE = #prepared, CHART (
          COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
          SCALES (unusedX = BAND (CHANNEL = X), unusedY = LINEAR (CHANNEL = Y, MAX = 1000),
            distances = LINEAR (CHANNEL = X, INCLUDE_ZERO = OFF, MIN = 1, MAX = 10),
            estimates = LINEAR (CHANNEL = Y, INCLUDE_ZERO = OFF, MIN = 1, MAX = 10)),
          LAYERS (envelope = AREA (
            INHERIT_ENCODINGS = OFF, NULL_HANDLING = GAP,
            {{(ribbon ? "" : "AREA_BASELINE = ZERO,")}}
            POSITION = NUDGE(X = 0.02, Y = -0.03, UNIT = {{unit}}),
            ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = distances),
              {{(ribbon ? "Y_START = LowerBound (TYPE = QUANTITATIVE, SCALE = estimates), Y_END = UpperBound" : "Y = UpperBound")}} (TYPE = QUANTITATIVE, SCALE = estimates)),
            STYLE (INTERPOLATION = 'LINEAR', COLOR = '#112233')
          ))
        ));
        """;

    public static IEnumerable<object[]> DataGeometryCases() => GeometryCases().Where(row => (string)row[0] == "EM")
        .Select(row => new object[] { row[1], row[2], row[3], row[4], row[5] });

    [Theory]
    [MemberData(nameof(DataGeometryCases))]
    public void DataNudgeMapsAnchorsAndPreservesCrossSectionSpans(bool ribbon, bool reverse, bool logarithmic, bool facets, string missing)
    {
        var sql = Script(ribbon, "DATA");
        if (reverse) sql = sql.Replace("INCLUDE_ZERO = OFF, MIN", "REVERSE = ON, INCLUDE_ZERO = OFF, MIN", StringComparison.Ordinal);
        if (logarithmic)
        {
            sql = sql.Replace("distances = LINEAR", "distances = LOGARITHMIC", StringComparison.Ordinal);
            if (ribbon) sql = sql.Replace("estimates = LINEAR", "estimates = LOGARITHMIC", StringComparison.Ordinal);
        }
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
            AssertMappedCrossSections(plan, baseline, ribbon);
            Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, bounds)), ChartContractSerializer.Serialize(plan));
            Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
        }
    }

    [Theory]
    [InlineData(false, "LEFT")]
    [InlineData(true, "LEFT")]
    [InlineData(false, "RIGHT")]
    [InlineData(true, "RIGHT")]
    [InlineData(false, "BOTTOM")]
    [InlineData(true, "BOTTOM")]
    public void DataNudgeUsesFinalPlotAreaWithIsolatedColorLegends(bool ribbon, string legend)
    {
        var sql = Script(ribbon, "DATA").Replace("distances = LINEAR", "distances = LOGARITHMIC", StringComparison.Ordinal);
        if (ribbon) sql = sql.Replace("estimates = LINEAR", "estimates = LOGARITHMIC", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var point = new MarkLayerSpec("observations", MarkKind.Point, 2,
            [new FieldBinding(FieldChannel.X, "Distance", DataSemanticKind.Quantitative, ScaleId: "distances"),
             new FieldBinding(FieldChannel.Y, "UpperBound", DataSemanticKind.Quantitative, ScaleId: "estimates"),
             new FieldBinding(FieldChannel.Color, "Cohort", DataSemanticKind.Nominal)], []);
        spec = spec with
        {
            Layers = spec.Layers.Add(point),
            Bindings = spec.Bindings.Add(point.Bindings[^1]),
            Theme = spec.Theme with { Tokens = [.. spec.Theme.Tokens, new StyleToken("LEGEND_POSITION", legend)] }
        };
        var resolver = new PlotPlanResolver();
        foreach (var bounds in new[] { new PlotBounds(0m, 0m, 800m, 600m), new PlotBounds(0m, 0m, 1000m, 650m) })
        {
            var plan = resolver.Resolve(spec, data, bounds);
            var baseline = resolver.Resolve(spec with { Layers = spec.Layers.SetItem(0, spec.Layers[0] with { Position = null }) }, data, bounds);
            Assert.True(plan.Legend.Length > 1);
            AssertMappedCrossSections(plan, baseline, ribbon);
            Assert.All(plan.Layers.Where(layer => layer.Mark == MarkKind.Point).SelectMany(layer => layer.Data), datum =>
            {
                Assert.Equal(0m, datum.DisplayOffsetX);
                Assert.Equal(0m, datum.DisplayOffsetY);
            });
        }
    }

    [Theory]
    [InlineData(false, "X")]
    [InlineData(true, "X")]
    [InlineData(true, "Y")]
    public void DataNudgeRejectsNonpositiveLogarithmicAnchorTargets(bool ribbon, string axis)
    {
        var sql = Script(ribbon, "DATA").Replace(axis == "X" ? "distances = LINEAR" : "estimates = LINEAR", axis == "X" ? "distances = LOGARITHMIC" : "estimates = LOGARITHMIC", StringComparison.Ordinal)
            .Replace(axis == "X" ? "X = 0.02" : "Y = -0.03", axis == "X" ? "X = -8" : "Y = -8", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        Assert.Contains("positive logarithmic domain", Assert.Throws<InvalidOperationException>(() => new PlotPlanResolver().Resolve(spec, data)).Message);
    }

    public static IEnumerable<object[]> GeometryCases()
    {
        foreach (var unit in new[] { "EM", "BAND" })
            foreach (var ribbon in new[] { false, true })
                foreach (var reverse in new[] { false, true })
                    foreach (var logarithmic in new[] { false, true })
                        foreach (var facets in new[] { false, true })
                            foreach (var missing in ribbon ? new[] { "none", "Distance", "LowerBound", "UpperBound" } : new[] { "none", "Distance", "UpperBound" })
                                yield return [unit, ribbon, reverse, logarithmic, facets, missing];
    }

    [Theory]
    [MemberData(nameof(GeometryCases))]
    public void CompletePolygonsTranslateThroughAxesFacetsResizeAndGaps(string unit, bool ribbon, bool reverse, bool logarithmic, bool facets, string missing)
    {
        var sql = Script(ribbon, unit);
        if (reverse) sql = sql.Replace("INCLUDE_ZERO = OFF, MIN", "REVERSE = ON, INCLUDE_ZERO = OFF, MIN", StringComparison.Ordinal);
        if (logarithmic)
        {
            sql = sql.Replace("distances = LINEAR", "distances = LOGARITHMIC", StringComparison.Ordinal);
            if (ribbon) sql = sql.Replace("estimates = LINEAR", "estimates = LOGARITHMIC", StringComparison.Ordinal);
        }
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
            Assert.Equal(5, spec.Version);
            Assert.Equal(8, plan.Version);
            Assert.Equal(new ResolvedCartesianAxes("distances", "estimates"), plan.CartesianAxes);
            Assert.Equal(baseline.Fallback.Items.ToArray(), plan.Fallback.Items.ToArray());
            Assert.Equal(baseline.Scales.SelectMany(scale => scale.Domain), plan.Scales.SelectMany(scale => scale.Domain));
            Assert.Equal(baseline.Layers[0].Data.SelectMany(datum => datum.Channels), plan.Layers[0].Data.SelectMany(datum => datum.Channels));
            var actual = Paths(plan);
            var before = Paths(baseline);
            Assert.Equal(facets || missing != "none" ? 2 : 1, actual.Length);
            Assert.Equal(before.Length, actual.Length);
            for (var panel = 0; panel < actual.Length; panel++)
            {
                var viewport = facets ? plan.Facets[panel].CartesianViewport! : plan.CartesianViewport!;
                var dx = -0.03m * (unit == "EM" ? 12m : viewport.Width - 80m);
                var dy = -0.02m * (unit == "EM" ? 12m : viewport.Height - 100m);
                var originalVertices = Vertices(before[panel]);
                var translatedVertices = Vertices(actual[panel]);
                Assert.Equal(originalVertices.Length, translatedVertices.Length);
                for (var index = 0; index < originalVertices.Length; index++)
                {
                    Assert.InRange(Math.Abs(translatedVertices[index].X - originalVertices[index].X - dx), 0m, 0.002m);
                    Assert.InRange(Math.Abs(translatedVertices[index].Y - originalVertices[index].Y - dy), 0m, 0.002m);
                }
                Assert.Equal(before[panel].Value, actual[panel].Value);
            }
            foreach (var datum in plan.Layers[0].Data)
            {
                var viewport = facets ? Assert.Single(plan.Facets, panel => panel.RowIndices.Contains(datum.RowIndex)).CartesianViewport! : plan.CartesianViewport!;
                Assert.Equal(-0.03m * (unit == "EM" ? 12m : viewport.Width - 80m), datum.DisplayOffsetX);
                Assert.Equal(-0.02m * (unit == "EM" ? 12m : viewport.Height - 100m), datum.DisplayOffsetY);
            }
            Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, bounds)), ChartContractSerializer.Serialize(plan));
            Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
            Assert.Equal(TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(baseline), 140).NormalizedText,
                TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 140).NormalizedText);
        }
    }

    [Theory]
    [InlineData(false, "EM")]
    [InlineData(true, "EM")]
    [InlineData(false, "BAND")]
    [InlineData(true, "BAND")]
    [InlineData(false, "DATA")]
    [InlineData(true, "DATA")]
    public void ConditionedLinesAndOtherMarkLayersRetainSeparatePlacement(bool ribbon, string unit)
    {
        var sql = Script(ribbon, unit).Replace("LAYERS (envelope = AREA", """
            LAYERS (route = LINE (
              INHERIT_ENCODINGS = OFF, NULL_HANDLING = GAP,
              ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = distances), Y = UpperBound (TYPE = QUANTITATIVE, SCALE = estimates)),
              STYLE (INTERPOLATION = 'LINEAR'), CONDITIONS (COLOR WHEN UpperBound >= 5 THEN '#ff0000')
            ), envelope = AREA
            """, StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var area = spec.Layers.Single(layer => layer.Mark == MarkKind.Area);
        var point = new MarkLayerSpec("observations", MarkKind.Point, 2,
            [new FieldBinding(FieldChannel.X, "Distance", DataSemanticKind.Quantitative, ScaleId: "distances"),
             new FieldBinding(FieldChannel.Y, "UpperBound", DataSemanticKind.Quantitative, ScaleId: "estimates")], []);
        spec = spec with { Layers = spec.Layers.Add(point) };
        var resolver = new PlotPlanResolver();
        var plan = resolver.Resolve(spec, data);
        var baseline = resolver.Resolve(spec with { Layers = spec.Layers.Select(layer => layer.Id == area.Id ? layer with { Position = null } : layer).ToImmutableArray() }, data);
        Assert.Equal(11, plan.Version);
        Assert.Single(Paths(plan));
        var before = XDocument.Parse(new SvgChartRenderer().Render(baseline));
        var after = XDocument.Parse(new SvgChartRenderer().Render(plan));
        foreach (var name in new[] { "plot-point", "plot-conditional-line" })
            Assert.Equal(before.Descendants().Where(element => (string?)element.Attribute("class") == name).Select(element => element.ToString()),
                after.Descendants().Where(element => (string?)element.Attribute("class") == name).Select(element => element.ToString()));
        Assert.All(plan.Layers.Where(layer => layer.Mark != MarkKind.Area).SelectMany(layer => layer.Data), datum =>
        {
            Assert.Equal(0m, datum.DisplayOffsetX);
            Assert.Equal(0m, datum.DisplayOffsetY);
        });
        Assert.Equal(baseline.Fallback.Items.ToArray(), plan.Fallback.Items.ToArray());
        Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
    }

    [Theory]
    [InlineData(false, "EM")]
    [InlineData(true, "EM")]
    [InlineData(false, "BAND")]
    [InlineData(true, "BAND")]
    [InlineData(false, "DATA")]
    [InlineData(true, "DATA")]
    public void ConstantCoincidentCrossSectionsRetainAuthoredEndpoints(bool ribbon, string unit)
    {
        var sql = Script(ribbon, unit).Replace("X = Distance", "X = DATUM(5)", StringComparison.Ordinal)
            .Replace("Y_START = LowerBound", "Y_START = DATUM(5)", StringComparison.Ordinal)
            .Replace("Y_END = UpperBound", "Y_END = DATUM(5)", StringComparison.Ordinal)
            .Replace("Y = UpperBound", "Y = DATUM(5)", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var points = Vertices(Assert.Single(Paths(plan)));
        Assert.Equal(12, points.Length);
        Assert.Equal(ribbon ? 1 : 2, points.Distinct().Count());
        Assert.Equal(6, plan.Fallback.Items.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtraColorEncodingRemainsRejectedInAuthoringSpecsAndPlans(bool ribbon)
    {
        var (spec, data) = Lower(Script(ribbon, "EM"));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var sql = Script(ribbon, "EM").Replace("X = Distance", "COLOR = Cohort (TYPE = NOMINAL), X = Distance", StringComparison.Ordinal);
        Assert.Contains(AdvancedChartSemanticValidator.Validate(Parse(sql)), diagnostic => diagnostic.Message.Contains("ASPECT_RATIO AREA", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>((spec with { Layers = [spec.Layers[0] with { Bindings = spec.Layers[0].Bindings.Add(new FieldBinding(FieldChannel.Color, "Cohort", DataSemanticKind.Nominal)) }] }).Validate);
        Assert.Throws<InvalidDataException>((plan with { Layers = [plan.Layers[0] with { Data = plan.Layers[0].Data.Select(datum => datum with { Encodings = [new ResolvedEncodingValue(ConditionalEncodingChannel.Color, ChartValue.From("#ff0000"))] }).ToImmutableArray() }] }).Validate);
    }

    [Theory]
    [InlineData(false, "EM")]
    [InlineData(true, "EM")]
    [InlineData(false, "BAND")]
    [InlineData(true, "BAND")]
    [InlineData(false, "DATA")]
    [InlineData(true, "DATA")]
    public async Task AuthoringDesignerLineageAndPdfPreserveRawEndpoints(bool ribbon, string unit)
    {
        var sql = Script(ribbon, unit);
        var statement = Parse(sql);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = sql + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Envelope)));";
        var design = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(design.Error);
        Assert.Equal(statement.ToSql(), Parse(new DesignerScriptPatcher().Patch(source, design.DesignState)).ToSql());
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(sql).Tokenize(), sql).Parse());
        foreach (var field in ribbon ? new[] { "Distance", "LowerBound", "UpperBound" } : new[] { "Distance", "UpperBound" })
            Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Envelope" && entry.SourceColumns.Contains(field));
        var (spec, data) = Lower(sql);
        Assert.Equal(ChartContractSerializer.Serialize(spec), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(ChartContractSerializer.Serialize(spec))));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var svg = new SvgChartRenderer().Render(plan);
        var report = new ReportManifest { Title = "Nudged area", Source = "nudged-area.rptsql", Visuals = [new VisualManifest { Name = "Envelope", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = svg }] };
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await new PdfExporter().ExportAsync(report))[..4]);
    }

    [Theory]
    [InlineData(false, "EM", 0)]
    [InlineData(true, "EM", 1)]
    [InlineData(false, "BAND", 2)]
    [InlineData(true, "BAND", 2)]
    [InlineData(false, "DATA", 0)]
    [InlineData(true, "DATA", 0)]
    [InlineData(false, "DATA", 1)]
    [InlineData(true, "DATA", 1)]
    [InlineData(false, "DATA", 2)]
    [InlineData(true, "DATA", 2)]
    public void EmptySingletonAndAllMissingInputsProduceNoPolygon(bool ribbon, string unit, int profile)
    {
        var (spec, data) = Lower(Script(ribbon, unit));
        data = data with
        {
            RowCount = profile == 0 ? 0 : profile == 1 ? 1 : data.RowCount,
            Columns = data.Columns.Select(column => column with
            { Values = profile == 0 ? [] : profile == 1 ? [column.Values[0]] : column.Name == "Distance" ? column.Values.Select(_ => ChartValue.Null()).ToImmutableArray() : column.Values, DisplayValues = [] }).ToImmutableArray()
        };
        Assert.Empty(Paths(new PlotPlanResolver().Resolve(spec, data)));
    }

    [Theory]
    [InlineData(false, "EM")]
    [InlineData(true, "EM")]
    [InlineData(false, "BAND")]
    [InlineData(true, "BAND")]
    [InlineData(false, "DATA")]
    [InlineData(true, "DATA")]
    public void RepresentativeWorkloadRetainsExistingBudgets(bool ribbon, string unit)
    {
        const int rowCount = 600;
        var (spec, data) = Lower(Script(ribbon, unit).Replace("SCALES (", "FACET (WRAP = Cohort, COLUMNS = 4), RESOLVE (X = INDEPENDENT, Y = INDEPENDENT), SCALES (", StringComparison.Ordinal));
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
        Assert.Equal(12, Paths(plan).Length);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "AREA SVG rendering exceeded 5 seconds."); // flaky-time-bound-ok: 5 seconds bounds a 600-row, 12-panel native workload with ample headroom.
        Assert.True(resolution < TimeSpan.FromSeconds(5), "AREA resolution exceeded 5 seconds."); // flaky-time-bound-ok: 5 seconds bounds a 600-row, 12-panel native workload with ample headroom.
        Assert.True(allocation < 16L * 1024 * 1024, $"AREA resolution allocated {allocation:N0} bytes.");
        Assert.InRange(Encoding.UTF8.GetByteCount(ChartContractSerializer.Serialize(plan)), 1, 6 * 1024 * 1024);
        Assert.InRange(Encoding.UTF8.GetByteCount(svg), 1, 600 * 1024);
    }

    [Theory]
    [InlineData(false, "EM", "1DFE7DE1E9298278886DA4264F17167D03B286563DB21D328D7691D968770D91", "42D1C9D8A3F49372ED4DB5069D6C10ED2D57A6904BF4BBDF55B3BEE64B605F22")]
    [InlineData(true, "EM", "873003EB3741E602EE36DA30A72FF5E2F11C20992688AA7FC0AE2B1D3FF48DA2", "5DE565E8F3BDA96490C70C929536DF56031353DF8D743CD4B9907B663A512966")]
    [InlineData(false, "BAND", "5C79CD63D0A5B8AF26DD34AFE6FC160B35A3A363E9F8B029A3347600F054F5F5", "5A247BF2F1805EB6695DB357B248A75EE39FBABB17E7392AA5C65514F3B07AE7")]
    [InlineData(true, "BAND", "87B43459FE1FB18EF7A2A27ED6718CCE492290D0E3E6F97E527FCB52FB220C01", "1F378448DC17640F84AA9914DAC46076F1DFDF465F27A772B43A353E67A90C95")]
    [InlineData(false, "DATA", "F66E643006A7C030C2C32C60109E0985385297017AA056B2498D9E821FAEAD0D", "DEAFCB151F8EA268C5510B3E7759D8472F937D8D1E0491E17FE44B40C7304916")]
    [InlineData(true, "DATA", "B7703BD251F0FB960497F1E7ADC385AC22F5A1AC1DB88754C9908C64C3019431", "9D887583919606ABDCC64859E87AA27187353F77A72F2F374789C419AE518263")]
    public void PlanAndSvgHaveDeterministicFingerprints(bool ribbon, string unit, string expectedPlan, string expectedSvg)
    {
        var (spec, data) = Lower(Script(ribbon, unit));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        Assert.True(planHash == expectedPlan && svgHash == expectedSvg, $"Plan: {planHash}; SVG: {svgHash}");
    }

    [Theory]
    [InlineData("EM")]
    [InlineData("BAND")]
    [InlineData("DATA")]
    public void PublishedExamplesParseLowerAndResolve(string unit)
    {
        var path = Path.Combine(TerminalSnapshotHarness.GetRepoRoot(), "docs", "reference", "visuals-reporting", "visuals", "chart.md");
        var example = Regex.Matches(File.ReadAllText(path), @"```sql\s*(.*?)```", RegexOptions.Singleline)
            .Select(match => match.Groups[1].Value.Trim()).Single(block => block.StartsWith("CREATE VISUAL " + (unit == "EM" ? "NudgedArea" : unit == "BAND" ? "NudgedRibbon" : "DataRibbon") + " AS CUSTOM", StringComparison.Ordinal));
        var (spec, data) = Lower(example);
        Assert.Equal(Parse(example).ToSql(), Parse(Parse(example).ToSql()).ToSql());
        Assert.Equal(unit == "EM" ? PositionAdjustmentUnit.Em : unit == "BAND" ? PositionAdjustmentUnit.Band : PositionAdjustmentUnit.Data, spec.Layers[0].Position!.Unit);
        Assert.Single(Paths(new PlotPlanResolver().Resolve(spec, data)));
    }

    private static XElement[] Paths(PlotPlan plan) => XDocument.Parse(new SvgChartRenderer().Render(plan)).Descendants().Where(element => (string?)element.Attribute("class") is "plot-area" or "plot-ribbon").ToArray();
    private static (decimal X, decimal Y)[] Vertices(XElement path) => path.Attribute("d")!.Value.Split(' ').SkipLast(1).Chunk(3).Select(tokens =>
        (decimal.Parse(tokens[1], CultureInfo.InvariantCulture), decimal.Parse(tokens[2], CultureInfo.InvariantCulture))).ToArray();
    private static void AssertMappedCrossSections(PlotPlan plan, PlotPlan baseline, bool ribbon)
    {
        var layer = plan.Layers.Single(layer => layer.Mark == MarkKind.Area);
        var original = baseline.Layers.Single(layer => layer.Mark == MarkKind.Area);
        Assert.Equal(original.Data.SelectMany(datum => datum.Channels), layer.Data.SelectMany(datum => datum.Channels));
        Assert.Equal(baseline.Scales.SelectMany(scale => scale.Domain), plan.Scales.SelectMany(scale => scale.Domain));
        Assert.Equal(baseline.Fallback.Items.ToArray(), plan.Fallback.Items.ToArray());
        var upper = OraclePoints(baseline, ribbon ? FieldChannel.YEnd : FieldChannel.Y);
        var lower = OraclePoints(baseline, ribbon ? FieldChannel.YStart : null);
        var anchor = ribbon ? lower : upper;
        var target = OraclePoints(baseline, ribbon ? FieldChannel.YStart : FieldChannel.Y, 0.02m, -0.03m);
        var before = Paths(baseline);
        var after = Paths(plan);
        Assert.Equal(before.Length, after.Length);
        for (var run = 0; run < after.Length; run++)
        {
            Assert.Equal(before[run].Value, after[run].Value);
            var rows = Regex.Matches(after[run].Value, @"Row (\d+):").Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) - 1).ToArray();
            var vertices = Vertices(after[run]);
            Assert.Equal(rows.Length * 2, vertices.Length);
            for (var index = 0; index < rows.Length; index++)
            {
                var row = rows[index];
                var dx = target[row].X - anchor[row].X;
                var dy = target[row].Y - anchor[row].Y;
                Assert.InRange(Math.Abs(vertices[index].X - upper[row].X - dx), 0m, 0.003m);
                Assert.InRange(Math.Abs(vertices[index].Y - upper[row].Y - dy), 0m, 0.003m);
                Assert.InRange(Math.Abs(vertices[^(index + 1)].X - lower[row].X - dx), 0m, 0.003m);
                Assert.InRange(Math.Abs(vertices[^(index + 1)].Y - lower[row].Y - dy), 0m, 0.003m);
                var datum = Assert.Single(layer.Data, datum => datum.RowIndex == row);
                Assert.InRange(Math.Abs(datum.DisplayOffsetX - dx), 0m, 0.002m);
                Assert.InRange(Math.Abs(datum.DisplayOffsetY - dy), 0m, 0.002m);
            }
        }
        Assert.All(layer.Data.Where(datum => datum.IsGap), datum =>
        {
            Assert.Equal(0m, datum.DisplayOffsetX);
            Assert.Equal(0m, datum.DisplayOffsetY);
        });
        Assert.Equal(TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(baseline), 140).NormalizedText,
            TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 140).NormalizedText);
    }

    private static Dictionary<int, (decimal X, decimal Y)> OraclePoints(PlotPlan plan, FieldChannel? bound, decimal dx = 0m, decimal dy = 0m)
    {
        var area = plan.Layers.Single(layer => layer.Mark == MarkKind.Area);
        var points = area with
        {
            Id = "oracle",
            Mark = MarkKind.Point,
            ZIndex = plan.Layers.Max(layer => layer.ZIndex) + 1,
            AreaRibbon = false,
            AreaRibbonScaleId = null,
            Position = null,
            Style = [],
            Data = area.Data.Select(datum => datum with
            {
                DisplayOffsetX = 0m,
                DisplayOffsetY = 0m,
                Channels = [new ResolvedChannelValue(FieldChannel.X, datum.IsGap ? ChartValue.Null() : ChartValue.From(PlotPlanResolver.Number(datum.Channels.Single(channel => channel.Channel == FieldChannel.X).Value)!.Value + dx), null),
                    new ResolvedChannelValue(FieldChannel.Y, datum.IsGap ? ChartValue.Null() : ChartValue.From((bound is null ? 0m : PlotPlanResolver.Number(datum.Channels.Single(channel => channel.Channel == bound).Value)!.Value) + dy), null)]
            }).ToImmutableArray()
        };
        var oracle = plan with { Layers = plan.Layers.Select(layer => layer.Id == area.Id ? layer with { Data = [] } : layer).ToImmutableArray().Add(points) };
        return XDocument.Parse(new SvgChartRenderer().Render(oracle)).Descendants().Where(element => (string?)element.Attribute("class") == "plot-point" &&
            element.Ancestors().Any(parent => (string?)parent.Attribute("data-layer") == "oracle"))
            .ToDictionary(element => int.Parse(element.Attribute("data-row-index")!.Value, CultureInfo.InvariantCulture),
                element => (decimal.Parse(element.Attribute("cx")!.Value, CultureInfo.InvariantCulture), decimal.Parse(element.Attribute("cy")!.Value, CultureInfo.InvariantCulture)));
    }

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
            Name = "Envelope",
            Columns = ["Distance", "LowerBound", "UpperBound", "Cohort"],
            Rows = [["8", "1", "4", "A"], ["2", "7", "2", "A"], ["5", "3", "9", "A"], ["1", "8", "3", "B"], ["9", "2", "6", "B"], ["4", "6", "1", "B"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
