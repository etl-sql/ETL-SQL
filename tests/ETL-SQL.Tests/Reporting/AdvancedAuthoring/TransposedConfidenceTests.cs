using System.Collections.Immutable;
using System.Diagnostics;
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

public sealed class TransposedConfidenceTests
{
    private static string Sql(bool confidence, string mode, string position, bool log, bool reverse, bool facets) => $$"""
        CREATE VISUAL Envelope AS CUSTOM (SOURCE = #prepared, CHART (
          COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
          {{(facets ? "FACET (WRAP = Cohort, COLUMNS = 2), RESOLVE (X = INDEPENDENT, Y = INDEPENDENT)," : "")}}
          SCALES (unusedX = BAND (CHANNEL = X), unusedY = LINEAR (CHANNEL = Y, MAX = 1000),
            distances = {{(log ? "LOGARITHMIC" : "LINEAR")}} (CHANNEL = X, INCLUDE_ZERO = OFF, MIN = 1, MAX = 20, REVERSE = {{(reverse ? "ON" : "OFF")}}),
            estimates = {{(log ? "LOGARITHMIC" : "LINEAR")}} (CHANNEL = Y, INCLUDE_ZERO = OFF, MIN = 1, MAX = 20, REVERSE = {{(reverse ? "ON" : "OFF")}})),
          LAYERS (envelope = AREA (NULL_HANDLING = GAP, POSITION = {{position}},
            ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = distances),
              {{(confidence ? "CONFIDENCE_LOW" : "Y_START")}} = LowerBound (TYPE = QUANTITATIVE, SCALE = estimates),
              {{(confidence ? "CONFIDENCE_HIGH" : "Y_END")}} = UpperBound (TYPE = QUANTITATIVE, SCALE = estimates)),
            STYLE (INTERPOLATION = '{{mode}}', COLOR = '#112233')))
        ));
        """;

    public static IEnumerable<object[]> Cases()
    {
        foreach (var mode in new[] { "LINEAR", "SMOOTH", "STEP_BEFORE", "STEP_AFTER" })
            foreach (var position in new[] { "IDENTITY", "NUDGE(X = 0.1, Y = 0.2, UNIT = EM)", "NUDGE(X = 0.1, Y = 0.2, UNIT = BAND)",
                "NUDGE(X = 0.1, Y = 0.2, UNIT = DATA)", "JITTER(X = 0.02, Y = 0.03, KEY = Id, SEED = 42)" })
                foreach (var log in new[] { false, true })
                    foreach (var reverse in new[] { false, true })
                        foreach (var facets in new[] { false, true }) yield return [mode, position, log, reverse, facets];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ConfidenceBoundsRetainRibbonGeometryAndRawChannelIdentity(string mode, string position, bool log, bool reverse, bool facets)
    {
        var (spec, data) = Lower(Sql(true, mode, position, log, reverse, facets));
        var (ribbonSpec, _) = Lower(Sql(false, mode, position, log, reverse, facets));
        var resolver = new PlotPlanResolver();
        foreach (var bounds in new[] { new PlotBounds(0, 0, 800, 500), new PlotBounds(0, 0, 1100, 700) })
        {
            var plan = resolver.Resolve(spec, data, bounds);
            var ribbon = resolver.Resolve(ribbonSpec, data, bounds);
            Assert.Equal(13, spec.Version);
            Assert.Equal(16, plan.Version);
            Assert.True(plan.Layers[0].AreaConfidence);
            Assert.True(plan.Layers[0].AreaRibbon);
            Assert.Equal(new ResolvedCartesianAxes("distances", "estimates"), plan.CartesianAxes);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(ribbon.Scales), System.Text.Json.JsonSerializer.Serialize(plan.Scales));
            Assert.All(plan.Layers[0].Data, datum =>
            {
                Assert.DoesNotContain(datum.Channels, channel => channel.Channel is FieldChannel.YStart or FieldChannel.YEnd or FieldChannel.Y);
                Assert.Equal(data.Columns.Single(column => column.Name == "LowerBound").Values[datum.RowIndex],
                    Assert.Single(datum.Channels, channel => channel.Channel == FieldChannel.ConfidenceLow).Value);
                Assert.Equal(data.Columns.Single(column => column.Name == "UpperBound").Values[datum.RowIndex],
                    Assert.Single(datum.Channels, channel => channel.Channel == FieldChannel.ConfidenceHigh).Value);
            });
            var offsets = plan.Layers[0].Data.Select(datum => (datum.DisplayOffsetX, datum.DisplayOffsetY)).ToArray();
            if (!position.StartsWith("JITTER", StringComparison.Ordinal))
                Assert.Equal(ribbon.Layers[0].Data.Select(datum => (datum.DisplayOffsetX, datum.DisplayOffsetY)), offsets);
            // Stable jitter identities include channel names. Compare boundaries with the same physical offsets.
            ribbon = ribbon with
            {
                Layers = [ribbon.Layers[0] with { Data = ribbon.Layers[0].Data.Select((datum, index) => datum with
            { DisplayOffsetX = offsets[index].DisplayOffsetX, DisplayOffsetY = offsets[index].DisplayOffsetY }).ToImmutableArray() }]
            };
            var paths = Paths(plan, "plot-confidence-band");
            var ribbonPaths = Paths(ribbon, "plot-ribbon");
            Assert.Equal(facets ? 4 : 2, paths.Length);
            Assert.Equal(ribbonPaths.Select(path => path.Attribute("d")!.Value), paths.Select(path => path.Attribute("d")!.Value));
            Assert.All(paths, path => Assert.Contains("confidence interval", path.Value));
            Assert.All(plan.Fallback.Items, item => Assert.Equal("confidence interval", item.Detail));
            Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
            Assert.Equal(ChartContractSerializer.Serialize(spec), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(ChartContractSerializer.Serialize(spec))));
            var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 140).NormalizedText;
            Assert.Contains("confidence Y 14 to 3", terminal);
            Assert.Contains("gap", terminal);
        }
    }

    [Theory]
    [InlineData("LINEAR")]
    [InlineData("SMOOTH")]
    [InlineData("STEP_BEFORE")]
    [InlineData("STEP_AFTER")]
    public void MissingEmptyAndShortInputsRetainConfidenceContracts(string mode)
    {
        var (spec, data) = Lower(Sql(true, mode, "IDENTITY", false, false, false));
        foreach (var count in new[] { 0, 1, 2 })
        {
            var shortData = data with
            {
                RowCount = count,
                Columns = data.Columns.Select(column => column with
                { Values = column.Values.Take(count).ToImmutableArray(), DisplayValues = [] }).ToImmutableArray()
            };
            var plan = new PlotPlanResolver().Resolve(spec, shortData);
            Assert.Equal(16, plan.Version);
            if (count < 2) Assert.Empty(Paths(plan, "plot-confidence-band"));
            else Assert.DoesNotContain(" C ", Assert.Single(Paths(plan, "plot-confidence-band")).Attribute("d")!.Value);
            Assert.Throws<InvalidDataException>(() => (plan with { Layers = [plan.Layers[0] with { AreaConfidence = false }] }).Validate());
            Assert.Throws<InvalidDataException>(() => (plan with { Layers = [plan.Layers[0] with { AreaRibbon = false }] }).Validate());
            Assert.Throws<InvalidDataException>(() => (plan with { Schema = ChartContractVersions.OrdinaryAreaInterpolationPlotPlanSchema, Version = 15 }).Validate());
            Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
        }
        foreach (var missing in new[] { "Distance", "LowerBound", "UpperBound" })
        {
            var empty = data with
            {
                Columns = data.Columns.Select(column => column.Name == missing ? column with
                { Values = Enumerable.Repeat(ChartValue.Null(), data.RowCount).ToImmutableArray(), DisplayValues = [] } : column).ToImmutableArray()
            };
            var plan = new PlotPlanResolver().Resolve(spec, empty);
            Assert.Empty(Paths(plan, "plot-confidence-band"));
            Assert.All(plan.Layers[0].Data, datum => Assert.True(datum.IsGap));
        }
        Assert.Throws<InvalidDataException>(() => (spec with { Schema = ChartContractVersions.OrdinaryAreaInterpolationChartSpecSchema, Version = 12 }).Validate());
    }

    [Theory]
    [InlineData("NULL_HANDLING = GAP", "NULL_HANDLING = CONNECT")]
    [InlineData("NULL_HANDLING = GAP", "NULL_HANDLING = ZERO")]
    [InlineData("NULL_HANDLING = GAP,", "NULL_HANDLING = GAP, AREA_BASELINE = ZERO,")]
    [InlineData("NULL_HANDLING = GAP,", "NULL_HANDLING = GAP, CONDITIONS (COLOR WHEN UpperBound > 5 THEN '#ff0000' ELSE '#0000ff'),")]
    [InlineData("CONFIDENCE_LOW", "Y_START")]
    [InlineData("CONFIDENCE_HIGH", "Y_END")]
    [InlineData("X = Distance", "COLOR = Cohort (TYPE = NOMINAL), X = Distance")]
    [InlineData("= AREA (", "= LINE (")]
    public void UnsupportedCombinationsHavePositionedDiagnostics(string before, string after)
    {
        var sql = Sql(true, "SMOOTH", "IDENTITY", false, false, false).Replace(before, after, StringComparison.Ordinal);
        var statement = Assert.Single(new Parser(new Lexer(sql).Tokenize(), sql).Parse().Statements.OfType<CreateVisualStatement>());
        Assert.Contains(AdvancedChartSemanticValidator.Validate(statement), diagnostic => diagnostic.Line > 0 && diagnostic.Column > 0);
    }

    [Theory]
    [InlineData("LINEAR")]
    [InlineData("SMOOTH")]
    [InlineData("STEP_BEFORE")]
    [InlineData("STEP_AFTER")]
    public void MixedConditionedLayersRetainConfidenceAndEnvelopePrecedence(string mode)
    {
        var sql = Sql(true, mode, "IDENTITY", false, false, false);
        var (spec, data) = Lower(sql);
        var plain = new PlotPlanResolver().Resolve(spec, data);
        sql = sql.Replace("LAYERS (envelope =", "LAYERS (conditioned = LINE (NULL_HANDLING = GAP, ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = distances), Y = UpperBound (TYPE = QUANTITATIVE, SCALE = estimates)), STYLE (INTERPOLATION = 'SMOOTH'), CONDITIONS (COLOR WHEN UpperBound > 5 THEN '#ff0000' ELSE '#0000ff')), envelope =", StringComparison.Ordinal);
        var (mixedSpec, mixedData) = Lower(sql);
        var mixed = new PlotPlanResolver().Resolve(mixedSpec, mixedData);
        Assert.Equal(13, mixedSpec.Version);
        Assert.Equal(16, mixed.Version);
        Assert.Single(mixed.Layers, layer => layer.AreaConfidence);
        Assert.Single(mixed.Layers, layer => layer.ConnectionInterpolation is not null);
        Assert.Equal(Paths(plain, "plot-confidence-band").Select(path => path.Attribute("d")!.Value),
            Paths(mixed, "plot-confidence-band").Select(path => path.Attribute("d")!.Value));
        Assert.Contains("confidence Y 14 to 3", TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(mixed), 140).NormalizedText);
    }

    [Theory]
    [InlineData("LINEAR")]
    [InlineData("SMOOTH")]
    [InlineData("STEP_BEFORE")]
    [InlineData("STEP_AFTER")]
    public async Task AuthoringDesignerLineageAndPdfPreserveConfidenceIntent(string mode)
    {
        var sql = Sql(true, mode, "JITTER(X = 0.02, Y = 0.03, KEY = Id, SEED = 42)", true, true, true);
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        Assert.Equal(statement.ToSql(), Assert.Single(new Parser(new Lexer(statement.ToSql()).Tokenize(), statement.ToSql()).Parse().Statements).ToSql());
        var source = sql + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Envelope)));";
        var design = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(design.Error);
        var patched = new DesignerScriptPatcher().Patch(source, design.DesignState);
        Assert.Contains("CONFIDENCE_LOW", patched);
        Assert.Contains(mode, patched);
        Assert.Empty(new Parser(new Lexer(patched).Tokenize(), patched).Parse().Diagnostics);
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(script);
        foreach (var field in new[] { "Distance", "LowerBound", "UpperBound", "Id", "Cohort" })
            Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Envelope" && entry.SourceColumns.Contains(field));
        var (spec, data) = Lower(sql);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.True(PlotPlanConformanceHarness.Evaluate(plan, [new ProjectionBackend(false)]).IsConformant);
        Assert.False(PlotPlanConformanceHarness.Evaluate(plan, [new ProjectionBackend(true)]).IsConformant);
        var report = new ReportManifest
        {
            Title = "Envelope",
            Source = "envelope.rptsql",
            Visuals = [new VisualManifest { Name = "Envelope", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = new SvgChartRenderer().Render(plan) }]
        };
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await new PdfExporter().ExportAsync(report))[..4]);
    }

    private sealed class ProjectionBackend(bool eraseConfidence) : IPlotPlanSemanticBackend
    {
        public string Name => "confidence-band";
        public PlotSemanticProjection Project(PlotPlan plan)
        {
            var projection = PlotSemanticProjection.FromPlan(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan)));
            return eraseConfidence ? projection with { Layers = projection.Layers.Select(layer => layer with { AreaConfidence = false }).ToImmutableArray() } : projection;
        }
    }

    [Theory]
    [InlineData("LINEAR")]
    [InlineData("SMOOTH")]
    [InlineData("STEP_BEFORE")]
    [InlineData("STEP_AFTER")]
    public void ConfidenceWorkloadPreservesExistingBudgets(string mode)
    {
        var (spec, data) = Lower(Sql(true, mode, "JITTER(X = 0.02, Y = 0.03, KEY = Id, SEED = 42)", true, false, true));
        const int rowCount = 600;
        data = data with
        {
            RowCount = rowCount,
            Columns = data.Columns.Select(column => column with
            {
                Values = Enumerable.Range(0, rowCount).Select(row => column.Name switch
                {
                    "Cohort" => ChartValue.From("P" + row / 50),
                    "Id" => ChartValue.From("K" + row),
                    "LowerBound" => ChartValue.From((decimal)(17 - row % 16)),
                    _ => ChartValue.From((decimal)(2 + row % 16))
                }).ToImmutableArray(),
                DisplayValues = []
            }).ToImmutableArray()
        };
        var before = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 1200, 800));
        var allocation = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "Confidence resolution exceeded 5 seconds."); // flaky-time-bound-ok: bounds the established 600-row, 12-panel workload.
        clock.Restart();
        var svg = new SvgChartRenderer().Render(plan);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "Confidence rendering exceeded 5 seconds."); // flaky-time-bound-ok: bounds the established 600-row, 12-panel workload.
        Assert.Equal(12, plan.Facets.Length);
        Assert.Equal(12, Paths(plan, "plot-confidence-band").Length);
        Assert.True(allocation < 16L * 1024 * 1024, $"Resolution allocated {allocation:N0} bytes.");
        Assert.InRange(Encoding.UTF8.GetByteCount(ChartContractSerializer.Serialize(plan)), 1, 6 * 1024 * 1024);
        Assert.InRange(Encoding.UTF8.GetByteCount(svg), 1, 600 * 1024);
    }

    [Theory]
    [InlineData("LINEAR", "B424AFCA8FC4370F0B95715F77582A29EF0D95774BD94F0A2FC15655774BF38A", "3035598EEE010F62CA59F10D61C355CA476353F0E0ED17BE6DF84E75B46973B5", "4959F99BBB8DE81919DD8F0D748DA917DED4353FE6F5E76D8005C5862885D64E")]
    [InlineData("SMOOTH", "07F41F4DC4580DB0727646E598134AA1B6C1AD0E10BF7D23C2510C089822037B", "7018829404EB21857C0FD7BFDBF61F1C18CE0AA7488E7872F2620DEEF54EAF14", "16A0354B026DF397F6441863CC6592779BDD9EA108B993097F03C20DD3444641")]
    [InlineData("STEP_BEFORE", "1939A68AC18F8742150E9E2FF73E0867AF25FEA0D6E121D4C4D242445BB55350", "5F271D6FD62BCC7BBC280A70CFE3D863A44BC3E75A4456F6F910F1EB482E567D", "EDCA700776D9701F71CE6C4BF0B944ACD5A2AD2060465307D517CD3E6B8C130B")]
    [InlineData("STEP_AFTER", "429FEE0B8D903FD6E41C34076940A5BD30AC00B6A1CE802E82A9189EDDC27C7E", "84A881675852D2206A1D6DE7FA82A138F3D3D0046A7AE58F0EC4DD87EE89631D", "8DA75CCFBEA8DBDDCAF47604FF2220828E10568734858E864F7FB25D8BFBFCC8")]
    public void PlanSvgAndTerminalMatchDeterministicFixtures(string mode, string expectedPlan, string expectedSvg, string expectedTerminal)
    {
        var (spec, data) = Lower(Sql(true, mode, "JITTER(X = 0.02, Y = 0.03, KEY = Id, SEED = 42)", true, true, true));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        var terminalHash = Hash(TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 140).NormalizedText);
        Assert.True(planHash == expectedPlan && svgHash == expectedSvg && terminalHash == expectedTerminal,
            $"{mode}: plan {planHash}; svg {svgHash}; terminal {terminalHash}");
    }

    [Theory]
    [InlineData("LINEAR")]
    [InlineData("SMOOTH")]
    [InlineData("STEP_BEFORE")]
    [InlineData("STEP_AFTER")]
    public void ConstantsAndRelayoutPreserveRawConfidenceBindings(string mode)
    {
        var sql = Sql(true, mode, "IDENTITY", true, true, true)
            .Replace("X = Distance (", "X = DATUM(3) (", StringComparison.Ordinal)
            .Replace("= LowerBound (", "= DATUM(9) (", StringComparison.Ordinal)
            .Replace("= UpperBound (", "= DATUM(5) (", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var resolver = new PlotPlanResolver();
        var initial = resolver.Resolve(spec, data);
        var bounds = new PlotBounds(0, 0, 1100, 700);
        var plan = resolver.Relayout(spec, data, initial, bounds);
        Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, bounds)), ChartContractSerializer.Serialize(plan));
        Assert.Equal(CoordinateKind.TransposedCartesian, PlotSemanticProjection.FromPlan(plan).ConnectedCoordinate);
        Assert.All(plan.Layers[0].Data, datum => Assert.False(datum.IsGap));
        Assert.Equal(2, Paths(plan, "plot-confidence-band").Length);
        Assert.All(plan.Fallback.Items, item => Assert.Contains("confidence Y 9 to 5", item.Value));
    }

    [Fact]
    public void PublishedConfidenceExampleParsesAndRoundTrips()
    {
        var path = Path.Combine(TerminalSnapshotHarness.GetRepoRoot(), "docs", "reference", "visuals-reporting", "visuals", "chart.md");
        var sql = Regex.Matches(File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal), @"```sql\n([\s\S]*?)```")
            .Select(match => match.Groups[1].Value).Single(block => block.Contains("CREATE VISUAL ConfidenceBand", StringComparison.Ordinal));
        var (spec, data) = Lower(sql);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(16, plan.Version);
        Assert.Equal(2, Paths(plan, "plot-confidence-band").Length);
        Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
    }

    private static XElement[] Paths(PlotPlan plan, string name) => XDocument.Parse(new SvgChartRenderer().Render(plan)).Descendants()
        .Where(element => (string?)element.Attribute("class") == name).ToArray();

    private static (ChartSpec Spec, ChartDataSet Data) Lower(string sql)
    {
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        Assert.Empty(AdvancedChartSemanticValidator.Validate(statement));
        var manifest = new VisualManifest
        {
            Name = "Envelope",
            Columns = ["Distance", "LowerBound", "UpperBound", "Cohort", "Id"],
            Rows = Enumerable.Range(0, sql.Contains("FACET", StringComparison.Ordinal) ? 16 : 8).Select(index => new List<string?>
            {
                new[] { 8, 2, 5, 4, 7, 3, 9, 2 }[index % 8].ToString(CultureInfo.InvariantCulture),
                new[] { 14, 3, 12, 4, 15, 6, 11, 2 }[index % 8].ToString(CultureInfo.InvariantCulture),
                index % 8 == 3 ? null : new[] { 3, 14, 5, 4, 9, 12, 6, 8 }[index % 8].ToString(CultureInfo.InvariantCulture),
                index < 8 ? "A" : "B", "K" + index.ToString(CultureInfo.InvariantCulture)
            }).ToList()
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
