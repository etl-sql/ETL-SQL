using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

/// <summary>
/// Parser, formatter, and DROP coverage for author bookmarks. These exercise the real lexer/parser
/// and the AstSerializer round-trip — not constructed AST nodes.
/// </summary>
public class AuthorBookmarkTests
{
    private static Script Parse(string sql)
    {
        var tokens = new Lexer(sql).Tokenize();
        return new Parser(tokens, sql).Parse();
    }

    // Parse().Parse() collects syntax errors as diagnostics; ParseStatement() propagates them,
    // which is what the rejection tests need to observe.
    private static Statement ParseStatement(string sql)
    {
        var tokens = new Lexer(sql).Tokenize();
        return new Parser(tokens, sql).ParseStatement();
    }

    [Fact]
    public void ParsesMinimalBookmark()
    {
        var script = Parse("CREATE BOOKMARK Simple AS (TITLE = 'Simple view');");
        var bm = Assert.IsType<CreateBookmarkStatement>(Assert.Single(script.Statements));
        Assert.Equal("Simple", bm.Name);
        Assert.Contains("Simple view", bm.Title!.ToSql());
        Assert.False(bm.IsDefault);
        Assert.Empty(bm.Parameters);
        Assert.Empty(bm.StateEntries);
        Assert.Null(bm.PageName);
    }

    [Fact]
    public void RetainsTypedParameterLiterals()
    {
        var script = Parse("""
            CREATE BOOKMARK B AS (
                PARAMETERS (@region = 'West', @year = 2026, @flag = TRUE, @nothing = NULL)
            );
            """);
        var bm = Assert.IsType<CreateBookmarkStatement>(Assert.Single(script.Statements));
        Assert.Equal(4, bm.Parameters.Count);

        var region = Assert.IsType<LiteralExpression>(bm.Parameters[0].Value);
        Assert.Equal(TokenType.STRING_LITERAL, region.Type);
        Assert.Equal("West", region.Value);

        var year = Assert.IsType<LiteralExpression>(bm.Parameters[1].Value);
        Assert.Equal(TokenType.NUMBER, year.Type);
        Assert.Equal(2026m, year.Value);

        var flag = Assert.IsType<LiteralExpression>(bm.Parameters[2].Value);
        Assert.Equal(true, flag.Value);

        var nothing = Assert.IsType<LiteralExpression>(bm.Parameters[3].Value);
        Assert.Null(nothing.Value);
    }

    [Fact]
    public void ParsesStructuredStateEntries()
    {
        var script = Parse("""
            CREATE BOOKMARK WestCoastDetail AS (
                PAGE = Detail,
                STATE (FilterPanel.COLLAPSED = ON, DetailChart.VISIBLE = OFF),
                DEFAULT = ON
            );
            """);
        var bm = Assert.IsType<CreateBookmarkStatement>(Assert.Single(script.Statements));
        Assert.True(bm.IsDefault);
        Assert.Equal("Detail", bm.PageName);
        Assert.Equal(2, bm.StateEntries.Count);
        Assert.Equal("FilterPanel", bm.StateEntries[0].ObjectName);
        Assert.Equal(BookmarkStateProperty.Collapsed, bm.StateEntries[0].Property);
        Assert.True(bm.StateEntries[0].On);
        Assert.Equal("DetailChart", bm.StateEntries[1].ObjectName);
        Assert.Equal(BookmarkStateProperty.Visible, bm.StateEntries[1].Property);
        Assert.False(bm.StateEntries[1].On);
    }

    [Fact]
    public void FormatterDoesNotQuoteNumbersOrBooleans()
    {
        var script = Parse("""
            CREATE BOOKMARK B AS (
                PARAMETERS (@region = 'West', @year = 2026, @flag = TRUE),
                STATE (Panel.COLLAPSED = ON),
                DEFAULT = ON
            );
            """);
        var formatted = script.Statements[0].ToSql();
        Assert.Contains("@region = 'West'", formatted);
        Assert.Contains("@year = 2026", formatted);      // not '2026'
        Assert.Contains("@flag = TRUE", formatted);       // not 'TRUE'
        Assert.Contains("Panel.COLLAPSED = ON", formatted);
        Assert.Contains("DEFAULT = ON", formatted);
        Assert.DoesNotContain("'2026'", formatted);
        Assert.DoesNotContain("'TRUE'", formatted);
    }

    [Fact]
    public void FormatterRoundTripsTypedBookmark()
    {
        var original = """
            CREATE BOOKMARK Detail AS (TITLE = 'Detail', PARAMETERS (@region = 'West', @year = 2026), PAGE = DetailPage, STATE (FilterPanel.COLLAPSED = ON), DEFAULT = ON);
            """;
        var formatted = Parse(original).Statements[0].ToSql();
        var reparsed = Assert.IsType<CreateBookmarkStatement>(Parse(formatted).Statements[0]);
        Assert.Equal("Detail", reparsed.Name);
        Assert.True(reparsed.IsDefault);
        Assert.Equal("DetailPage", reparsed.PageName);
        var year = Assert.IsType<LiteralExpression>(reparsed.Parameters[1].Value);
        Assert.Equal(TokenType.NUMBER, year.Type);
        Assert.Equal(2026m, year.Value);
    }

    [Fact]
    public void ParsesApplyBookmarkActionAndRoundTrips()
    {
        var script = Parse("CREATE BUTTON Btn AS (ACTIONS (ON_CLICK = APPLY_BOOKMARK(WestCoastDetail)));");
        var btn = Assert.IsType<CreateButtonStatement>(Assert.Single(script.Statements));
        var ab = Assert.IsType<ApplyBookmarkAction>(Assert.Single(btn.Actions));
        Assert.Equal("WestCoastDetail", ab.BookmarkName);

        var formatted = btn.ToSql();
        Assert.Contains("APPLY_BOOKMARK(WestCoastDetail)", formatted);
        var reparsed = Assert.IsType<CreateButtonStatement>(Parse(formatted).Statements[0]);
        Assert.Equal("WestCoastDetail", Assert.IsType<ApplyBookmarkAction>(reparsed.Actions[0]).BookmarkName);
    }

    [Theory]
    [InlineData("CREATE BOOKMARK B AS (STATE (Panel.MAXIMIZED = ON));")]      // invalid property
    [InlineData("CREATE BOOKMARK B AS (STATE (Panel.COLLAPSED = YES));")]     // invalid value
    [InlineData("CREATE BOOKMARK B AS (STATE (Panel = ON));")]                // missing property
    [InlineData("CREATE BOOKMARK B AS (STATE (Panel.VISIBLE.EXTRA = ON));")]  // nested property
    [InlineData("CREATE BOOKMARK B AS (DEFAULT = TRUE);")]                    // DEFAULT not ON/OFF
    [InlineData("CREATE BOOKMARK B AS (PARAMETERS (@x = 1, @x = 2));")]       // duplicate parameter
    public void RejectsMalformedBookmarkSyntax(string sql)
    {
        Assert.Throws<SyntaxException>(() => ParseStatement(sql));
    }

    [Fact]
    public void RejectsNonScalarParameterValue()
    {
        // A function call is not a typed scalar literal or variable reference.
        Assert.Throws<SyntaxException>(() => ParseStatement("CREATE BOOKMARK B AS (PARAMETERS (@x = UPPER('a')));"));
    }

    [Fact]
    public void AcceptsVariableReferenceParameterValue()
    {
        var script = Parse("CREATE BOOKMARK B AS (PARAMETERS (@target = @source));");
        var bm = Assert.IsType<CreateBookmarkStatement>(script.Statements[0]);
        Assert.IsType<VariableExpression>(bm.Parameters[0].Value);
    }

    [Fact]
    public void ParsesDropBookmark()
    {
        var script = Parse("DROP BOOKMARK WestCoastDetail;");
        var drop = Assert.IsType<DropReportObjectStatement>(Assert.Single(script.Statements));
        Assert.Equal(ReportObjectType.Bookmark, drop.ObjectType);
        Assert.Equal("WestCoastDetail", drop.Name);
        Assert.False(drop.IfExists);
    }

    [Fact]
    public void ParsesDropBookmarkIfExistsAndRoundTrips()
    {
        var script = Parse("DROP BOOKMARK IF EXISTS WestCoastDetail;");
        var drop = Assert.IsType<DropReportObjectStatement>(Assert.Single(script.Statements));
        Assert.True(drop.IfExists);

        var formatted = drop.ToSql();
        Assert.Contains("DROP BOOKMARK", formatted);
        Assert.Contains("IF EXISTS", formatted);
        Assert.Contains("WestCoastDetail", formatted);
        var reparsed = Assert.IsType<DropReportObjectStatement>(Parse(formatted).Statements[0]);
        Assert.Equal(ReportObjectType.Bookmark, reparsed.ObjectType);
        Assert.True(reparsed.IfExists);
    }
}
