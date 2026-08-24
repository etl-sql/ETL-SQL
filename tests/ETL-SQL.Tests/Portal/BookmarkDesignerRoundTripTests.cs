using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core.Parser;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Xunit;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Tests.Portal;

/// <summary>
/// Report Builder support for author bookmarks: read them out of a script, edit them, and write them
/// back without the rest of the file moving.
///
/// The property that matters most here is that a bookmark's typed values survive the trip. The
/// designer carries a parameter value as the authored source text, so <c>25</c> comes back as
/// <c>25</c> and not <c>'25'</c> — the same reason the bookmark AST holds an Expression rather than a
/// string. A round-trip that quietly retypes values would make the builder unsafe to open a
/// hand-authored report in.
/// </summary>
public class BookmarkDesignerRoundTripTests
{
    private readonly DesignerAnalysisService _analysis = new();
    private readonly DesignerScriptPatcher _patcher = new();
    private readonly DesignerScriptGenerationService _generator = new();

    private const string Script = """
        -- Data prep the designer must not touch
        CREATE DATASET &sales AS (
          SELECT region, amount FROM pg.sales
        );

        CREATE VISUAL v_rev AS BAR (
            SOURCE = &sales,
            MAPPINGS (X = region, Y = amount)
        );

        CREATE PAGE [Summary] AS DASHBOARD (
            LAYOUT (STRUCTURE = 'A', MAP ('A' = v_rev))
        );

        CREATE BOOKMARK WestQ4 AS (
            TITLE = 'West, Q4',
            PARAMETERS (
                @Region = 'West',
                @Limit = 25
            ),
            PAGE = Summary,
            STATE (
                v_rev.COLLAPSED = ON
            ),
            DEFAULT = ON
        );
        """;

    [Fact]
    public void FixtureParsesCleanly() => Assert.Empty(ParseErrors(Script));

    [Fact]
    public void ParseSurfacesBookmarksWithTheirAuthoredValues()
    {
        var state = _analysis.Parse(Script, 500).DesignState;

        var bookmark = Assert.Single(state.Bookmarks!);
        Assert.Equal("WestQ4", bookmark.Name);
        Assert.Equal("West, Q4", bookmark.Title);
        Assert.Equal("Summary", bookmark.Page);
        Assert.True(bookmark.IsDefault);

        Assert.Equal("'West'", bookmark.Parameters!.Single(p => p.Name == "@Region").Value);
        // A number stays a number. Coercing it here is what would turn it into '25' on write-back.
        Assert.Equal("25", bookmark.Parameters!.Single(p => p.Name == "@Limit").Value);

        var entry = Assert.Single(bookmark.State!);
        Assert.Equal("v_rev", entry.ObjectName);
        Assert.Equal("COLLAPSED", entry.Property);
        Assert.True(entry.On);
    }

    [Fact]
    public void ReadingAndWritingBackWithoutEditsLeavesTheScriptByteForByte()
    {
        var state = _analysis.Parse(Script, 500).DesignState;
        Assert.Equal(Script, _patcher.Patch(Script, state));
    }

    [Fact]
    public void EditingOneBookmarkLeavesTheRestOfTheScriptAlone()
    {
        var state = _analysis.Parse(Script, 500).DesignState;
        var edited = state with
        {
            Bookmarks =
            [
                state.Bookmarks![0] with
                {
                    Title = "Western Q4",
                    Parameters =
                    [
                        new DesignerBookmarkParameterDto("@Region", "'West'"),
                        new DesignerBookmarkParameterDto("@Limit", "50")
                    ]
                }
            ]
        };

        var patched = _patcher.Patch(Script, edited);

        Assert.Contains("TITLE = 'Western Q4'", patched);
        Assert.Contains("@Limit = 50", patched);
        Assert.DoesNotContain("@Limit = 25", patched);
        // Everything outside the bookmark is untouched, comment and data prep included.
        Assert.Contains("-- Data prep the designer must not touch", patched);
        Assert.Contains("SELECT region, amount FROM pg.sales", patched);
        Assert.Contains("CREATE PAGE [Summary] AS DASHBOARD", patched);
        Assert.Empty(ParseErrors(patched));
    }

    [Fact]
    public void AddingABookmarkAppendsItAndKeepsTheExistingOne()
    {
        var state = _analysis.Parse(Script, 500).DesignState;
        var added = state with
        {
            Bookmarks = state.Bookmarks!.Append(new DesignerBookmarkDto(
                "bm_new",
                "EastQ4",
                Page: "Summary",
                Parameters: [new DesignerBookmarkParameterDto("@Region", "'East'")])).ToList()
        };

        var patched = _patcher.Patch(Script, added);

        Assert.Contains("CREATE BOOKMARK WestQ4 AS (", patched);
        Assert.Contains("CREATE BOOKMARK EastQ4 AS (", patched);
        Assert.Empty(ParseErrors(patched));

        // The new bookmark is readable again with the values it was given.
        var reparsed = _analysis.Parse(patched, 500).DesignState;
        var east = reparsed.Bookmarks!.Single(b => b.Name == "EastQ4");
        Assert.Equal("'East'", east.Parameters!.Single().Value);
        Assert.Equal("Summary", east.Page);
    }

    [Fact]
    public void RemovingABookmarkDeletesItsStatement()
    {
        var state = _analysis.Parse(Script, 500).DesignState;
        var patched = _patcher.Patch(Script, state with { Bookmarks = [] });

        Assert.DoesNotContain("CREATE BOOKMARK", patched);
        Assert.Contains("CREATE PAGE [Summary] AS DASHBOARD", patched);
        Assert.Empty(ParseErrors(patched));
    }

    [Fact]
    public void AClientThatDoesNotEditBookmarksDoesNotDeleteThem()
    {
        var state = _analysis.Parse(Script, 500).DesignState;

        // Null, not empty: an older builder cannot represent bookmarks, and losing them because the
        // client could not send them back would be silent data loss.
        var patched = _patcher.Patch(Script, state with { Bookmarks = null });

        Assert.Contains("CREATE BOOKMARK WestQ4 AS (", patched);
        Assert.Equal(Script, patched);
    }

    [Fact]
    public void GeneratingAFreshScriptEmitsBookmarksThatParseBack()
    {
        var state = new DesignerStateDto(
            Pages:
            [
                new DesignerPageDto("p1", "Summary", "Dashboard",
                [
                    new DesignerVisualDto("v1", "v_rev", "BAR", 1, 1, 12, 4, "Revenue", "sales",
                        new Dictionary<string, string> { ["X"] = "region", ["Y"] = "amount" },
                        new Dictionary<string, string>())
                ])
            ],
            Datasets: [new DesignerDatasetDto("ds1", "sales", "SELECT region, amount FROM pg.sales")],
            Bookmarks:
            [
                new DesignerBookmarkDto("bm_0", "WestQ4", "West, Q4", "Summary", IsDefault: true,
                    Parameters:
                    [
                        new DesignerBookmarkParameterDto("@Region", "'West'"),
                        new DesignerBookmarkParameterDto("@Limit", "25")
                    ],
                    State: [new DesignerBookmarkStateDto("v_rev", "COLLAPSED", true)])
            ]);

        var generated = _generator.Generate(state);
        Assert.Empty(ParseErrors(generated));

        var reparsed = _analysis.Parse(generated, 500).DesignState;
        var bookmark = Assert.Single(reparsed.Bookmarks!);
        Assert.Equal("WestQ4", bookmark.Name);
        Assert.Equal("West, Q4", bookmark.Title);
        Assert.Equal("Summary", bookmark.Page);
        Assert.True(bookmark.IsDefault);
        Assert.Equal("25", bookmark.Parameters!.Single(p => p.Name == "@Limit").Value);
        Assert.Equal(("v_rev", "COLLAPSED", true),
            (bookmark.State!.Single().ObjectName, bookmark.State!.Single().Property, bookmark.State!.Single().On));
    }

    private static IReadOnlyList<string> ParseErrors(string script)
    {
        var ast = new CoreParser(new Lexer(script).Tokenize(), script).Parse();
        return ast.Diagnostics
            .Where(d => d.Severity == ETL_SQL.Core.Common.DiagnosticSeverity.Error)
            .Select(d => d.Message)
            .ToList();
    }
}
