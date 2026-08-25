using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

/// <summary>
/// Governance for the Portal-only operational chart adapter.
///
/// `native-charts.js` is a second, deliberately separate charting implementation: it draws the
/// orchestrator page's Gantt, sparkline, and dependency graph, and it is not a shared report runtime
/// asset. That separation is the decision — see docs/architecture/portal-ui.md — so these tests do
/// not try to merge it into `PlotPlan` or into the Report-SQL capability matrix. They keep it inside
/// its own boundary and hold it to its own ownership, dependency/license, accessibility, behavioural,
/// and footprint gates, which is what it was missing.
/// </summary>
public sealed class PortalOperationalChartAssetTests
{
    /// <summary>Raw budget. It is an adapter; a charting library would not fit.</summary>
    private const int MaxRawBytes = 8 * 1024;

    /// <summary>Gzip budget for the same asset.</summary>
    private const int MaxGzipBytes = 3 * 1024;

    [Fact]
    [Trait("Category", "Smoke.Reporting")]
    public void OperationalChartAsset_DeclaresItsOwnershipBoundary()
    {
        var source = Source();

        Assert.StartsWith("/*!", source, StringComparison.Ordinal);
        Assert.Contains("Owner: ETL-SQL Report Portal", source);
        Assert.Contains("Not a shared report runtime asset", source);
        Assert.Contains("NOT a PlotPlan consumer", source);
        Assert.Contains("docs/architecture/portal-ui.md", source);
    }

    [Fact]
    [Trait("Category", "Smoke.Reporting")]
    public void OperationalChartAsset_StaysOutsideTheSharedRuntimeAndItsSync()
    {
        var root = RepoRoot();

        Assert.False(File.Exists(Path.Combine(root, "src", "ETL-SQL.ReportRuntime", "Resources", "Shared", "native-charts.js")),
            "native-charts.js must not be copied into the shared report runtime.");

        var sync = File.ReadAllText(Path.Combine(root, "scripts", "sync-assets.js"));
        Assert.DoesNotContain("native-charts", sync, StringComparison.OrdinalIgnoreCase);

        var matrix = File.ReadAllText(Path.Combine(root, "src", "ETL-SQL.Reporting", "Baselines", "VisualCapabilityMatrix.cs"));
        Assert.DoesNotContain("native-charts", matrix, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Smoke.Reporting")]
    public void OperationalChartAsset_HasNoThirdPartyDependency()
    {
        var body = WithoutBanner(Source());

        Assert.DoesNotContain("import ", body, StringComparison.Ordinal);
        Assert.DoesNotContain("require(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", body, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", body, StringComparison.Ordinal);
        Assert.DoesNotContain("cdn.", body, StringComparison.OrdinalIgnoreCase);
        // It mimics the ECharts call shape; it must never fall back to a real ECharts global.
        Assert.DoesNotContain("global.echarts", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("window.echarts", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Smoke.Reporting")]
    public void OperationalChartAsset_RendersAnAccessibleImage()
    {
        var source = Source();

        Assert.Contains("role=\"img\"", source);
        Assert.Contains("aria-label=\"${esc(label)}\"", source);
        // The label must come from the caller's option, not be a hardcoded string for every chart.
        Assert.Contains("option.title?.text", source);
        Assert.DoesNotContain("aria-label=\"Native chart\"", source);
    }

    [Fact]
    [Trait("Category", "Smoke.Reporting")]
    public void OperationalChartAsset_KeepsTheShimSurfaceTheOrchestratorPageCalls()
    {
        var source = Source();
        foreach (var member in new[] { "setOption", "resize()", "dispose()", "on(name, callback)", "dispatchAction()", "getInstanceByDom" })
            Assert.Contains(member, source);

        // Every interpolated value reaches innerHTML, so nothing may bypass the escaper.
        Assert.Contains("const esc =", source);
        foreach (var interpolation in Regex.Matches(source, @"\$\{(?<expr>[^}]+)\}").Cast<Match>())
        {
            var expr = interpolation.Groups["expr"].Value;
            var isNumeric = expr.StartsWith("width", StringComparison.Ordinal)
                || expr.StartsWith("height", StringComparison.Ordinal)
                || expr.StartsWith("index", StringComparison.Ordinal)
                || expr.StartsWith("marks", StringComparison.Ordinal)
                || expr.StartsWith("pad", StringComparison.Ordinal)
                || expr.StartsWith("slot", StringComparison.Ordinal)
                || expr.StartsWith("h", StringComparison.Ordinal)
                || expr.StartsWith("a[", StringComparison.Ordinal)
                || expr.StartsWith("b[", StringComparison.Ordinal)
                || expr.StartsWith("p[", StringComparison.Ordinal);
            Assert.True(isNumeric || expr.StartsWith("esc(", StringComparison.Ordinal),
                $"Unescaped interpolation in native-charts.js: ${{{expr}}}");
        }
    }

    [Fact]
    [Trait("Category", "Smoke.Reporting")]
    public void OperationalChartAsset_StaysWithinItsFootprintBudget()
    {
        var bytes = File.ReadAllBytes(AssetPath());

        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(bytes, 0, bytes.Length);

        Assert.True(bytes.Length <= MaxRawBytes,
            $"native-charts.js is {bytes.Length:N0} B raw, over the {MaxRawBytes:N0} B operational-UI budget. " +
            "An adapter that has grown into a charting library belongs in a design discussion, not a size bump.");
        Assert.True(buffer.Length <= MaxGzipBytes,
            $"native-charts.js is {buffer.Length:N0} B gzip, over the {MaxGzipBytes:N0} B operational-UI budget.");
    }

    [Fact]
    [Trait("Category", "Smoke.Reporting")]
    public void OrchestratorPage_IsTheOnlyConsumer()
    {
        var root = RepoRoot();
        var wwwroot = Path.Combine(root, "src", "ETL-SQL.Portal", "wwwroot");
        var referencing = Directory.EnumerateFiles(wwwroot, "*.html", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("native-charts.js", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .Order()
            .ToList();

        Assert.Equal(["orchestrator.html"], referencing);
    }

    private static string AssetPath() =>
        Path.Combine(RepoRoot(), "src", "ETL-SQL.Portal", "wwwroot", "js", "native-charts.js");

    private static string Source() => File.ReadAllText(AssetPath()).Replace("\r\n", "\n");

    private static string WithoutBanner(string source)
    {
        var end = source.IndexOf("*/", StringComparison.Ordinal);
        return end < 0 ? source : source[(end + 2)..];
    }

    private static string RepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "ETL-SQL.slnx")) || Directory.Exists(Path.Combine(current, ".git")))
                return current;
            current = Path.GetDirectoryName(current);
        }
        return Directory.GetCurrentDirectory();
    }
}
