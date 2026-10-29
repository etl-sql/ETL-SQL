using System;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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
        var productionFiles = scanRoots.SelectMany(ProductionFiles).ToList();
        var consumers = productionFiles.Where(path =>
        {
            var text = File.ReadAllText(path);
            return text.Contains("Microsoft.ClearScript", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("echarts.min.js", StringComparison.OrdinalIgnoreCase) ||
                   Regex.IsMatch(text, @"(?<![A-Za-z0-9_$])echarts\.init\s*\(", RegexOptions.IgnoreCase);
        }).Select(path => Path.GetRelativePath(root, path)).ToList();
        Assert.True(consumers.Count == 0, string.Join(Environment.NewLine, consumers));
    }

    [Fact]
    public async Task SourceScan_PreservesNestedAndUntrackedSourcesWhilePruningOnlyExcludedDirectories()
    {
        var temporaryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        var root = Path.Combine(temporaryRoot, "chart-retirement-" + Guid.NewGuid().ToString("N"));
        var included = new[] { "new/deep/source.ts", "node_modules-copy/vendor.js", "obj-copy/project.csproj" };
        var excluded = new[] { "bin/deep/output.js", "nested/obj/output.cs", "node_modules/nested/vendor.js", "nested/readme.md" };
        try
        {
            foreach (var relative in included.Concat(excluded))
            {
                var file = Path.Combine(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await File.WriteAllTextAsync(file, "audit fixture", CancellationToken.None);
            }
            var actual = ProductionFiles(root).Select(file => Path.GetRelativePath(root, file).Replace('\\', '/')).Order().ToArray();
            Assert.Equal(included.Order(), actual);
        }
        finally
        {
            Assert.Equal(temporaryRoot, Path.GetDirectoryName(Path.GetFullPath(root)));
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static System.Collections.Generic.IEnumerable<string> ProductionFiles(string root)
    {
        var files = new FileSystemEnumerable<string>(root, (ref FileSystemEntry entry) => entry.ToFullPath(), new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true
        })
        {
            ShouldRecursePredicate = (ref FileSystemEntry entry) => entry.FileName is not ("bin" or "obj" or "node_modules"),
            ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory
        };
        return files.Where(path => Path.GetExtension(path) is ".cs" or ".js" or ".mjs" or ".ts" or ".tsx" or ".html" or ".csproj");
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
