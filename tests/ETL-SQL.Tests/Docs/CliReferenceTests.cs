using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace ETL_SQL.Tests.Docs;

/// <summary>
/// Keeps the generated CLI reference (docs/reference/cli/**) in sync with the command tree. In normal
/// runs this asserts the committed pages match what the generator produces; run with the environment
/// variable <c>ETLSQL_REGEN_CLI_DOCS=1</c> to (re)write the pages after changing a command definition.
/// </summary>
[Trait("Category", "Docs")]
public sealed class CliReferenceTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void CliReference_MatchesCommandTree()
    {
        var expected = CliReferenceGenerator.Generate();
        var regenerate = Environment.GetEnvironmentVariable("ETLSQL_REGEN_CLI_DOCS") == "1";

        var cliDir = Path.Combine(RepoRoot, CliReferenceGenerator.CliDir);
        if (regenerate)
        {
            Directory.CreateDirectory(cliDir);
            var expectedFull = expected.Keys
                .Select(k => Path.GetFullPath(Path.Combine(RepoRoot, k.Replace('/', Path.DirectorySeparatorChar))))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(cliDir))
            {
                foreach (var f in Directory.EnumerateFiles(cliDir, "*.md"))
                {
                    if (!expectedFull.Contains(Path.GetFullPath(f)))
                        File.Delete(f);
                }
            }
            foreach (var (rel, content) in expected)
            {
                var full = Path.Combine(RepoRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, content);
            }
            return;
        }

        var problems = new List<string>();

        foreach (var (rel, content) in expected)
        {
            var full = Path.Combine(RepoRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full))
                problems.Add($"missing: {rel}");
            else if (Normalize(File.ReadAllText(full)) != Normalize(content))
                problems.Add($"out of date: {rel}");
        }

        // Stale pages: committed cli pages the generator no longer produces.
        if (Directory.Exists(cliDir))
        {
            var expectedFull = expected.Keys
                .Select(k => Path.GetFullPath(Path.Combine(RepoRoot, k.Replace('/', Path.DirectorySeparatorChar))))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Directory.EnumerateFiles(cliDir, "*.md"))
                if (!expectedFull.Contains(Path.GetFullPath(f)))
                    problems.Add($"stale (regenerate to remove): {Path.GetRelativePath(RepoRoot, f).Replace('\\', '/')}");
        }

        Assert.True(problems.Count == 0,
            "CLI reference is out of sync with the command tree. Regenerate with "
            + "`ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter FullyQualifiedName~CliReferenceTests`:\n"
            + string.Join("\n", problems.Select(p => "  " + p)));
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd('\n');
}
