using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Parser;
using ETL_SQL.LSP;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Xunit;

namespace ETL_SQL.LanguageServer.Tests;

/// <summary>
/// Editor support for author bookmarks. A bookmark identifier is written in three places and read in
/// two more, so the properties that matter are that the editor can offer the declared set where only
/// a bookmark is valid, explain what applying one will do, and rewrite every occurrence when the name
/// changes — a rename that missed <c>APPLY_BOOKMARK</c> would leave a button pointing at nothing.
/// </summary>
public sealed class BookmarkLanguageFeatureTests
{
    private const string ScriptText = """
        DECLARE @Region VARCHAR INPUT = 'All';
        DECLARE @Limit INT INPUT = 10;
        CREATE VISUAL FilterPanel AS TEXT (VALUE = 'Filters');
        CREATE PAGE Summary AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = FilterPanel)));

        CREATE BOOKMARK WestQ4 AS (
            TITLE = 'West, Q4',
            PARAMETERS (
                @Region = 'West',
                @Limit = 25
            ),
            PAGE = Summary,
            STATE (
                FilterPanel.VISIBLE = OFF
            ),
            DEFAULT = ON
        );

        CREATE BOOKMARK EastQ4 AS (
            PARAMETERS (@Region = 'East')
        );

        CREATE BUTTON GoWest AS (
            LABEL = 'West',
            ON_CLICK = APPLY_BOOKMARK(WestQ4)
        );

        DROP BOOKMARK EastQ4;
        """;

    // ── Rename ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task RenamingABookmarkRewritesItsDeclarationAndEveryReference()
    {
        var (provider, uri) = RenameProvider();

        var result = await provider.Handle(new RenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = PositionOf(ScriptText, "WestQ4 AS"),
            NewName = "WesternQ4"
        }, CancellationToken.None);

        var edits = Assert.IsAssignableFrom<IEnumerable<TextEdit>>(result!.Changes![uri]).ToList();
        // The declaration and the APPLY_BOOKMARK in the button's action.
        Assert.Equal(2, edits.Count);
        Assert.All(edits, edit => Assert.Equal("WesternQ4", edit.NewText));
        Assert.Equal(ScriptText.Replace("WestQ4", "WesternQ4"), Apply(ScriptText, edits));
    }

    [Fact]
    public async Task RenamingFromTheApplySiteRewritesTheDeclarationToo()
    {
        var (provider, uri) = RenameProvider();

        var result = await provider.Handle(new RenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = PositionOf(ScriptText, "WestQ4)"),
            NewName = "WesternQ4"
        }, CancellationToken.None);

        var edits = Assert.IsAssignableFrom<IEnumerable<TextEdit>>(result!.Changes![uri]).ToList();
        Assert.Equal(2, edits.Count);
    }

    [Fact]
    public async Task RenamingFromTheDropSiteRewritesTheDeclarationToo()
    {
        var (provider, uri) = RenameProvider();

        var result = await provider.Handle(new RenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = PositionOf(ScriptText, "EastQ4;"),
            NewName = "EasternQ4"
        }, CancellationToken.None);

        var edits = Assert.IsAssignableFrom<IEnumerable<TextEdit>>(result!.Changes![uri]).ToList();
        Assert.Equal(2, edits.Count);
        Assert.Equal(ScriptText.Replace("EastQ4", "EasternQ4"), Apply(ScriptText, edits));
    }

    [Fact]
    public async Task PrepareRenameOffersTheBookmarkIdentifierUnderTheCursor()
    {
        var (provider, uri) = RenameProvider();

        var result = await provider.Handle(new PrepareRenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = PositionOf(ScriptText, "WestQ4 AS")
        }, CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task RenamingAPageRewritesTheBookmarkPageReference()
    {
        var (provider, uri) = RenameProvider();

        var result = await provider.Handle(new RenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = PositionOf(ScriptText, "Summary"),
            NewName = "Overview"
        }, CancellationToken.None);

        var edits = Assert.IsAssignableFrom<IEnumerable<TextEdit>>(result!.Changes![uri]).ToList();
        Assert.Equal(2, edits.Count);
        var rewritten = Apply(ScriptText, edits);
        Assert.Contains("CREATE PAGE Overview", rewritten);
        Assert.Contains("PAGE = Overview", rewritten);
    }

    [Fact]
    public async Task RenamingANamedObjectRewritesBookmarkStateReference()
    {
        var (provider, uri) = RenameProvider();
        var result = await provider.Handle(new RenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = PositionOf(ScriptText, "FilterPanel.VISIBLE"),
            NewName = "Filters"
        }, CancellationToken.None);

        var rewritten = Apply(ScriptText, Assert.IsAssignableFrom<IEnumerable<TextEdit>>(result!.Changes![uri]));
        Assert.Contains("CREATE VISUAL Filters", rewritten);
        Assert.Contains("Filters.VISIBLE", rewritten);
    }

    [Fact]
    public async Task RenamingAParameterRewritesBookmarkAssignments()
    {
        var (provider, uri) = RenameProvider();
        var result = await provider.Handle(new RenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = PositionOf(ScriptText, "@Region VARCHAR"),
            NewName = "Area"
        }, CancellationToken.None);

        var rewritten = Apply(ScriptText, Assert.IsAssignableFrom<IEnumerable<TextEdit>>(result!.Changes![uri]));
        Assert.DoesNotContain("@Region", rewritten);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(rewritten, "@Area").Count);
    }

    // ── Completion context ───────────────────────────────────────────────────

    [Theory]
    [InlineData("ON_CLICK = APPLY_BOOKMARK(")]
    [InlineData("ON_CLICK = APPLY_BOOKMARK( We")]
    [InlineData("DROP BOOKMARK ")]
    [InlineData("DROP BOOKMARK IF EXISTS ")]
    public void ABookmarkIdentifierIsExpectedWhereOnlyOneIsValid(string scriptBefore)
        => Assert.True(BookmarkSymbols.ExpectsBookmarkName(scriptBefore));

    [Theory]
    [InlineData("SELECT * FROM ")]
    [InlineData("CREATE BOOKMARK ")]          // declaring a new name, not referencing one
    [InlineData("APPLY_BOOKMARK(WestQ4) ")]   // already closed
    public void ABookmarkIdentifierIsNotExpectedElsewhere(string scriptBefore)
        => Assert.False(BookmarkSymbols.ExpectsBookmarkName(scriptBefore));

    [Fact]
    public void DeclaredBookmarksAreOfferedInSourceOrder()
    {
        var script = Parse();
        Assert.Equal(new[] { "WestQ4", "EastQ4" }, BookmarkSymbols.Declared(script).Select(b => b.Name));
    }

    // ── Hover ────────────────────────────────────────────────────────────────

    [Fact]
    public void HoverDocumentationExplainsWhatApplyingTheBookmarkDoes()
    {
        var bookmark = BookmarkSymbols.Find(Parse(), "westq4");
        Assert.NotNull(bookmark);
        var markdown = BookmarkSymbols.Describe(bookmark!);

        Assert.Contains("Bookmark `WestQ4`", markdown);
        Assert.Contains("report default", markdown);
        Assert.Contains("`Summary`", markdown);
        Assert.Contains("@Region", markdown);
        // The typed value is shown as authored rather than flattened to a quoted string.
        Assert.Contains("25", markdown);
        Assert.Contains("FilterPanel.VISIBLE", markdown);
        Assert.Contains("APPLY_BOOKMARK(WestQ4)", markdown);
    }

    [Fact]
    public void HoverFindsNothingForAnIdentifierThatIsNotABookmark()
        => Assert.Null(BookmarkSymbols.Find(Parse(), "Summary"));

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ETL_SQL.Core.Script Parse() =>
        new Parser(new Lexer(ScriptText).Tokenize(), ScriptText).Parse();

    private static (ReportRenameProvider Provider, DocumentUri Uri) RenameProvider()
    {
        var uri = DocumentUri.From("untitled:bookmarks.rptsql");
        var store = new DocumentStateStore();
        store.SetState(uri, ScriptText, Parse(), new ETL_SQL.Core.LineageTracker(ETL_SQL.Common.NullLogger.Instance));
        return (new ReportRenameProvider(store), uri);
    }

    /// <summary>Applies edits back to the source so the test asserts the resulting text, not just counts.</summary>
    private static string Apply(string text, IEnumerable<TextEdit> edits)
    {
        var lines = text.Split('\n');
        foreach (var edit in edits.OrderByDescending(e => e.Range.Start.Line).ThenByDescending(e => e.Range.Start.Character))
        {
            var line = (int)edit.Range.Start.Line;
            var start = (int)edit.Range.Start.Character;
            var end = (int)edit.Range.End.Character;
            lines[line] = lines[line][..start] + edit.NewText + lines[line][end..];
        }
        return string.Join("\n", lines);
    }

    private static Position PositionOf(string text, string value)
    {
        var offset = text.IndexOf(value, System.StringComparison.Ordinal);
        Assert.True(offset >= 0, $"Anchor '{value}' not found in the script.");
        var before = text[..offset];
        var line = before.Count(character => character == '\n');
        var lineStart = before.LastIndexOf('\n') + 1;
        return new Position(line, offset - lineStart);
    }
}
