using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
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

public sealed class TransposedLineInterpolationTests
{
    private static string Sql(string mode, string position, bool log, bool reverse, bool facets) => $$"""
        CREATE VISUAL Route AS CUSTOM (SOURCE = #prepared, CHART (
          COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
          {{(facets ? "FACET (WRAP = Cohort, COLUMNS = 2), RESOLVE (X = INDEPENDENT, Y = INDEPENDENT)," : "")}}
          SCALES (distances = {{(log ? "LOGARITHMIC" : "LINEAR")}} (CHANNEL = X, INCLUDE_ZERO = OFF, MIN = 1, MAX = 20, REVERSE = {{(reverse ? "ON" : "OFF")}}),
                  estimates = {{(log ? "LOGARITHMIC" : "LINEAR")}} (CHANNEL = Y, INCLUDE_ZERO = OFF, MIN = 1, MAX = 20, REVERSE = {{(reverse ? "ON" : "OFF")}})),
          LAYERS (route = LINE (NULL_HANDLING = GAP, POSITION = {{position}},
            ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = distances), Y = Estimate (TYPE = QUANTITATIVE, SCALE = estimates)),
            STYLE (INTERPOLATION = '{{mode}}', COLOR = '#112233')))
        ));
        """;

    public static IEnumerable<object[]> Cases()
    {
        foreach (var mode in new[] { "SMOOTH", "STEP_BEFORE", "STEP_AFTER" })
            foreach (var position in new[] { "IDENTITY", "NUDGE(X = 0.1, Y = 0.2, UNIT = EM)", "NUDGE(X = 0.1, Y = 0.2, UNIT = BAND)",
                "NUDGE(X = 0.1, Y = 0.2, UNIT = DATA)", "JITTER(X = 0.02, Y = 0.03, KEY = Id, SEED = 42)" })
                foreach (var log in new[] { false, true })
                    foreach (var reverse in new[] { false, true })
                        foreach (var facets in new[] { false, true }) yield return [mode, position, log, reverse, facets];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void CurvesUseMappedPlacedVerticesAndGapBoundaries(string mode, string position, bool log, bool reverse, bool facets)
    {
        var (spec, data) = Lower(Sql(mode, position, log, reverse, facets));
        var (linear, _) = Lower(Sql("LINEAR", position, log, reverse, facets));
        var resolver = new PlotPlanResolver();
        foreach (var bounds in new[] { new PlotBounds(0, 0, 800, 500), new PlotBounds(0, 0, 1100, 700) })
        {
            var plan = resolver.Resolve(spec, data, bounds);
            var baseline = resolver.Resolve(linear, data, bounds);
            Assert.Equal(11, spec.Version);
            Assert.Equal(14, plan.Version);
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
            Assert.Equal(straight.Length, paths.Length);
            for (var run = 0; run < paths.Length; run++)
            {
                var vertices = Points(straight[run]);
                var expected = new List<(decimal X, decimal Y)> { vertices[0] };
                for (var i = 1; i < vertices.Length; i++)
                {
                    var start = vertices[i - 1];
                    var end = vertices[i];
                    if (mode == "SMOOTH" && vertices.Length > 2)
                    {
                        var before = i == 1 ? start : vertices[i - 2];
                        var after = i + 1 == vertices.Length ? end : vertices[i + 1];
                        expected.Add((start.X + (end.X - before.X) / 6m, start.Y + (end.Y - before.Y) / 6m));
                        expected.Add((end.X - (after.X - start.X) / 6m, end.Y - (after.Y - start.Y) / 6m));
                    }
                    else if (mode != "SMOOTH") expected.Add(mode == "STEP_BEFORE" ? (end.X, start.Y) : (start.X, end.Y));
                    expected.Add(end);
                }
                Assert.Equal(mode == "SMOOTH" ? vertices.Length - 1 : 0, paths[run].Count(character => character == 'C'));
                var actual = Points(paths[run]);
                Assert.Equal(expected.Count, actual.Length);
                for (var i = 0; i < actual.Length; i++)
                {
                    Assert.InRange(Math.Abs(expected[i].X - actual[i].X), 0m, .002m);
                    Assert.InRange(Math.Abs(expected[i].Y - actual[i].Y), 0m, .002m);
                }
            }
            var json = ChartContractSerializer.Serialize(plan);
            Assert.Equal(json, ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(json)));
            Assert.Equal(ChartContractSerializer.Serialize(spec), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(ChartContractSerializer.Serialize(spec))));
            var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 140).NormalizedText;
            Assert.Contains(mode, terminal);
            Assert.Contains("gap", terminal);
            Assert.Contains("X vertical, Y horizontal", terminal);
        }
    }

    [Theory]
    [InlineData("SMOOTH")]
    [InlineData("STEP_BEFORE")]
    [InlineData("STEP_AFTER")]
    public void ContractsRejectDowngradesAndMissingOrInventedInterpolation(string mode)
    {
        var (spec, data) = Lower(Sql(mode, "IDENTITY", false, false, false));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Throws<InvalidDataException>(() => (spec with { Schema = ChartContractVersions.ChartSpecSchema, Version = 2 }).Validate());
        Assert.Throws<InvalidDataException>(() => (plan with { Schema = ChartContractVersions.PlotPlanSchema, Version = 3 }).Validate());
        Assert.Throws<InvalidDataException>(() => (plan with { Layers = [plan.Layers[0] with { PathInterpolation = null }] }).Validate());
        Assert.Throws<InvalidDataException>(() => (plan with { Layers = [plan.Layers[0] with { PathInterpolation = (ConnectedInterpolationKind)999 }] }).Validate());
        Assert.Throws<InvalidDataException>(() => (plan with { CartesianAxes = null }).Validate());
        Assert.Throws<InvalidDataException>(() => (plan with { Coordinate = plan.Coordinate! with { Kind = CoordinateKind.Cartesian } }).Validate());
        Assert.Throws<InvalidDataException>(() => (plan with { Layers = [plan.Layers[0] with { Mark = MarkKind.Point }] }).Validate());
        Assert.True(PlotPlanConformanceHarness.Evaluate(plan, [new ProjectionBackend(false)]).IsConformant);
        Assert.False(PlotPlanConformanceHarness.Evaluate(plan, [new ProjectionBackend(true)]).IsConformant);
    }

    private sealed class ProjectionBackend(bool eraseInterpolation) : IPlotPlanSemanticBackend
    {
        public string Name => "ordinary-interpolation";
        public PlotSemanticProjection Project(PlotPlan plan)
        {
            var projection = PlotSemanticProjection.FromPlan(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan)));
            return eraseInterpolation ? projection with { Layers = projection.Layers.Select(layer => layer with { PathInterpolation = null }).ToImmutableArray() } : projection;
        }
    }

    [Theory]
    [InlineData("SMOOTH")]
    [InlineData("STEP_BEFORE")]
    [InlineData("STEP_AFTER")]
    public void ConstantCoordinatesAndRelayoutRetainRepeatedAnchors(string mode)
    {
        var sql = Sql(mode, "IDENTITY", true, true, true).Replace("X = Distance (", "X = DATUM(3) (", StringComparison.Ordinal)
            .Replace("Y = Estimate (", "Y = DATUM(5) (", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var resolver = new PlotPlanResolver();
        var initial = resolver.Resolve(spec, data);
        var bounds = new PlotBounds(0, 0, 1100, 700);
        var plan = resolver.Relayout(spec, data, initial, bounds);
        Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, bounds)), ChartContractSerializer.Serialize(plan));
        Assert.Equal(16, plan.Layers[0].Data.Length);
        Assert.All(plan.Layers[0].Data, datum => Assert.False(datum.IsGap));
        Assert.Equal(2, Paths(plan).Length);
        foreach (var path in Paths(plan)) Assert.Single(Points(path).Distinct());
    }

    [Theory]
    [InlineData("SMOOTH", 0)]
    [InlineData("SMOOTH", 1)]
    [InlineData("SMOOTH", 2)]
    [InlineData("STEP_BEFORE", 0)]
    [InlineData("STEP_BEFORE", 1)]
    [InlineData("STEP_BEFORE", 2)]
    [InlineData("STEP_AFTER", 0)]
    [InlineData("STEP_AFTER", 1)]
    [InlineData("STEP_AFTER", 2)]
    public void EmptySingletonAndTwoVertexRunsPreserveEnvelope(string mode, int count)
    {
        var (spec, data) = Lower(Sql(mode, "IDENTITY", false, false, false));
        data = data with { RowCount = count, Columns = data.Columns.Select(column => column with { Values = column.Values.Take(count).ToImmutableArray(), DisplayValues = [] }).ToImmutableArray() };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(14, plan.Version);
        var paths = Paths(plan);
        if (count < 2) Assert.Empty(paths);
        else
        {
            Assert.DoesNotContain("C", Assert.Single(paths));
            Assert.Equal(mode == "SMOOTH" ? 2 : 3, Points(paths[0]).Length);
        }
        Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
    }

    [Theory]
    [InlineData("SMOOTH")]
    [InlineData("STEP_BEFORE")]
    [InlineData("STEP_AFTER")]
    public async Task AuthoringDesignerLineageAndPdfPreserveInterpolation(string mode)
    {
        var sql = Sql(mode, "JITTER(X = 0.02, Y = 0.03, KEY = Id, SEED = 42)", true, true, true);
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        Assert.Equal(statement.ToSql(), Assert.Single(new Parser(new Lexer(statement.ToSql()).Tokenize(), statement.ToSql()).Parse().Statements).ToSql());
        var source = sql + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Route)));";
        var design = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(design.Error);
        var patched = new DesignerScriptPatcher().Patch(source, design.DesignState);
        Assert.Contains(mode, patched);
        Assert.Empty(new Parser(new Lexer(patched).Tokenize(), patched).Parse().Diagnostics);
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(script);
        foreach (var field in new[] { "Distance", "Estimate", "Id", "Cohort" })
            Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Route" && entry.SourceColumns.Contains(field));
        var (spec, data) = Lower(sql);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var report = new ReportManifest
        {
            Title = "Route",
            Source = "route.rptsql",
            Visuals = [new VisualManifest { Name = "Route", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = new SvgChartRenderer().Render(plan) }]
        };
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await new PdfExporter().ExportAsync(report))[..4]);
    }

    [Theory]
    [InlineData("SMOOTH")]
    [InlineData("STEP_BEFORE")]
    [InlineData("STEP_AFTER")]
    public void RepresentativeWorkloadRetainsExistingBudgets(string mode)
    {
        var (spec, data) = Lower(Sql(mode, "JITTER(X = 0.02, Y = 0.03, KEY = Id, SEED = 42)", true, false, true));
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
                    _ => ChartValue.From((decimal)(2 + row % 16))
                }).ToImmutableArray(),
                DisplayValues = []
            }).ToImmutableArray()
        };
        var before = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 1200, 800));
        var allocation = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "LINE resolution exceeded 5 seconds."); // flaky-time-bound-ok: bounds the established 600-row, 12-panel workload.
        clock.Restart();
        var svg = new SvgChartRenderer().Render(plan);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "LINE rendering exceeded 5 seconds."); // flaky-time-bound-ok: bounds the established 600-row, 12-panel workload.
        Assert.Equal(12, plan.Facets.Length);
        Assert.True(allocation < 16L * 1024 * 1024, $"Resolution allocated {allocation:N0} bytes.");
        Assert.InRange(Encoding.UTF8.GetByteCount(ChartContractSerializer.Serialize(plan)), 1, 6 * 1024 * 1024);
        Assert.InRange(Encoding.UTF8.GetByteCount(svg), 1, 600 * 1024);
    }

    [Theory]
    [InlineData("SMOOTH")]
    [InlineData("STEP_BEFORE")]
    [InlineData("STEP_AFTER")]
    public void ExplicitAxesAndMixedConditionedLayersPreserveEnvelopePrecedence(string mode)
    {
        var sql = Sql(mode, "IDENTITY", false, false, false)
            .Replace("SCALES (", "SCALES (unused = BAND (CHANNEL = X), unusedY = LINEAR (CHANNEL = Y, MIN = 100, MAX = 200),", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var plain = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal("distances", plain.CartesianAxes!.XScaleId);
        var layered = sql.Replace("LAYERS (route =", "LAYERS (conditioned = LINE (NULL_HANDLING = GAP, ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = distances), Y = Estimate (TYPE = QUANTITATIVE, SCALE = estimates)), STYLE (INTERPOLATION = 'SMOOTH', COLOR = '#445566'), CONDITIONS (COLOR WHEN Estimate > 5 THEN '#ff0000' ELSE '#0000ff')), route =", StringComparison.Ordinal);
        var (mixedSpec, mixedData) = Lower(layered);
        var mixed = new PlotPlanResolver().Resolve(mixedSpec, mixedData);
        Assert.Equal(11, mixedSpec.Version);
        Assert.Equal(14, mixed.Version);
        Assert.Single(mixed.Layers, layer => layer.ConnectionInterpolation is not null);
        Assert.Single(mixed.Layers, layer => layer.PathInterpolation is not null);
        Assert.Equal(Paths(plain), Paths(mixed));
        Assert.Equal(ChartContractSerializer.Serialize(mixed), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(mixed))));
    }

    [Theory]
    [InlineData("SMOOTH", "22E5C8541CDDD00D0131CEFE9ACD1598238634827A0E6A2AF58414F19471EAF1", "71BBC6E0C782DCB5229BCB10AEF93FC003F5976151DFB2485FFC60F26AB36E04", "24CACC3D1DE4A6F5D905815D45A65B36642DA4747267B37050CD3FC704565E4F")]
    [InlineData("STEP_BEFORE", "3E074A43FFBBFF6EE3C4DEF6003B7BD2670B3F6C0BAFF93D3E598707C8619F6A", "47D15BE656EF3A729F32D7FC8A1BD61A8D1425711F3CF3E5562B23622CC8885C", "130B596A210B2DF11949C47A1022195506D5863AAE03A0FEFAF517761391FDC5")]
    [InlineData("STEP_AFTER", "57BEED3B376AAC9B25202F1F7C6256F793C88FA043973519869DD32F21DEB796", "059FB6BF4B8DEDE85BD14141E9D54AE35E4DE790D9BFE53BDB7445186D92DD61", "20D055B480C6ED087323EC3B629D41B22756511267AFE1CAC233750EA673BD99")]
    public void PlanSvgAndTerminalMatchDeterministicFixtures(string mode, string expectedPlan, string expectedSvg, string expectedTerminal)
    {
        var (spec, data) = Lower(Sql(mode, "JITTER(X = 0.02, Y = 0.03, KEY = Id, SEED = 42)", true, true, true));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        var terminalHash = Hash(TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 140).NormalizedText);
        Assert.True(planHash == expectedPlan && svgHash == expectedSvg && terminalHash == expectedTerminal,
            $"{mode}: plan {planHash}; svg {svgHash}; terminal {terminalHash}");
    }

    [Fact]
    public void PublishedSmoothExampleParsesAndAllMissingRowsKeepIntent()
    {
        var path = Path.Combine(TerminalSnapshotHarness.GetRepoRoot(), "docs", "reference", "visuals-reporting", "visuals", "chart.md");
        var sql = Regex.Matches(File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal), @"```sql\n([\s\S]*?)```")
            .Select(match => match.Groups[1].Value).Single(block => block.Contains("CREATE VISUAL SmoothRoute", StringComparison.Ordinal));
        var (spec, data) = Lower(sql);
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == "Estimate" ? column with
            { Values = Enumerable.Repeat(ChartValue.Null(), data.RowCount).ToImmutableArray(), DisplayValues = [] } : column).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(14, plan.Version);
        Assert.Empty(Paths(plan));
        Assert.All(plan.Layers[0].Data, datum => Assert.True(datum.IsGap));
        Assert.Contains("SMOOTH", TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 140).NormalizedText);
    }

    private static string[] Paths(PlotPlan plan) => XDocument.Parse(new SvgChartRenderer().Render(plan)).Descendants()
        .Where(element => element.Name.LocalName == "path" && (string?)element.Attribute("stroke") == "#112233" && (string?)element.Attribute("fill") == "none")
        .Select(element => element.Attribute("d")!.Value).ToArray();

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
            Name = "Route",
            Columns = ["Distance", "Estimate", "Cohort", "Id"],
            Rows = Enumerable.Range(0, sql.Contains("FACET", StringComparison.Ordinal) ? 16 : 8).Select(index => new List<string?>
            {
                (index % 8 + 2).ToString(CultureInfo.InvariantCulture), index % 8 == 3 ? null : (new[] { 3, 14, 5, 4, 15, 6, 12, 8 }[index % 8]).ToString(CultureInfo.InvariantCulture),
                index < 8 ? "A" : "B", "K" + index.ToString(CultureInfo.InvariantCulture)
            }).ToList()
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
