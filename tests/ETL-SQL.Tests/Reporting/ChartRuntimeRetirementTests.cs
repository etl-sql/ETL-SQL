using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

public sealed class ChartRuntimeRetirementTests
{
    [Fact]
    public void RetiredChartRuntime_HasNoPackagesAssetsOrProductionConsumers()
    {
        var root = RepoRoot();
        Assert.DoesNotContain("Microsoft.ClearScript", File.ReadAllText(Path.Combine(root, "Directory.Packages.props")), StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(root, "src", "ETL-SQL.ReportRuntime", "Resources", "Shared", "echarts.min.js")));
        Assert.False(File.Exists(Path.Combine(root, "src", "ETL-SQL.Reporting", "EChartsSsrRenderer.cs")));
        Assert.False(File.Exists(Path.Combine(root, "src", "ETL-SQL.Reporting", "EChartsRenderer.cs")));
        Assert.False(File.Exists(Path.Combine(root, "src", "ETL-SQL.Reporting", "Renderers", "PlotPlanEChartsRenderer.cs")));

        var scanRoots = new[] { Path.Combine(root, "src"), Path.Combine(root, "tools", "ui-sandbox"), Path.Combine(root, "scripts") };
        var productionFiles = scanRoots.SelectMany(scanRoot => Directory.EnumerateFiles(scanRoot, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true
        }))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}") &&
                           Path.GetExtension(path) is ".cs" or ".js" or ".mjs" or ".ts" or ".tsx" or ".html" or ".csproj")
            .ToList();
        var consumers = productionFiles.Where(path =>
        {
            var text = File.ReadAllText(path);
            return text.Contains("Microsoft.ClearScript", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("echarts.min.js", StringComparison.OrdinalIgnoreCase) ||
                   Regex.IsMatch(text, @"(?<![A-Za-z0-9_$])echarts\.init\s*\(", RegexOptions.IgnoreCase);
        }).Select(path => Path.GetRelativePath(root, path)).ToList();
        Assert.True(consumers.Count == 0, string.Join(Environment.NewLine, consumers));
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
