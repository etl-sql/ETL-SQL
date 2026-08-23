using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

/// <summary>Static-safety coverage for the bookmark lint rule, exercising the real Linter pipeline.</summary>
public class BookmarkValidationRuleTests
{
    private static async Task<List<LintResult>> Lint(string sql)
    {
        var linter = new Linter();
        linter.AddRule(new BookmarkValidationRule());
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        return (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();
    }

    private const string ValidPreamble = """
        DECLARE @region VARCHAR INPUT = 'All';
        DECLARE @year INT INPUT = 2026;
        CREATE VISUAL DetailTable AS TABLE (SOURCE = #sales);
        CREATE CONTAINER FilterPanel AS BOX (LAYOUT (STRUCTURE = 'A', MAP ('A' = DetailTable)));
        CREATE PAGE Main AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = DetailTable)));
        """;

    [Fact]
    public async Task ValidBookmark_ProducesNoDiagnostics()
    {
        var results = await Lint(ValidPreamble + """
            CREATE BOOKMARK B AS (
                PARAMETERS (@region = 'West', @year = 2026),
                PAGE = Main,
                STATE (FilterPanel.COLLAPSED = ON, DetailTable.VISIBLE = ON),
                DEFAULT = ON
            );
            """);
        Assert.Empty(results);
    }

    [Fact]
    public async Task DuplicateIdentifiers_AreErrors()
    {
        var results = await Lint("CREATE BOOKMARK A AS (PAGE = Main); CREATE BOOKMARK A AS (PAGE = Main);");
        Assert.Contains(results, r => r.Severity == LintSeverity.Error && r.Message.Contains("Duplicate bookmark identifier"));
    }

    [Fact]
    public async Task MultipleDefaults_AreErrors()
    {
        var results = await Lint(ValidPreamble +
            "CREATE BOOKMARK A AS (PAGE = Main, DEFAULT = ON); CREATE BOOKMARK B AS (PAGE = Main, DEFAULT = ON);");
        Assert.Contains(results, r => r.Severity == LintSeverity.Error && r.Message.Contains("Only one bookmark may be DEFAULT"));
    }

    [Fact]
    public async Task UndefinedPage_IsError()
    {
        var results = await Lint(ValidPreamble + "CREATE BOOKMARK B AS (PAGE = Ghost);");
        Assert.Contains(results, r => r.Severity == LintSeverity.Error && r.Message.Contains("undefined page 'Ghost'"));
    }

    [Fact]
    public async Task UndefinedStateObject_IsError()
    {
        var results = await Lint(ValidPreamble + "CREATE BOOKMARK B AS (STATE (Ghost.VISIBLE = ON));");
        Assert.Contains(results, r => r.Severity == LintSeverity.Error && r.Message.Contains("undefined object 'Ghost'"));
    }

    [Fact]
    public async Task UndeclaredParameter_IsError()
    {
        var results = await Lint(ValidPreamble + "CREATE BOOKMARK B AS (PARAMETERS (@ghost = 'x'));");
        Assert.Contains(results, r => r.Severity == LintSeverity.Error && r.Message.Contains("@ghost"));
    }

    [Fact]
    public async Task ParameterTypeMismatch_IsWarning()
    {
        // @year is INT; assigning a string literal is a type mismatch warning.
        var results = await Lint(ValidPreamble + "CREATE BOOKMARK B AS (PARAMETERS (@year = 'twenty'));");
        Assert.Contains(results, r => r.Severity == LintSeverity.Warning && r.Message.Contains("declared type 'INT'"));
    }

    [Fact]
    public async Task ApplyBookmark_UnknownTarget_IsError()
    {
        var results = await Lint(ValidPreamble +
            "CREATE BUTTON Btn AS (ACTIONS (ON_CLICK = APPLY_BOOKMARK(Nope)));");
        Assert.Contains(results, r => r.Severity == LintSeverity.Error && r.Message.Contains("undefined bookmark 'Nope'"));
    }
}
