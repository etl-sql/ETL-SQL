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

public sealed class TransposedAreaInterpolationTests
{
    private static string Sql(bool ribbon, string mode, string position, bool log, bool reverse, bool facets) => $$"""
        CREATE VISUAL Envelope AS CUSTOM (SOURCE = #prepared, CHART (
          COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
          {{(facets ? "FACET (WRAP = Cohort, COLUMNS = 2), RESOLVE (X = INDEPENDENT, Y = INDEPENDENT)," : "")}}
          SCALES (unusedX = BAND (CHANNEL = X), unusedY = LINEAR (CHANNEL = Y, MAX = 1000),
            distances = {{(log ? "LOGARITHMIC" : "LINEAR")}} (CHANNEL = X, INCLUDE_ZERO = OFF, MIN = 1, MAX = 20, REVERSE = {{(reverse ? "ON" : "OFF")}}),
            estimates = {{(log && ribbon ? "LOGARITHMIC" : "LINEAR")}} (CHANNEL = Y, INCLUDE_ZERO = OFF, MIN = {{(ribbon ? "1" : "0")}}, MAX = 20, REVERSE = {{(reverse ? "ON" : "OFF")}})),
          LAYERS (envelope = AREA (NULL_HANDLING = GAP, POSITION = {{position}}, {{(ribbon ? "" : "AREA_BASELINE = ZERO,")}}
            ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = distances),
              {{(ribbon ? "Y_START = LowerBound (TYPE = QUANTITATIVE, SCALE = estimates), Y_END = UpperBound" : "Y = UpperBound")}} (TYPE = QUANTITATIVE, SCALE = estimates)),
            STYLE (INTERPOLATION = '{{mode}}', COLOR = '#112233')))
        ));
        """;

    public static IEnumerable<object[]> Cases()
    {
        foreach (var ribbon in new[] { false, true })
            foreach (var mode in new[] { "SMOOTH", "STEP_BEFORE", "STEP_AFTER" })
                foreach (var position in new[] { "IDENTITY", "NUDGE(X = 0.1, Y = 0.2, UNIT = EM)", "NUDGE(X = 0.1, Y = 0.2, UNIT = BAND)",
                    "NUDGE(X = 0.1, Y = 0.2, UNIT = DATA)", "JITTER(X = 0.02, Y = 0.03, KEY = Id, SEED = 42)" })
                    foreach (var log in new[] { false, true })
                        foreach (var reverse in new[] { false, true })
                            foreach (var facets in new[] { false, true }) yield return [ribbon, mode, position, log, reverse, facets];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void BothBoundariesInterpolatePlacedVerticesAndCloseInReverseOrder(bool ribbon, string mode, string position, bool log, bool reverse, bool facets)
    {
        var (spec, data) = Lower(Sql(ribbon, mode, position, log, reverse, facets));
        var (linear, _) = Lower(Sql(ribbon, "LINEAR", position, log, reverse, facets));
        var resolver = new PlotPlanResolver();
        foreach (var bounds in new[] { new PlotBounds(0, 0, 800, 500), new PlotBounds(0, 0, 1100, 700) })
        {
            var plan = resolver.Resolve(spec, data, bounds);
            var baseline = resolver.Resolve(linear, data, bounds);
            Assert.Equal(12, spec.Version);
            Assert.Equal(15, plan.Version);
            Assert.Equal(new ResolvedCartesianAxes("distances", "estimates"), plan.CartesianAxes);
            Assert.Equal(mode, ResolvedConnectionGeometry.Name(plan.Layers[0].PathInterpolation!.Value));
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(baseline.Scales), System.Text.Json.JsonSerializer.Serialize(plan.Scales));
            Assert.Equal(baseline.Layers[0].Data.SelectMany(datum => datum.Channels), plan.Layers[0].Data.SelectMany(datum => datum.Channels));
            Assert.Equal(baseline.Layers[0].Data.Select(datum => (datum.IsGap, datum.DisplayOffsetX, datum.DisplayOffsetY)),
                plan.Layers[0].Data.Select(datum => (datum.IsGap, datum.DisplayOffsetX, datum.DisplayOffsetY)));
            Assert.Equal(baseline.Fallback.Items.ToArray(), plan.Fallback.Items.ToArray());
            var paths = Paths(plan);
            var straight = Paths(baseline);
            Assert.Equal(facets ? 4 : 2, paths.Length);
            for (var run = 0; run < paths.Length; run++)
            {
                var vertices = Points(straight[run].Attribute("d")!.Value);
                var count = vertices.Length / 2;
                var expected = Boundary(vertices[..count], mode, false).Concat(Boundary(vertices[count..], mode, true)).ToArray();
                var path = paths[run].Attribute("d")!.Value;
                Assert.EndsWith(" Z", path);
                Assert.Equal(mode == "SMOOTH" ? 2 * (count - 1) : 0, path.Count(character => character == 'C'));
                var actual = Points(path);
                Assert.Equal(expected.Length, actual.Length);
                for (var i = 0; i < actual.Length; i++)
                {
                    Assert.InRange(Math.Abs(expected[i].X - actual[i].X), 0m, .002m);
                    Assert.InRange(Math.Abs(expected[i].Y - actual[i].Y), 0m, .002m);
                }
                Assert.Equal(straight[run].Value, paths[run].Value);
            }
            Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
            Assert.Equal(ChartContractSerializer.Serialize(spec), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(ChartContractSerializer.Serialize(spec))));
            var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 140).NormalizedText;
            Assert.Contains(mode, terminal);
            Assert.Contains("gap", terminal);
        }
    }

    private static IEnumerable<(decimal X, decimal Y)> Boundary((decimal X, decimal Y)[] vertices, string mode, bool lower)
    {
        yield return vertices[0];
        for (var i = 1; i < vertices.Length; i++)
        {
            var start = vertices[i - 1];
            var end = vertices[i];
            if (mode == "SMOOTH" && vertices.Length > 2)
            {
                var before = i == 1 ? start : vertices[i - 2];
                var after = i + 1 == vertices.Length ? end : vertices[i + 1];
                yield return (start.X + (end.X - before.X) / 6m, start.Y + (end.Y - before.Y) / 6m);
                yield return (end.X - (after.X - start.X) / 6m, end.Y - (after.Y - start.Y) / 6m);
            }
            else if (mode != "SMOOTH") yield return (mode == "STEP_BEFORE") != lower ? (end.X, start.Y) : (start.X, end.Y);
            yield return end;
        }
    }

    [Theory]
    [InlineData(false, "SMOOTH")]
    [InlineData(true, "SMOOTH")]
    [InlineData(false, "STEP_BEFORE")]
    [InlineData(true, "STEP_BEFORE")]
    [InlineData(false, "STEP_AFTER")]
    [InlineData(true, "STEP_AFTER")]
    public void EmptyShortAndIncompleteRunsPreserveGuardedIntent(bool ribbon, string mode)
    {
        var (spec, data) = Lower(Sql(ribbon, mode, "IDENTITY", false, false, false));
        foreach (var count in new[] { 0, 1, 2 })
        {
            var shortData = data with
            {
                RowCount = count,
                Columns = data.Columns.Select(column => column with
                { Values = column.Values.Take(count).ToImmutableArray(), DisplayValues = [] }).ToImmutableArray()
            };
            var plan = new PlotPlanResolver().Resolve(spec, shortData);
            Assert.Equal(15, plan.Version);
            if (count < 2) Assert.Empty(Paths(plan));
            else
            {
                var path = Assert.Single(Paths(plan)).Attribute("d")!.Value;
                Assert.DoesNotContain("C", path);
                Assert.Equal(mode == "SMOOTH" ? 4 : 6, Points(path).Length);
            }
            Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
        }
        foreach (var missing in ribbon ? new[] { "Distance", "LowerBound", "UpperBound" } : new[] { "Distance", "UpperBound" })
        {
            var empty = data with
            {
                Columns = data.Columns.Select(column => column.Name == missing ? column with
                { Values = Enumerable.Repeat(ChartValue.Null(), data.RowCount).ToImmutableArray(), DisplayValues = [] } : column).ToImmutableArray()
            };
            var plan = new PlotPlanResolver().Resolve(spec, empty);
            Assert.Empty(Paths(plan));
            Assert.All(plan.Layers[0].Data, datum => Assert.True(datum.IsGap));
        }
        var complete = new PlotPlanResolver().Resolve(spec, data);
        Assert.Throws<InvalidDataException>(() => (spec with { Schema = ChartContractVersions.OrdinaryInterpolationChartSpecSchema, Version = 11 }).Validate());
        Assert.Throws<InvalidDataException>(() => (complete with { Schema = ChartContractVersions.OrdinaryInterpolationPlotPlanSchema, Version = 14 }).Validate());
        Assert.Throws<InvalidDataException>(() => (complete with { Layers = [complete.Layers[0] with { PathInterpolation = null }] }).Validate());
        Assert.Throws<InvalidDataException>(() => (complete with { Layers = [complete.Layers[0] with { PathInterpolation = (ConnectedInterpolationKind)999 }] }).Validate());
        Assert.Throws<InvalidDataException>(() => (complete with { CartesianAxes = null }).Validate());
        Assert.True(PlotPlanConformanceHarness.Evaluate(complete, [new ProjectionBackend(false)]).IsConformant);
        Assert.False(PlotPlanConformanceHarness.Evaluate(complete, [new ProjectionBackend(true)]).IsConformant);
    }

    private sealed class ProjectionBackend(bool eraseInterpolation) : IPlotPlanSemanticBackend
    {
        public string Name => "area-interpolation";
        public PlotSemanticProjection Project(PlotPlan plan)
        {
            var projection = PlotSemanticProjection.FromPlan(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan)));
            return eraseInterpolation ? projection with { Layers = projection.Layers.Select(layer => layer with { PathInterpolation = null }).ToImmutableArray() } : projection;
        }
    }

    [Theory]
    [InlineData(false, "SMOOTH")]
    [InlineData(true, "SMOOTH")]
    [InlineData(false, "STEP_BEFORE")]
    [InlineData(true, "STEP_BEFORE")]
    [InlineData(false, "STEP_AFTER")]
    [InlineData(true, "STEP_AFTER")]
    public async Task AuthoringDesignerLineageAndPdfPreserveBounds(bool ribbon, string mode)
    {
        var sql = Sql(ribbon, mode, "JITTER(X = 0.02, Y = 0.03, KEY = Id, SEED = 42)", true, true, true);
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        Assert.Equal(statement.ToSql(), Assert.Single(new Parser(new Lexer(statement.ToSql()).Tokenize(), statement.ToSql()).Parse().Statements).ToSql());
        var source = sql + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Envelope)));";
        var design = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(design.Error);
        var patched = new DesignerScriptPatcher().Patch(source, design.DesignState);
        Assert.Contains(mode, patched);
        Assert.Empty(new Parser(new Lexer(patched).Tokenize(), patched).Parse().Diagnostics);
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(script);
        foreach (var field in ribbon ? new[] { "Distance", "LowerBound", "UpperBound", "Id", "Cohort" } : new[] { "Distance", "UpperBound", "Id", "Cohort" })
            Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Envelope" && entry.SourceColumns.Contains(field));
        var (spec, data) = Lower(sql);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var report = new ReportManifest
        {
            Title = "Envelope",
            Source = "envelope.rptsql",
            Visuals = [new VisualManifest { Name = "Envelope", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = new SvgChartRenderer().Render(plan) }]
        };
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await new PdfExporter().ExportAsync(report))[..4]);
    }

    [Theory]
    [InlineData(false, "SMOOTH")]
    [InlineData(true, "SMOOTH")]
    [InlineData(false, "STEP_BEFORE")]
    [InlineData(true, "STEP_BEFORE")]
    [InlineData(false, "STEP_AFTER")]
    [InlineData(true, "STEP_AFTER")]
    public void WorkloadKeepsExistingAllocationTimeAndPayloadBudgets(bool ribbon, string mode)
    {
        var (spec, data) = Lower(Sql(ribbon, mode, "JITTER(X = 0.02, Y = 0.03, KEY = Id, SEED = 42)", true, false, true));
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
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "AREA resolution exceeded 5 seconds."); // flaky-time-bound-ok: bounds the established 600-row, 12-panel workload.
        clock.Restart();
        var svg = new SvgChartRenderer().Render(plan);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "AREA rendering exceeded 5 seconds."); // flaky-time-bound-ok: bounds the established 600-row, 12-panel workload.
        Assert.Equal(12, plan.Facets.Length);
        Assert.Equal(12, Paths(plan).Length);
        Assert.True(allocation < 16L * 1024 * 1024, $"Resolution allocated {allocation:N0} bytes.");
        Assert.InRange(Encoding.UTF8.GetByteCount(ChartContractSerializer.Serialize(plan)), 1, 6 * 1024 * 1024);
        Assert.InRange(Encoding.UTF8.GetByteCount(svg), 1, 600 * 1024);
    }

    [Theory]
    [InlineData(false, "SMOOTH")]
    [InlineData(true, "SMOOTH")]
    [InlineData(false, "STEP_BEFORE")]
    [InlineData(true, "STEP_BEFORE")]
    [InlineData(false, "STEP_AFTER")]
    [InlineData(true, "STEP_AFTER")]
    public void MixedLinesAndConnectionsPreserveAreaEnvelopeAndBoundaryGeometry(bool ribbon, string mode)
    {
        var sql = Sql(ribbon, mode, "IDENTITY", false, false, false);
        var (spec, data) = Lower(sql);
        var plain = new PlotPlanResolver().Resolve(spec, data);
        var layered = sql.Replace("LAYERS (envelope =", "LAYERS (ordinary = LINE (NULL_HANDLING = GAP, ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = distances), Y = UpperBound (TYPE = QUANTITATIVE, SCALE = estimates)), STYLE (INTERPOLATION = 'STEP_BEFORE')), conditioned = LINE (NULL_HANDLING = GAP, ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = distances), Y = UpperBound (TYPE = QUANTITATIVE, SCALE = estimates)), STYLE (INTERPOLATION = 'SMOOTH'), CONDITIONS (COLOR WHEN UpperBound > 5 THEN '#ff0000' ELSE '#0000ff')), envelope =", StringComparison.Ordinal);
        var (mixedSpec, mixedData) = Lower(layered);
        var mixed = new PlotPlanResolver().Resolve(mixedSpec, mixedData);
        Assert.Equal(12, mixedSpec.Version);
        Assert.Equal(15, mixed.Version);
        Assert.Single(mixed.Layers, layer => layer.ConnectionInterpolation is not null);
        Assert.Equal(2, mixed.Layers.Count(layer => layer.PathInterpolation is not null));
        Assert.Equal(Paths(plain).Select(path => path.Attribute("d")!.Value), Paths(mixed).Select(path => path.Attribute("d")!.Value));
        Assert.Equal(ChartContractSerializer.Serialize(mixed), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(mixed))));
        if (!ribbon)
            Assert.Throws<InvalidDataException>(() => (mixed with { Scales = mixed.Scales.Select(scale => scale.Id == "estimates" ? scale with { IncludesZero = false } : scale).ToImmutableArray() }).Validate());
    }

    [Theory]
    [InlineData(false, "SMOOTH")]
    [InlineData(true, "SMOOTH")]
    [InlineData(false, "STEP_BEFORE")]
    [InlineData(true, "STEP_BEFORE")]
    [InlineData(false, "STEP_AFTER")]
    [InlineData(true, "STEP_AFTER")]
    public void ConstantsAndRelayoutKeepCoincidentCrossSections(bool ribbon, string mode)
    {
        var sql = Sql(ribbon, mode, "IDENTITY", true, true, true)
            .Replace("X = Distance (", "X = DATUM(3) (", StringComparison.Ordinal)
            .Replace("= LowerBound (", "= DATUM(9) (", StringComparison.Ordinal)
            .Replace("= UpperBound (", "= DATUM(5) (", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var resolver = new PlotPlanResolver();
        var initial = resolver.Resolve(spec, data);
        var bounds = new PlotBounds(0, 0, 1100, 700);
        var plan = resolver.Relayout(spec, data, initial, bounds);
        Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, bounds)), ChartContractSerializer.Serialize(plan));
        Assert.All(plan.Layers[0].Data, datum => Assert.False(datum.IsGap));
        Assert.Equal(2, Paths(plan).Length);
        foreach (var path in Paths(plan)) Assert.Equal(2, Points(path.Attribute("d")!.Value).Distinct().Count());
    }

    [Theory]
    [InlineData(false, "SMOOTH", "A0E9285BA3B9BB45117795D6B3190AC074A4D4ED6D8B4B3BE5FF9464AAAD947C", "C404E724EA1B615BB5516E8316DEBFCA254269010F93B95B1396BA26D9234ACB", "37B0F09FD44484E7E29AB97347C4014AAB9A17A874093C6342A4A1E05987979C")]
    [InlineData(true, "SMOOTH", "34B8F90B4EBBAA22B996F0D06BA37B6A6107B2B6C82EFD1AAD4F7C438D220A15", "59A2A9AA258246032D80D3E72F0EABD5473EC83D33A6BCBE4D44EA56836BE755", "E338CBAF81F5EC48CDD5F1E35126D5AFCDD9A2BEBE42D5CB520DEC5041661F5A")]
    [InlineData(false, "STEP_BEFORE", "84F7C607386588F0F97C0FE0AC365CE2E9675F3D7DEA6549902254CE7E034B8F", "71368801F92825ACA3016771F46051B45D7A9ECF9C69CF338BEF44A5E77B55D6", "0A816281018B62ED5AB8EC576E7B1BADFE7E73A82BEB76015F3D2BECC1DCC647")]
    [InlineData(true, "STEP_BEFORE", "3CCBCE11DC181265A79FBB3740DDDD8B5DB4C0C392EE9E3F6CBD319CEBE39A8A", "C529F58D6C43F867D1946F30D8C74022637B6A2CA5C4342172E2FEF912E7A3ED", "6C7C9007A26A3EF587B0764547E7919F2EF4A14C0FDB34913CB6E3803FD7C56B")]
    [InlineData(false, "STEP_AFTER", "1CA32D67269D1A4A2B0296FAFB8FC52756DC38BF8E13A02EE19CC0A40339957D", "C9E82F6BBDECDBD23B176C4AD8CA4A175A310035761CF26A0B9471482BDD49A7", "C74CE43A95682A4E2798BAF0A1169BCEC588631F554B0A837B9A93887F7D8B79")]
    [InlineData(true, "STEP_AFTER", "003C9AF8D5A800D7A43C1DCD5A0438E011BD4F75F9FAC994A91BE9D045450BE3", "458D144586A9B6F4F070AA5BCCF9391F04F1C41492BDA9465A42E22E06AC8DF1", "41B78A4BAADC9A0878D77EF7AB9DEB4FCBBFF53B06506B956393963757780B69")]
    public void PlanSvgAndTerminalMatchDeterministicFixtures(bool ribbon, string mode, string expectedPlan, string expectedSvg, string expectedTerminal)
    {
        var (spec, data) = Lower(Sql(ribbon, mode, "JITTER(X = 0.02, Y = 0.03, KEY = Id, SEED = 42)", true, true, true));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        var terminalHash = Hash(TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 140).NormalizedText);
        Assert.True(planHash == expectedPlan && svgHash == expectedSvg && terminalHash == expectedTerminal,
            $"{ribbon}/{mode}: plan {planHash}; svg {svgHash}; terminal {terminalHash}");
    }

    [Fact]
    public void PublishedSmoothRibbonExampleParsesAndRoundTrips()
    {
        var path = Path.Combine(TerminalSnapshotHarness.GetRepoRoot(), "docs", "reference", "visuals-reporting", "visuals", "chart.md");
        var sql = Regex.Matches(File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal), @"```sql\n([\s\S]*?)```")
            .Select(match => match.Groups[1].Value).Single(block => block.Contains("CREATE VISUAL SmoothRibbon", StringComparison.Ordinal));
        var (spec, data) = Lower(sql);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(15, plan.Version);
        Assert.Equal(2, Paths(plan).Length);
        Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
    }

    private static XElement[] Paths(PlotPlan plan) => XDocument.Parse(new SvgChartRenderer().Render(plan)).Descendants()
        .Where(element => (string?)element.Attribute("class") is "plot-area" or "plot-ribbon").ToArray();

    private static (decimal X, decimal Y)[] Points(string path)
    {
        var numbers = Regex.Matches(path, @"-?\d+(?:\.\d+)?").Select(match => decimal.Parse(match.Value, CultureInfo.InvariantCulture)).ToArray();
        return Enumerable.Range(0, numbers.Length / 2).Select(index => (numbers[index * 2], numbers[index * 2 + 1])).ToArray();
    }

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
