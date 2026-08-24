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
/// Detail-surface dependencies must participate in rename. Before this, renaming a
/// container updated only its declaration and left <c>TOOLTIP = &lt;container&gt;</c>
/// pointing at a name that no longer existed — a report that still parsed but whose
/// popover silently resolved to nothing.
/// </summary>
public sealed class DetailSurfaceRenameTests
{
    // Mirrors the shape of samples/10_Kitchen_Sinks/01_BAR.rptsql, plus an inline
    // VISUALS list so both reference forms are covered.
    private const string ScriptText = """
        CREATE VISUAL MonthDetail AS BAR (
          SOURCE = (SELECT Region, Revenue FROM #sales WHERE Month = @hover_value),
          MAPPINGS (X = Region, Y = Revenue)
        );

        CREATE CONTAINER TooltipBox AS BOX (
          LAYOUT (
            STRUCTURE = 'A',
            MAP ('A' = MonthDetail)
          )
        );

        CREATE VISUAL BarWithTooltip AS BAR (
          SOURCE = (SELECT Month, SUM(Revenue) AS Revenue FROM #sales GROUP BY Month),
          MAPPINGS (X = Month, Y = Revenue),
          TOOLTIP = TooltipBox
        );

        CREATE VISUAL InlineBar AS BAR (
          SOURCE = (SELECT Month, Revenue FROM #sales),
          MAPPINGS (X = Month, Y = Revenue),
          TOOLTIP ('**Detail**', VISUALS (MonthDetail))
        );
        """;

    [Fact]
    public async Task RenamingAContainer_UpdatesTheTooltipReference()
    {
        var (provider, uri) = Provider();

        var result = await provider.Handle(new RenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = PositionOf(ScriptText, "TooltipBox AS BOX"),
            NewName = "DetailBox"
        }, CancellationToken.None);

        var edited = Apply(ScriptText, result!, uri);

        Assert.Contains("CREATE CONTAINER DetailBox AS BOX", edited);
        Assert.Contains("TOOLTIP = DetailBox", edited);
        Assert.DoesNotContain("TooltipBox", edited);
    }

    [Fact]
    public async Task RenamingAVisual_UpdatesTheContainerSlotAndInlineVisualsList()
    {
        var (provider, uri) = Provider();

        var result = await provider.Handle(new RenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = PositionOf(ScriptText, "MonthDetail AS BAR"),
            NewName = "RegionDetail"
        }, CancellationToken.None);

        var edited = Apply(ScriptText, result!, uri);

        Assert.Contains("CREATE VISUAL RegionDetail AS BAR", edited);
        Assert.Contains("MAP ('A' = RegionDetail)", edited);
        Assert.Contains("VISUALS (RegionDetail)", edited);
        Assert.DoesNotContain("MonthDetail", edited);
    }

    [Fact]
    public async Task RenamingAVisual_LeavesUnrelatedIdentifiersAlone()
    {
        var (provider, uri) = Provider();

        var result = await provider.Handle(new RenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = PositionOf(ScriptText, "MonthDetail AS BAR"),
            NewName = "RegionDetail"
        }, CancellationToken.None);

        var edited = Apply(ScriptText, result!, uri);

        // The rename must not disturb the other visuals, their sources, or the parameter.
        Assert.Contains("CREATE VISUAL BarWithTooltip AS BAR", edited);
        Assert.Contains("@hover_value", edited);
        Assert.Contains("GROUP BY Month", edited);
    }

    /// <summary>Applies the returned edits back to the source and returns the result text.</summary>
    private static string Apply(string text, WorkspaceEdit edit, DocumentUri uri)
    {
        var edits = Assert.IsAssignableFrom<IEnumerable<TextEdit>>(edit.Changes![uri]).ToList();
        Assert.NotEmpty(edits);

        // Apply last-first so earlier offsets stay valid.
        foreach (var item in edits.OrderByDescending(e => e.Range.Start.Line).ThenByDescending(e => e.Range.Start.Character))
        {
            int start = OffsetOf(text, item.Range.Start);
            int end = OffsetOf(text, item.Range.End);
            text = text[..start] + item.NewText + text[end..];
        }
        return text;
    }

    private static int OffsetOf(string text, Position position)
    {
        int offset = 0;
        for (int line = 0; line < position.Line; line++)
            offset = text.IndexOf('\n', offset) + 1;
        return offset + position.Character;
    }

    private static (ReportRenameProvider Provider, DocumentUri Uri) Provider()
    {
        var uri = DocumentUri.From("untitled:detail-surface.rptsql");
        var parser = new Parser(new Lexer(ScriptText).Tokenize(), ScriptText);
        var store = new DocumentStateStore();
        store.SetState(uri, ScriptText, parser.Parse(), new ETL_SQL.Core.LineageTracker(ETL_SQL.Common.NullLogger.Instance));
        return (new ReportRenameProvider(store), uri);
    }

    private static Position PositionOf(string text, string value)
    {
        var offset = text.IndexOf(value, System.StringComparison.Ordinal);
        var before = text[..offset];
        var line = before.Count(character => character == '\n');
        var lineStart = before.LastIndexOf('\n') + 1;
        return new Position(line, offset - lineStart);
    }
}
