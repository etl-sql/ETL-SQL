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

public sealed class ReportRenameProviderTests
{
    private const string ScriptText = """
        CREATE VISUAL Native AS CUSTOM (
          SOURCE = #prepared,
          CHART (
            COORDINATE (TYPE = CARTESIAN),
            SCALES (revenue_scale = LINEAR (CHANNEL = Y)),
            LAYERS (bars = RECT (
              ENCODINGS (Y = Revenue (TYPE = QUANTITATIVE, SCALE = revenue_scale)),
              CONDITIONS (COLOR WHEN Revenue < 0 THEN '#b91c1c')
            )),
            FACET (ROW = Revenue)
          )
        );
        """;

    [Fact]
    public async Task RenameScale_UpdatesDeclarationAndEncodingReferenceOnly()
    {
        var (provider, uri) = Provider();
        var cursor = PositionOf(ScriptText, "revenue_scale =");

        var result = await provider.Handle(new RenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = cursor,
            NewName = "amount_scale"
        }, CancellationToken.None);

        var edits = Assert.IsAssignableFrom<IEnumerable<TextEdit>>(result!.Changes![uri]).ToList();
        Assert.Equal(2, edits.Count);
        Assert.All(edits, edit => Assert.Equal("amount_scale", edit.NewText));
    }

    [Fact]
    public async Task RenameField_UpdatesEncodingConditionAndFacetButNotStringLiteral()
    {
        var (provider, uri) = Provider();
        var cursor = PositionOf(ScriptText, "Revenue (TYPE");

        var result = await provider.Handle(new RenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = cursor,
            NewName = "NetRevenue"
        }, CancellationToken.None);

        var edits = Assert.IsAssignableFrom<IEnumerable<TextEdit>>(result!.Changes![uri]).ToList();
        Assert.Equal(3, edits.Count);
    }

    private static (ReportRenameProvider Provider, DocumentUri Uri) Provider()
    {
        var uri = DocumentUri.From("untitled:advanced-chart.rptsql");
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
