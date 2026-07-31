using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using Xunit;

namespace ETL_SQL.Tests.Compatibility;

public class ScriptCompatibilityCorpusTests
{
    public static IEnumerable<object[]> CanonicalScripts()
    {
        yield return new object[]
        {
            "version-gate",
            "REQUIRE VERSION >= '0.1.0'; SELECT * FROM eng.version;"
        };
        yield return new object[]
        {
            "flatfile-connection",
            "CREATE CONNECTION csv_in AS FLATFILE('data.csv', HEADER = 'ON');"
        };
        yield return new object[]
        {
            "report-visual",
            "CREATE VISUAL SalesBar AS BAR (SOURCE = (SELECT product, revenue FROM #sales), MAPPINGS (X = product, Y = revenue));"
        };
        yield return new object[]
        {
            "bulk-insert",
            "BULK INSERT #Target (Name, Amount) FROM 'sales.csv' WITH (FORMAT = 'CSV', HEADER = ON);"
        };
    }

    [Theory]
    [MemberData(nameof(CanonicalScripts))]
    public void CanonicalCompatibilityCorpus_ParsesWithoutErrors(string name, string sql)
    {
        var script = Parse(sql);

        Assert.DoesNotContain(script.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains(name));
        Assert.True(
            script.Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error),
            $"{name} produced parser errors: {string.Join("; ", script.Diagnostics.Select(d => d.Message))}");
        Assert.NotEmpty(script.Statements);
    }

    [Fact]
    public async Task MigrationLinter_DeprecatedFileConnection_ReportsStableDiagnostic()
    {
        var linter = new Linter();
        linter.AddRule(new DeprecatedConnectionSyntaxRule());
        var script = Parse("CREATE CONNECTION legacy AS FILE('data.csv');");

        var results = (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();

        var diagnostic = Assert.Single(results);
        Assert.Equal(DeprecatedConnectionSyntaxRule.FileConnectorDiagnosticCode, diagnostic.Code);
        Assert.Equal(LintSeverity.Warning, diagnostic.Severity);
        Assert.Contains("FLATFILE", diagnostic.Message);
    }

    private static Script Parse(string sql)
    {
        var lexer = new Lexer(sql);
        var parser = new Parser(lexer.Tokenize(), sql);
        return parser.Parse();
    }
}
