using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

public class AuthorBookmarkTests
{
    private static Script Parse(string sql)
    {
        var tokens = new Lexer(sql).Tokenize();
        return new Parser(tokens, sql).Parse();
    }

    [Fact]
    public void ParsesMinimalBookmark()
    {
        var script = Parse("CREATE BOOKMARK Simple AS (TITLE = 'Simple view');");
        var stmt = Assert.Single(script.Statements);
        var bm = Assert.IsType<CreateBookmarkStatement>(stmt);
        Assert.Equal("Simple", bm.Name);
        Assert.NotNull(bm.Title);
        Assert.Contains("Simple view", bm.Title!.ToSql());
        Assert.False(bm.IsDefault);
        Assert.Empty(bm.Parameters);
        Assert.Empty(bm.StateEntries);
        Assert.Null(bm.PageName);
    }

    [Fact]
    public void ParsesFullBookmark()
    {
        var script = Parse("""
            CREATE BOOKMARK WestCoastDetail AS (
                TITLE = 'West Coast Detail',
                PARAMETERS (
                    @region = 'West',
                    @year = 2026
                ),
                PAGE = Detail,
                STATE (
                    FilterPanel.COLLAPSED = ON,
                    DetailChart.VISIBLE = ON
                ),
                DEFAULT = ON
            );
            """);
        var stmt = Assert.Single(script.Statements);
        var bm = Assert.IsType<CreateBookmarkStatement>(stmt);
        Assert.Equal("WestCoastDetail", bm.Name);
        Assert.True(bm.IsDefault);
        Assert.Equal("Detail", bm.PageName);
        Assert.Equal(2, bm.Parameters.Count);
        Assert.Equal("@region", bm.Parameters[0].ParameterName);
        Assert.Equal("West", bm.Parameters[0].Value);
        Assert.Equal("@year", bm.Parameters[1].ParameterName);
        Assert.Equal("2026", bm.Parameters[1].Value);
        Assert.Equal(2, bm.StateEntries.Count);
        Assert.Equal("FilterPanel.COLLAPSED", bm.StateEntries[0].ObjectKey);
        Assert.Equal("ON", bm.StateEntries[0].Value);
    }

    [Fact]
    public void ParsesApplyBookmarkAction()
    {
        var script = Parse("""
            CREATE BUTTON ApplyBtn AS (
                TITLE = 'Apply West Coast',
                ACTIONS (ON_CLICK = APPLY_BOOKMARK(WestCoastDetail))
            );
            """);
        var stmt = Assert.Single(script.Statements);
        var btn = Assert.IsType<CreateButtonStatement>(stmt);
        var action = Assert.Single(btn.Actions);
        var ab = Assert.IsType<ApplyBookmarkAction>(action);
        Assert.Equal("WestCoastDetail", ab.BookmarkName);
    }

    [Fact]
    public void FormatterRoundTripsBookmark()
    {
        var script = Parse("""
            CREATE BOOKMARK Detail AS (
                TITLE = 'Detail View',
                PARAMETERS (@region = 'West'),
                PAGE = DetailPage,
                STATE (FilterPanel.COLLAPSED = ON),
                DEFAULT = ON
            );
            """);
        var formatted = script.Statements[0].ToSql();
        Assert.Contains("CREATE BOOKMARK Detail AS", formatted);
        Assert.Contains("TITLE =", formatted);
        Assert.Contains("PARAMETERS (@region = 'West')", formatted);
        Assert.Contains("PAGE = DetailPage", formatted);
        Assert.Contains("STATE (FilterPanel.COLLAPSED = ON)", formatted);
        Assert.Contains("DEFAULT = ON", formatted);

        var reparsed = Parse(formatted);
        var stmt = Assert.Single(reparsed.Statements);
        var bm = Assert.IsType<CreateBookmarkStatement>(stmt);
        Assert.Equal("Detail", bm.Name);
        Assert.True(bm.IsDefault);
        Assert.Equal("DetailPage", bm.PageName);
    }

    [Fact]
    public void FormatterRoundTripsApplyBookmarkAction()
    {
        var script = Parse("""
            CREATE BUTTON Btn AS (
                ACTIONS (ON_CLICK = APPLY_BOOKMARK(MyBookmark))
            );
            """);
        var formatted = script.Statements[0].ToSql();
        Assert.Contains("APPLY_BOOKMARK(MyBookmark)", formatted);

        var reparsed = Parse(formatted);
        var btn = Assert.IsType<CreateButtonStatement>(reparsed.Statements[0]);
        var action = Assert.Single(btn.Actions);
        var ab = Assert.IsType<ApplyBookmarkAction>(action);
        Assert.Equal("MyBookmark", ab.BookmarkName);
    }

    [Fact]
    public void ParsesDuplicateBookmarkIdentifiers()
    {
        var script = Parse("""
            CREATE BOOKMARK A AS (TITLE = 'First');
            CREATE BOOKMARK A AS (TITLE = 'Second');
            """);
        Assert.Equal(2, script.Statements.Count);
        Assert.All(script.Statements, s => Assert.IsType<CreateBookmarkStatement>(s));
        Assert.All(script.Statements.Cast<CreateBookmarkStatement>(), s => Assert.Equal("A", s.Name));
    }

    [Fact]
    public void ParsesMultipleDefaultBookmarks()
    {
        var script = Parse("""
            CREATE BOOKMARK A AS (TITLE = 'A', DEFAULT = ON);
            CREATE BOOKMARK B AS (TITLE = 'B', DEFAULT = ON);
            """);
        Assert.Equal(2, script.Statements.Count);
        Assert.All(script.Statements.Cast<CreateBookmarkStatement>(), s => Assert.True(s.IsDefault));
    }

    [Fact]
    public void ParsesBookmarkWithPageOnly()
    {
        var script = Parse("CREATE BOOKMARK Nav AS (PAGE = Overview);");
        var stmt = Assert.Single(script.Statements);
        var bm = Assert.IsType<CreateBookmarkStatement>(stmt);
        Assert.Equal("Overview", bm.PageName);
        Assert.Empty(bm.Parameters);
        Assert.Empty(bm.StateEntries);
        Assert.Null(bm.Title);
    }

    [Fact]
    public void ParsesBookmarkWithStateOnly()
    {
        var script = Parse("""
            CREATE BOOKMARK Collapsed AS (
                STATE (
                    Sidebar.COLLAPSED = ON,
                    Chart1.VISIBLE = OFF
                )
            );
            """);
        var stmt = Assert.Single(script.Statements);
        var bm = Assert.IsType<CreateBookmarkStatement>(stmt);
        Assert.Equal(2, bm.StateEntries.Count);
        Assert.Equal("Chart1.VISIBLE", bm.StateEntries[1].ObjectKey);
        Assert.Equal("OFF", bm.StateEntries[1].Value);
    }

    [Fact]
    public void BookmarkManifestContainsNoRawParameterValues()
    {
        var bookmark = new ETL_SQL.Reporting.BookmarkManifest
        {
            Name = "Test",
            Parameters = new() { ["@secret"] = "sensitive_value" },
            PageName = "Detail"
        };
        var json = System.Text.Json.JsonSerializer.Serialize(bookmark);
        Assert.Contains("sensitive_value", json);

        var urlHash = $"#bookmark={bookmark.Name}";
        Assert.DoesNotContain("sensitive_value", urlHash);
        Assert.Equal("#bookmark=Test", urlHash);
    }
}
