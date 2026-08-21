using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Interfaces;
using ETL_SQL.Core.Metadata;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Functions;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Engine.Services;
using ETL_SQL.ReportBuilder;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Baselines;
using ETL_SQL.Reporting.Renderers;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Tests.Reporting.Baselines;

public record BundleAssetMeasurement(
    string RelativePath,
    long RawBytes,
    long GzipBytes,
    long BrotliBytes);

public record FixtureMeasurement(
    string FixtureName,
    string VisualType,
    double FixtureBuildMs,
    double MarkdownExportMs,
    long MarkdownOutputBytes,
    double CsvExportMs,
    long CsvOutputBytes,
    double SvgExportMs,
    long SvgOutputBytes,
    long ManifestJsonBytes,
    long ProcessAllocatedBytes,
    string ClientPaintLatency = "N/A (unsupported: requires browser CDP instrumentation)",
    string ClientHeapFootprint = "N/A (unsupported: requires browser CDP instrumentation)");

public record BaselineReport(
    DateTime TimestampUtc,
    string GitBranch,
    string EngineVersion,
    IReadOnlyList<BundleAssetMeasurement> BundleAssets,
    long TotalBundleRawBytes,
    long TotalBundleGzipBytes,
    long TotalBundleBrotliBytes,
    IReadOnlyList<FixtureMeasurement> FixtureMeasurements,
    IReadOnlyList<VisualCapabilityEntry> CapabilityMatrix);

public class ReportingBaselineMeasurementHarness
{
    public static async Task<BaselineReport> RunFullBaselineAsync(string repoRoot)
    {
        var bundleAssets = MeasureBundleAssets(repoRoot);
        var fixtureMeasurements = await MeasureRepresentativeFixturesAsync(repoRoot);

        return new BaselineReport(
            TimestampUtc: DateTime.UtcNow,
            GitBranch: Environment.GetEnvironmentVariable("ETLSQL_REPORT_BASELINE_BRANCH") ?? "unknown",
            EngineVersion: Environment.GetEnvironmentVariable("ETLSQL_REPORT_BASELINE_VERSION")
                ?? typeof(ReportManifest).Assembly.GetName().Version?.ToString()
                ?? "unknown",
            BundleAssets: bundleAssets,
            TotalBundleRawBytes: bundleAssets.Sum(b => b.RawBytes),
            TotalBundleGzipBytes: bundleAssets.Sum(b => b.GzipBytes),
            TotalBundleBrotliBytes: bundleAssets.Sum(b => b.BrotliBytes),
            FixtureMeasurements: fixtureMeasurements,
            CapabilityMatrix: VisualCapabilityMatrix.AllCapabilities);
    }

    public static IReadOnlyList<BundleAssetMeasurement> MeasureBundleAssets(string repoRoot)
    {
        var runtimeDir = Path.Combine(repoRoot, "src", "ETL-SQL.ReportRuntime", "Resources", "Shared");
        if (!Directory.Exists(runtimeDir))
            return Array.Empty<BundleAssetMeasurement>();

        var assetFiles = Directory.GetFiles(runtimeDir, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".geojson", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();

        var results = new List<BundleAssetMeasurement>();

        foreach (var file in assetFiles)
        {
            var bytes = File.ReadAllBytes(file);
            var relPath = Path.GetRelativePath(runtimeDir, file).Replace('\\', '/');

            // Gzip compression
            using var gzMs = new MemoryStream();
            using (var gz = new GZipStream(gzMs, CompressionLevel.Optimal, leaveOpen: true))
            {
                gz.Write(bytes, 0, bytes.Length);
            }
            long gzBytes = gzMs.Length;

            // Brotli compression
            using var brMs = new MemoryStream();
            using (var br = new BrotliStream(brMs, CompressionLevel.Optimal, leaveOpen: true))
            {
                br.Write(bytes, 0, bytes.Length);
            }
            long brBytes = brMs.Length;

            results.Add(new BundleAssetMeasurement(relPath, bytes.Length, gzBytes, brBytes));
        }

        return results;
    }

    public static async Task<IReadOnlyList<FixtureMeasurement>> MeasureRepresentativeFixturesAsync(string repoRoot)
    {
        var fixturesDir = Path.Combine(repoRoot, "tests", "fixtures", "reporting", "representative");
        if (!Directory.Exists(fixturesDir))
            return Array.Empty<FixtureMeasurement>();

        var fixtureFiles = Directory.GetFiles(fixturesDir, "*.rptsql", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f)
            .ToList();

        var results = new List<FixtureMeasurement>();

        foreach (var file in fixtureFiles)
        {
            var fixtureName = Path.GetFileNameWithoutExtension(file);
            var script = await File.ReadAllTextAsync(file);

            // End-to-end fixture build (Lexer -> Parser -> Evaluator -> ManifestBuilder).
            // The first fixture in a fresh test process also includes runtime JIT costs.
            long memBefore = GC.GetTotalAllocatedBytes(precise: false);
            var swCold = Stopwatch.StartNew();

            var tokens = new Lexer(script).Tokenize();
            var ast = new CoreParser(tokens, script).Parse();
            var parserErrors = ast.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            if (parserErrors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Fixture '{fixtureName}' has parser errors: {string.Join("; ", parserErrors.Select(d => d.Message))}");
            }

            var evaluator = CreateBaselineEvaluator();
            await evaluator.Evaluate(ast);

            var manifestBuilder = new ManifestBuilder(evaluator);
            var manifest = await manifestBuilder.BuildAsync(file);
            swCold.Stop();
            long memAfter = GC.GetTotalAllocatedBytes(precise: false);

            // Export measurements
            var swMd = Stopwatch.StartNew();
            var mdContent = new MarkdownRenderer().Render(manifest);
            swMd.Stop();
            var mdBytes = Encoding.UTF8.GetByteCount(mdContent);

            var swCsv = Stopwatch.StartNew();
            var csvContent = new CsvRenderer().Render(manifest);
            swCsv.Stop();
            var csvBytes = Encoding.UTF8.GetByteCount(csvContent);

            // SVG export
            var swSvg = Stopwatch.StartNew();
            var svgSb = new StringBuilder();
            var svgRenderer = new SvgChartRenderer();
            foreach (var v in manifest.Visuals)
            {
                var rendered = svgRenderer.Render(v);
                if (rendered != null)
                {
                    svgSb.Append(rendered);
                }
            }
            swSvg.Stop();
            var svgBytes = Encoding.UTF8.GetByteCount(svgSb.ToString());

            // Manifest JSON size
            var manifestJson = JsonSerializer.Serialize(manifest);
            var manifestBytes = Encoding.UTF8.GetByteCount(manifestJson);

            var visualType = manifest.Visuals.FirstOrDefault()?.VisualType.ToUpperInvariant() ?? "UNKNOWN";

            results.Add(new FixtureMeasurement(
                FixtureName: fixtureName,
                VisualType: visualType,
                FixtureBuildMs: Math.Round(swCold.Elapsed.TotalMilliseconds, 2),
                MarkdownExportMs: Math.Round(swMd.Elapsed.TotalMilliseconds, 3),
                MarkdownOutputBytes: mdBytes,
                CsvExportMs: Math.Round(swCsv.Elapsed.TotalMilliseconds, 3),
                CsvOutputBytes: csvBytes,
                SvgExportMs: Math.Round(swSvg.Elapsed.TotalMilliseconds, 3),
                SvgOutputBytes: svgBytes,
                ManifestJsonBytes: manifestBytes,
                ProcessAllocatedBytes: Math.Max(0, memAfter - memBefore)));
        }

        return results;
    }

    private static Evaluator CreateBaselineEvaluator()
    {
        var services = new ServiceCollection();
        var logger = NullLogger.Instance;
        var sec = new ETL_SQL.Services.SecurityService(logger) { IsTestMode = true };
        var connRegistry = new ConnectorRegistry();
        connRegistry.Register(new ETL_SQL.Connectors.MockDb.MockDbConnector());
        connRegistry.Register(new ETL_SQL.Connectors.FlatFile.FlatFileConnector());

        services.AddSingleton<Common.ILogger>(logger);
        services.AddSingleton(sec);
        services.AddSingleton<IConnectorRegistry>(connRegistry);
        services.AddSingleton<IFunctionRegistry, FunctionRegistry>();
        services.AddSingleton<ILineageTracker, LineageTracker>();
        services.AddSingleton<IDockerManager>(new Mock<IDockerManager>().Object);
        services.AddSingleton<ISessionStateManager>(new SessionStateManager(logger, sec, new Mock<Microsoft.Extensions.Configuration.IConfiguration>().Object, new SqliteSessionMetadataStoreFactory(), null));
        services.AddSingleton<ILanguageHelpRegistry, LanguageHelpRegistry>();
        services.AddSingleton<EvaluatorComponentRegistry>();
        services.AddSingleton<IReportContext, ReportRegistry>();
        services.AddTransient<Evaluator>();

        // Register statement handlers
        var handlers = new[]
        {
            typeof(DeclareStatementHandler),
            typeof(SetVariableStatementHandler),
            typeof(SelectStatementHandler),
            typeof(InsertStatementHandler),
            typeof(ExecutePushdownStatementHandler),
            typeof(CreateTableStatementHandler),
            typeof(CreateConnectionStatementHandler),
            typeof(CreateVisualStatementHandler),
            typeof(CreatePageStatementHandler),
            typeof(CreateDatasetStatementHandler),
            typeof(CreateContainerStatementHandler),
            typeof(CreateNavigationStatementHandler),
            typeof(CreateButtonStatementHandler),
            typeof(CreateStyleStatementHandler),
            typeof(CreateThemeStatementHandler),
            typeof(SetReportMetadataStatementHandler),
            typeof(ExportReportStatementHandler)
        };

        foreach (var h in handlers)
        {
            services.AddTransient(typeof(IStatementHandler), h);
            services.AddTransient(h);
        }

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<Evaluator>();
    }

    public static string FormatMarkdownReport(BaselineReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Phase 2 Reporting & Visuals Baseline Report");
        sb.AppendLine();
        sb.AppendLine($"> **Timestamp (UTC):** {report.TimestampUtc:yyyy-MM-dd HH:mm:ss} | **Branch:** `{report.GitBranch}` | **Engine Version:** `{report.EngineVersion}`");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 1. Browser Runtime Bundle Size Baseline");
        sb.AppendLine();
        sb.AppendLine("Measures physical payload sizes of client-side scripts, CSS styles, and library dependencies shipped in `src/ETL-SQL.ReportRuntime/Resources/Shared/`.");
        sb.AppendLine();
        sb.AppendLine("| Asset | Raw Size | Gzip Size | Brotli Size |");
        sb.AppendLine("| :--- | :---: | :---: | :---: |");

        foreach (var b in report.BundleAssets)
        {
            sb.AppendLine($"| `{b.RelativePath}` | {FormatBytes(b.RawBytes)} | {FormatBytes(b.GzipBytes)} | {FormatBytes(b.BrotliBytes)} |");
        }

        sb.AppendLine($"| **Total Shared Runtime** | **{FormatBytes(report.TotalBundleRawBytes)}** | **{FormatBytes(report.TotalBundleGzipBytes)}** | **{FormatBytes(report.TotalBundleBrotliBytes)}** |");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 2. Representative Visual Fixture Baselines");
        sb.AppendLine();
        sb.AppendLine("Measures end-to-end fixture build time, export throughput (Markdown, CSV, SVG), output payload sizes, and process allocations across the named representative fixtures. The first fixture in a fresh test process includes runtime JIT cost. CSV is 0 B for these chart-only fixtures because the CSV renderer exports tabular visuals only.");
        sb.AppendLine();
        sb.AppendLine("| Fixture | Visual Type | Fixture Build | Markdown Export | CSV Export | SVG Export | Manifest JSON | Process Allocated |");
        sb.AppendLine("| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: |");

        foreach (var f in report.FixtureMeasurements)
        {
            sb.AppendLine($"| `{f.FixtureName}` | `{f.VisualType}` | {f.FixtureBuildMs:F2} ms | {f.MarkdownExportMs:F3} ms ({FormatBytes(f.MarkdownOutputBytes)}) | {f.CsvExportMs:F3} ms ({FormatBytes(f.CsvOutputBytes)}) | {f.SvgExportMs:F3} ms ({FormatBytes(f.SvgOutputBytes)}) | {FormatBytes(f.ManifestJsonBytes)} | {FormatBytes(f.ProcessAllocatedBytes)} |");
        }

        sb.AppendLine();
        sb.AppendLine("### Explicit Client-Side Unsupported Measurements");
        sb.AppendLine("- **Client Browser Paint / V8 Frame Latency**: `N/A (unsupported: requires headless Chrome CDP profiling in browser test runner)`");
        sb.AppendLine("- **Client DOM/ECharts Heap Memory**: `N/A (unsupported: requires browser CDP memory heap snapshots)`");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"## 3. Visual Capability Matrix (All {report.CapabilityMatrix.Count} Visual Types)");
        sb.AppendLine();
        sb.Append(VisualCapabilityMatrix.ToMarkdownTable());

        return sb.ToString();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }
}
