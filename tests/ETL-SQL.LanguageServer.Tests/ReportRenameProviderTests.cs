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
            FACET (WRAP = Revenue, COLUMNS = 3)
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
    public async Task RenameField_UpdatesEncodingConditionAndWrappedFacetButNotStringLiteral()
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

    [Theory]
    [InlineData("estimates =", 4, false)]
    [InlineData("LowerBound (", 1, false)]
    [InlineData("estimates =", 4, true)]
    [InlineData("LowerBound (", 1, true)]
    public async Task TransposedAspectErrorBars_RenameScaleAndEndpoint(string token, int expectedEdits, bool nudge)
    {
        var script = """
            CREATE VISUAL Measurement AS CUSTOM (
              SOURCE = #prepared,
              CHART (
                COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
                SCALES (
                  distances = LINEAR (CHANNEL = X),
                  estimates = LINEAR (CHANNEL = Y)
                ),
                LAYERS (observations = POINT (ENCODINGS (
                  X = Distance (TYPE = QUANTITATIVE, SCALE = distances),
                  Y = Estimate (TYPE = QUANTITATIVE, SCALE = estimates),
                  ERROR_LOW = LowerBound (TYPE = QUANTITATIVE, SCALE = estimates),
                  ERROR_HIGH = UpperBound (TYPE = QUANTITATIVE, SCALE = estimates)
                )))
              )
            );
            """;
        if (nudge)
            script = script.Replace("POINT (ENCODINGS", "POINT (POSITION = NUDGE(X = 1, Y = -0.5, UNIT = EM), ENCODINGS", System.StringComparison.Ordinal);
        var (provider, uri) = Provider(script);
        var result = await provider.Handle(new RenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = PositionOf(script, token),
            NewName = "renamed"
        }, CancellationToken.None);
        var edits = Assert.IsAssignableFrom<IEnumerable<TextEdit>>(result!.Changes![uri]).ToList();
        Assert.Equal(expectedEdits, edits.Count);
        Assert.All(edits, edit => Assert.Equal("renamed", edit.NewText));
    }

    [Theory]
    [InlineData("Caption (", 2)]
    [InlineData("estimates =", 2)]
    public async Task TransposedAspectText_RenameBindingAndCondition(string token, int expectedEdits)
    {
        var script = """
            CREATE VISUAL Measurement AS CUSTOM (
              SOURCE = #prepared,
              CHART (
                COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
                SCALES (distances = LINEAR (CHANNEL = X), estimates = LINEAR (CHANNEL = Y)),
                ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = distances),
                           Y = Estimate (TYPE = QUANTITATIVE, SCALE = estimates)),
                LAYERS (labels = TEXT (
                  ENCODINGS (TEXT = Caption (TYPE = NOMINAL)),
                  CONDITIONS (TEXT WHEN Caption = 'Caption' THEN 'high')
                ))
              )
            );
            """;
        var (provider, uri) = Provider(script);
        var result = await provider.Handle(new RenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = PositionOf(script, token),
            NewName = "renamed"
        }, CancellationToken.None);
        var edits = Assert.IsAssignableFrom<IEnumerable<TextEdit>>(result!.Changes![uri]).ToList();
        Assert.Equal(expectedEdits, edits.Count);
        Assert.All(edits, edit => Assert.Equal("renamed", edit.NewText));
    }

    [Fact]
    public async Task TransposedAspectJitter_RenamesStableKeyAndBinding()
    {
        var script = """
            CREATE VISUAL Measurement AS CUSTOM (
              SOURCE = #prepared,
              CHART (
                COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
                LAYERS (observations = POINT (
                  POSITION = JITTER(X = 0.02, Y = 0.03, KEY = Id, SEED = 42),
                  ENCODINGS (X = Distance (TYPE = QUANTITATIVE), Y = Estimate (TYPE = QUANTITATIVE),
                             DETAIL = Id (TYPE = NOMINAL))
                ))
              )
            );
            """;
        var (provider, uri) = Provider(script);
        var result = await provider.Handle(new RenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = PositionOf(script, "Id, SEED"),
            NewName = "StableId"
        }, CancellationToken.None);
        var edits = Assert.IsAssignableFrom<IEnumerable<TextEdit>>(result!.Changes![uri]).ToList();
        Assert.Equal(2, edits.Count);
        Assert.All(edits, edit => Assert.Equal("StableId", edit.NewText));
    }

    private static (ReportRenameProvider Provider, DocumentUri Uri) Provider(string script = ScriptText)
    {
        var uri = DocumentUri.From("untitled:advanced-chart.rptsql");
        var parser = new Parser(new Lexer(script).Tokenize(), script);
        var store = new DocumentStateStore();
        store.SetState(uri, script, parser.Parse(), new ETL_SQL.Core.LineageTracker(ETL_SQL.Common.NullLogger.Instance));
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
