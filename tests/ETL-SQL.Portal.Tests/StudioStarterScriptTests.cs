using System.Text.RegularExpressions;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Parses the MOCKDB starter scripts that Studio Home's "Start with sample data" seeds.
///
/// <para>These exist because a first session was otherwise a dead end: the visual palette stays
/// disabled until a data sample exists, and a sample needs a connection a newcomer does not have.
/// They are the first ETL-SQL a new author ever reads, so a starter that does not parse would teach
/// the wrong syntax and break the canvas on arrival — and they live in a JavaScript string literal
/// where no compiler would notice.</para>
/// </summary>
[Trait("Category", "Portal")]
public sealed class StudioStarterScriptTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ETL-SQL.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>Extracts the starter scripts from their backtick literals in studio.js.</summary>
    public static TheoryData<string, string> Starters()
    {
        var studioJs = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "ETL-SQL.ReportRuntime", "Resources", "Shared", "designer", "studio.js"));

        var table = Regex.Match(
            studioJs,
            @"const\s+STUDIO_STARTER_SCRIPTS\s*=\s*Object\.freeze\(\{(?<body>.*?)\}\);",
            RegexOptions.Singleline);
        Assert.True(table.Success, "STUDIO_STARTER_SCRIPTS was not found in studio.js.");

        var data = new TheoryData<string, string>();
        foreach (Match entry in Regex.Matches(
            table.Groups["body"].Value,
            @"(?<name>\w+):\s*`(?<script>[^`]*)`",
            RegexOptions.Singleline))
        {
            data.Add(entry.Groups["name"].Value, entry.Groups["script"].Value);
        }

        Assert.True(data.Count >= 3, "Expected a starter script per Studio Home creation action.");
        return data;
    }

    [Theory]
    [MemberData(nameof(Starters))]
    public void StarterScript_ParsesWithoutError(string name, string script)
    {
        var parsed = new CoreParser(new Lexer(script).Tokenize(), script).Parse();
        var errors = parsed.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => $"line {diagnostic.Line}: {diagnostic.Message}")
            .ToList();

        Assert.True(errors.Count == 0, $"Starter script '{name}' does not parse: {string.Join("; ", errors)}");
    }

    [Theory]
    [MemberData(nameof(Starters))]
    public void StarterScript_UsesOnlyTheBuiltInSampleConnector(string name, string script)
    {
        // The whole point is a first run with no external dependency; a starter that reached for a
        // real database would reintroduce the dead end it exists to remove.
        Assert.Contains("MOCKDB()", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PASSWORD", script, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(name));
    }
}
