using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Reporting.Baselines;
using ETL_SQL.Tests.Reporting.Baselines;
using Xunit;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Tests.Reporting;

public class ReportingBaselineTests
{
    private static readonly Lazy<Task<IReadOnlyList<FixtureMeasurement>>> FixtureMeasurements =
        new(() => ReportingBaselineMeasurementHarness.MeasureRepresentativeFixturesAsync(GetRepoRoot()));

    private static string GetRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "ETL-SQL.slnx")) || Directory.Exists(Path.Combine(current, ".git")))
            {
                return current;
            }
            current = Path.GetDirectoryName(current);
        }
        return Directory.GetCurrentDirectory();
    }

    [Fact]
    public void CapabilityMatrix_CoversEveryVisualTypeEnumMember()
    {
        var allEnumValues = Enum.GetValues<VisualType>();
        var matrixEntries = VisualCapabilityMatrix.AllCapabilities;

        Assert.Equal(allEnumValues.Length, matrixEntries.Count);

        foreach (var type in allEnumValues)
        {
            var entry = matrixEntries.FirstOrDefault(e => e.Type == type);
            Assert.NotNull(entry);
            Assert.False(string.IsNullOrWhiteSpace(entry.Name));
            Assert.False(string.IsNullOrWhiteSpace(entry.Category));
            Assert.False(string.IsNullOrWhiteSpace(entry.Browser.Implementation));
            Assert.False(string.IsNullOrWhiteSpace(entry.StaticExport.Implementation));
            Assert.False(string.IsNullOrWhiteSpace(entry.PdfEmailExport.Implementation));
            Assert.False(string.IsNullOrWhiteSpace(entry.Terminal.Implementation));
        }
    }

    [Fact]
    public void CapabilityMatrix_TracksSourceBackedRendererBoundaries()
    {
        var nativeSvg = new[]
        {
            VisualType.Bar, VisualType.HorizontalBar, VisualType.Line, VisualType.Scatter,
            VisualType.Pie, VisualType.Donut, VisualType.Combo
        };
        Assert.Equal(nativeSvg.Order(), VisualCapabilityMatrix.NativeSvgVisualTypes.Order());

        Assert.Equal(23, VisualCapabilityMatrix.EChartsVisualTypes.Count);
        Assert.Equal(CapabilityLevel.Native, VisualCapabilityMatrix.Get(VisualType.Matrix).Browser.Level);
        Assert.Equal(CapabilityLevel.TemporaryDependency, VisualCapabilityMatrix.Get(VisualType.Matrix).StaticExport.Level);
        Assert.Equal(CapabilityLevel.SemanticFallback, VisualCapabilityMatrix.Get(VisualType.Map).Terminal.Level);
        Assert.Equal(CapabilityLevel.Native, VisualCapabilityMatrix.Get(VisualType.Gantt).Terminal.Level);
        Assert.Equal(CapabilityLevel.Unsupported, VisualCapabilityMatrix.Get(VisualType.Slicer).StaticExport.Level);
    }

    [Fact]
    public void CapabilityMatrix_EveryGraphicalVisualHasUsefulTerminalPath()
    {
        foreach (var type in VisualCapabilityMatrix.EChartsVisualTypes)
        {
            var terminal = VisualCapabilityMatrix.Get(type).Terminal;
            Assert.NotEqual(CapabilityLevel.Unsupported, terminal.Level);
            Assert.DoesNotContain("placeholder", terminal.Implementation, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(terminal.Implementation));
        }

        Assert.Contains("ranked regional", VisualCapabilityMatrix.Get(VisualType.Map).Terminal.Implementation);
        Assert.Contains("drop-off", VisualCapabilityMatrix.Get(VisualType.Sankey).Terminal.Implementation);
        Assert.Contains("hierarchy", VisualCapabilityMatrix.Get(VisualType.Sunburst).Terminal.Implementation);
        Assert.Contains("node-degree", VisualCapabilityMatrix.Get(VisualType.Network).Terminal.Implementation);
    }

    [Theory]
    [InlineData("bar_category_revenue.rptsql", "Bar")]
    [InlineData("line_timeseries_trend.rptsql", "Line")]
    [InlineData("scatter_correlation.rptsql", "Scatter")]
    [InlineData("donut_market_share.rptsql", "Donut")]
    [InlineData("combo_revenue_margin.rptsql", "Combo")]
    [InlineData("bar_with_goal_rule.rptsql", "Bar")]
    public async Task RepresentativeFixtures_DiscoverParseAndEvaluateSuccessfully(string fixtureFileName, string expectedVisualType)
    {
        var repoRoot = GetRepoRoot();
        var fixturePath = Path.Combine(repoRoot, "tests", "fixtures", "reporting", "representative", fixtureFileName);

        Assert.True(File.Exists(fixturePath), $"Fixture file '{fixtureFileName}' must exist at {fixturePath}");

        var script = await File.ReadAllTextAsync(fixturePath);
        Assert.False(string.IsNullOrWhiteSpace(script));

        var tokens = new Lexer(script).Tokenize();
        var ast = new CoreParser(tokens, script).Parse();

        Assert.NotNull(ast);
        Assert.NotEmpty(ast.Statements);
        Assert.DoesNotContain(ast.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var measurements = await FixtureMeasurements.Value;
        var fixtureName = Path.GetFileNameWithoutExtension(fixtureFileName);
        var fixtureMeasure = measurements.FirstOrDefault(m => m.FixtureName.Equals(fixtureName, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(fixtureMeasure);
        Assert.Equal(expectedVisualType.ToUpperInvariant(), fixtureMeasure.VisualType);
        Assert.True(fixtureMeasure.FixtureBuildMs > 0);
        Assert.True(fixtureMeasure.ManifestJsonBytes > 0);
    }

    [Fact]
    public async Task RuleAnnotationFixture_CorrectlyPreservesOverlaysInManifest()
    {
        var repoRoot = GetRepoRoot();
        var fixturePath = Path.Combine(repoRoot, "tests", "fixtures", "reporting", "representative", "bar_with_goal_rule.rptsql");

        var script = await File.ReadAllTextAsync(fixturePath);
        var tokens = new Lexer(script).Tokenize();
        var ast = new CoreParser(tokens, script).Parse();

        var visualStmt = ast.Statements.OfType<CreateVisualStatement>().FirstOrDefault();
        Assert.NotNull(visualStmt);
        Assert.NotNull(visualStmt.Overlays);
        Assert.Equal(3, visualStmt.Overlays.Count);

        var overlayTypes = visualStmt.Overlays.Select(o => o.OverlayType).ToList();
        Assert.Contains(OverlayType.Goal, overlayTypes);
        Assert.Contains(OverlayType.Average, overlayTypes);
        Assert.Contains(OverlayType.MovingAvg, overlayTypes);
    }

    [Fact]
    public void BundleAssetMeasurements_CalculatesSizesAndCompressionRatios()
    {
        var repoRoot = GetRepoRoot();
        var bundleAssets = ReportingBaselineMeasurementHarness.MeasureBundleAssets(repoRoot);

        Assert.NotEmpty(bundleAssets);

        var echarts = bundleAssets.FirstOrDefault(b => b.RelativePath.EndsWith("echarts.min.js", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(echarts);
        Assert.True(echarts.RawBytes > 100_000, "ECharts bundle size must exceed 100KB");
        Assert.True(echarts.GzipBytes < echarts.RawBytes, "Gzip size must be smaller than raw size");
        Assert.True(echarts.BrotliBytes < echarts.RawBytes, "Brotli size must be smaller than raw size");

        var runtimeJs = bundleAssets.FirstOrDefault(b => b.RelativePath.EndsWith("report-runtime.js", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(runtimeJs);
        Assert.True(runtimeJs.RawBytes > 0);
    }

    [Fact]
    public async Task FullBaselineHarness_RunsAndGeneratesMarkdownAndJsonReports()
    {
        var repoRoot = GetRepoRoot();
        var report = await ReportingBaselineMeasurementHarness.RunFullBaselineAsync(repoRoot);

        Assert.NotNull(report);
        Assert.NotEmpty(report.BundleAssets);
        Assert.NotEmpty(report.FixtureMeasurements);
        Assert.Equal(Enum.GetValues<VisualType>().Length, report.CapabilityMatrix.Count);

        var md = ReportingBaselineMeasurementHarness.FormatMarkdownReport(report);
        Assert.Contains("Phase 2 Reporting & Visuals Baseline Report", md);
        Assert.Contains("Visual Capability Matrix", md);
        Assert.Contains("bar_category_revenue", md);

        var outputDir = Environment.GetEnvironmentVariable("ETLSQL_REPORT_BASELINE_OUTPUT_DIR");
        if (!string.IsNullOrWhiteSpace(outputDir))
        {
            Directory.CreateDirectory(outputDir);
            await File.WriteAllTextAsync(Path.Combine(outputDir, "reporting-phase2-baselines.md"), md);
            await File.WriteAllTextAsync(
                Path.Combine(outputDir, "reporting-phase2-baselines.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters = { new JsonStringEnumConverter() }
                }));
        }
    }
}
