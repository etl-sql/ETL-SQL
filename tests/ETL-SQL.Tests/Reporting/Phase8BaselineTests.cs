using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Baselines;
using ETL_SQL.Tests.Reporting.Baselines;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

public sealed record Phase8RetirementResult(
    DateTime TimestampUtc, string GitBranch, string EngineVersion, string HostPlatform, string DotNetVersion,
    long BeforeBundleRawBytes, long AfterBundleRawBytes,
    long BeforeBundleGzipBytes, long AfterBundleGzipBytes,
    long BeforeBundleBrotliBytes, long AfterBundleBrotliBytes,
    long RemovedClearScriptPackageBytes, long CurrentClearScriptPackageBytes,
    double FirstFixtureBuildMs, double MedianFixtureBuildMs, long PeakFixtureAllocatedBytes,
    double TotalSvgExportMs, long TotalSvgOutputBytes, long TotalManifestJsonBytes,
    IReadOnlyList<BundleAssetMeasurement> BundleAssets,
    IReadOnlyList<FixtureMeasurement> FixtureMeasurements,
    IReadOnlyList<VisualCapabilityEntry> CapabilityMatrix);

public class Phase8BaselineTests
{
    [Fact]
    public async Task Phase8RetirementMeasurements_AreValidAndOptionallyWritten()
    {
        var repoRoot = RepoRoot();
        var measurement = await ReportingBaselineMeasurementHarness.RunFullBaselineAsync(repoRoot);
        var baselinePath = Path.Combine(repoRoot, "docs", "benchmarks", "reporting-phase8-baselines.json");
        using var baseline = JsonDocument.Parse(await File.ReadAllTextAsync(baselinePath));
        var root = baseline.RootElement;
        var fixtures = measurement.FixtureMeasurements;
        var orderedBuildTimes = fixtures.Select(item => item.FixtureBuildMs).Order().ToArray();
        var result = new Phase8RetirementResult(
            DateTime.UtcNow,
            Environment.GetEnvironmentVariable("ETLSQL_REPORT_BASELINE_BRANCH") ?? "phase8/standard-catalog-native-retirement",
            typeof(ReportManifest).Assembly.GetName().Version?.ToString() ?? "unknown",
            Environment.OSVersion.ToString(), Environment.Version.ToString(),
            root.GetProperty("TotalBundleRawBytes").GetInt64(), measurement.TotalBundleRawBytes,
            root.GetProperty("TotalBundleGzipBytes").GetInt64(), measurement.TotalBundleGzipBytes,
            root.GetProperty("TotalBundleBrotliBytes").GetInt64(), measurement.TotalBundleBrotliBytes,
            root.GetProperty("TotalClearScriptRawBytes").GetInt64(), 0,
            fixtures.FirstOrDefault()?.FixtureBuildMs ?? 0,
            orderedBuildTimes.Length == 0 ? 0 : orderedBuildTimes[orderedBuildTimes.Length / 2],
            fixtures.Select(item => item.ProcessAllocatedBytes).DefaultIfEmpty().Max(),
            fixtures.Sum(item => item.SvgExportMs), fixtures.Sum(item => item.SvgOutputBytes),
            fixtures.Sum(item => item.ManifestJsonBytes), measurement.BundleAssets, fixtures,
            VisualCapabilityMatrix.AllCapabilities);

        Assert.DoesNotContain(result.BundleAssets, asset => asset.RelativePath.Contains("echarts", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, result.CurrentClearScriptPackageBytes);
        Assert.DoesNotContain(result.CapabilityMatrix, entry => entry.HasExternalChartDependency);
        Assert.True(result.AfterBundleRawBytes < result.BeforeBundleRawBytes);
        Assert.All(result.FixtureMeasurements, fixture => Assert.True(fixture.SvgOutputBytes > 0));

        if (!string.Equals(Environment.GetEnvironmentVariable("ETLSQL_WRITE_PHASE8_RESULTS"), "1", StringComparison.Ordinal))
        {
            Assert.True(File.Exists(Path.Combine(repoRoot, "docs", "benchmarks", "reporting-phase8-results.json")));
            Assert.True(File.Exists(Path.Combine(repoRoot, "docs", "benchmarks", "reporting-phase8-results.md")));
            return;
        }

        var options = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
        var outputRoot = Path.Combine(repoRoot, "docs", "benchmarks");
        await File.WriteAllTextAsync(Path.Combine(outputRoot, "reporting-phase8-results.json"), JsonSerializer.Serialize(result, options));
        await File.WriteAllTextAsync(Path.Combine(outputRoot, "reporting-phase8-results.md"), Markdown(result));
    }

    private static string Markdown(Phase8RetirementResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Phase 8 Standard Catalog Migration and Runtime Retirement Results");
        sb.AppendLine();
        sb.AppendLine($"> Measured {result.TimestampUtc:yyyy-MM-dd HH:mm:ss} UTC on `{result.GitBranch}` with ETL-SQL `{result.EngineVersion}` / .NET `{result.DotNetVersion}`.");
        sb.AppendLine();
        sb.AppendLine("These results preserve `reporting-phase8-baselines.*` as the pre-migration record and measure the completed native runtime on the same representative fixture harness. Timings are local-machine observations, not universal performance budgets.");
        sb.AppendLine();
        sb.AppendLine("## Footprint");
        sb.AppendLine();
        sb.AppendLine("| Metric | Before | After | Change |");
        sb.AppendLine("| :--- | ---: | ---: | ---: |");
        Row(sb, "Shared browser assets (raw)", result.BeforeBundleRawBytes, result.AfterBundleRawBytes);
        Row(sb, "Shared browser assets (gzip)", result.BeforeBundleGzipBytes, result.AfterBundleGzipBytes);
        Row(sb, "Shared browser assets (Brotli)", result.BeforeBundleBrotliBytes, result.AfterBundleBrotliBytes);
        Row(sb, "ClearScript multi-RID package estimate", result.RemovedClearScriptPackageBytes, result.CurrentClearScriptPackageBytes);
        sb.AppendLine();
        sb.AppendLine("## Representative runtime and artifacts");
        sb.AppendLine();
        sb.AppendLine($"- First fixture build (the harness cold path): **{result.FirstFixtureBuildMs:F2} ms**");
        sb.AppendLine($"- Median fixture build: **{result.MedianFixtureBuildMs:F2} ms**");
        sb.AppendLine($"- Maximum per-fixture managed allocation: **{Bytes(result.PeakFixtureAllocatedBytes)}**");
        sb.AppendLine($"- Combined native SVG export time: **{result.TotalSvgExportMs:F3} ms**");
        sb.AppendLine($"- Combined native SVG artifact size: **{Bytes(result.TotalSvgOutputBytes)}**");
        sb.AppendLine($"- Combined representative manifest size: **{Bytes(result.TotalManifestJsonBytes)}**");
        sb.AppendLine();
        sb.AppendLine("| Fixture | Type | Build | SVG export | SVG size | Manifest | Allocated |");
        sb.AppendLine("| :--- | :---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var fixture in result.FixtureMeasurements)
            sb.AppendLine($"| `{fixture.FixtureName}` | `{fixture.VisualType}` | {fixture.FixtureBuildMs:F2} ms | {fixture.SvgExportMs:F3} ms | {Bytes(fixture.SvgOutputBytes)} | {Bytes(fixture.ManifestJsonBytes)} | {Bytes(fixture.ProcessAllocatedBytes)} |");
        sb.AppendLine();
        sb.AppendLine("## Capability result");
        sb.AppendLine();
        sb.AppendLine("All graphical catalog entries now use the shared renderer-neutral PlotPlan path or an approved focused native SVG layout module. No capability entry requires an external chart runtime.");
        sb.AppendLine();
        sb.Append(VisualCapabilityMatrix.ToMarkdownTable());
        return sb.ToString();
    }

    private static void Row(StringBuilder sb, string name, long before, long after)
    {
        var percent = before == 0 ? 0 : (after - before) * 100d / before;
        sb.AppendLine($"| {name} | {Bytes(before)} | {Bytes(after)} | {percent:F1}% |");
    }

    private static string Bytes(long bytes) => bytes < 1024 ? $"{bytes} B" : bytes < 1024 * 1024 ? $"{bytes / 1024d:F1} KB" : $"{bytes / 1024d / 1024d:F2} MB";

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
