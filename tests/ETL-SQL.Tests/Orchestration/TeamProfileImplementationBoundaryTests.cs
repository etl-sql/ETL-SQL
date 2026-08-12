using System.Text.RegularExpressions;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

/// <summary>
/// Team is a supported provider configuration of the common product, never a code fork. These
/// source-boundary checks deliberately cover the runtime and UI ownership roots where a Team-only
/// parser, evaluator, connector, catalog, checkpoint, promotion model, or Portal implementation
/// could otherwise appear unnoticed.
/// </summary>
public sealed class TeamProfileImplementationBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string[] RuntimeRoots =
    {
        "src/ETL-SQL.Core",
        "src/ETL-SQL.Engine",
        "src/ETL-SQL.Connectors",
        "src/ETL-SQL.Connectors.Common",
        "src/ETL-SQL.Connectors.Cloud",
        "src/ETL-SQL.Connectors.Databases",
        "src/ETL-SQL.Connectors.Files",
        "src/ETL-SQL.Connectors.Messaging",
        "src/ETL-SQL.Connectors.Remote",
        "src/ETL-SQL.Orchestrator",
        "src/ETL-SQL.Portal",
        "src/ETL-SQL.Reporting",
        "src/ETL-SQL.ReportRuntime"
    };

    [Fact]
    public void RuntimeAndUiContainNoTeamNamedImplementation()
    {
        var namedFiles = RuntimeRoots
            .SelectMany(root => Directory.EnumerateFiles(Path.Combine(RepositoryRoot, root), "*", SearchOption.AllDirectories))
            .Where(path => Path.GetFileName(path).Contains("Team", StringComparison.OrdinalIgnoreCase))
            .Select(Relative)
            .ToArray();

        Assert.True(namedFiles.Length == 0,
            "Team-only implementation files are forbidden; use common providers instead:\n" + string.Join("\n", namedFiles));
    }

    [Fact]
    public void RuntimeSourceDoesNotBranchOnTeamProfile()
    {
        var sourceFiles = RuntimeRoots.SelectMany(root =>
            Directory.EnumerateFiles(Path.Combine(RepositoryRoot, root), "*.cs", SearchOption.AllDirectories));
        var findings = new List<string>();
        var branchPattern = new Regex(
            @"\b(if|else\s+if|case|switch)\b[^\r\n]*(DeploymentProfile\s*\.\s*Team|[\""']Team[\""'])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (var path in sourceFiles)
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(path))
            {
                lineNumber++;
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
                if (branchPattern.IsMatch(line)) findings.Add($"{Relative(path)}:{lineNumber}: {trimmed}");
            }
        }

        Assert.True(findings.Count == 0,
            "Team must select common providers through configuration, not runtime profile branches:\n" + string.Join("\n", findings));
    }

    private static string Relative(string path) => Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/');

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ETL-SQL.slnx"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate ETL-SQL.slnx.");
    }
}
