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

    [Theory]
    [InlineData("distances =", 2)]
    [InlineData("estimates =", 2)]
    [InlineData("Distance (", 1)]
    [InlineData("Estimate (", 1)]
    public async Task TransposedAspectLine_RenamesScalesAndFields(string token, int expectedEdits)
    {
        var script = """
            CREATE VISUAL Route AS CUSTOM (SOURCE = #prepared, CHART (
              COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
              SCALES (distances = LINEAR (CHANNEL = X), estimates = LINEAR (CHANNEL = Y)),
              LAYERS (route = LINE (
                NULL_HANDLING = GAP,
                ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = distances),
                           Y = Estimate (TYPE = QUANTITATIVE, SCALE = estimates)),
                STYLE (INTERPOLATION = 'LINEAR')
              ))
            ));
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
    [InlineData("estimates =", 4, "")]
    [InlineData("LowerBound (", 1, "")]
    [InlineData("estimates =", 4, "EM")]
    [InlineData("estimates =", 4, "BAND")]
    [InlineData("estimates =", 4, "DATA")]
    [InlineData("LowerBound (", 1, "EM")]
    [InlineData("LowerBound (", 1, "BAND")]
    [InlineData("LowerBound (", 1, "DATA")]
    public async Task TransposedAspectErrorBars_RenameScaleAndEndpoint(string token, int expectedEdits, string nudgeUnit)
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
        if (nudgeUnit.Length > 0)
            script = script.Replace("POINT (ENCODINGS", $"POINT (POSITION = NUDGE(X = 1, Y = -0.5, UNIT = {nudgeUnit}), ENCODINGS", System.StringComparison.Ordinal);
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

    [Theory]
    [InlineData("cohorts =", 2)]
    [InlineData("Cohort (", 1)]
    public async Task TransposedAspectOffsets_RenameScaleAndGroupField(string token, int expectedEdits)
    {
        var script = """
            CREATE VISUAL Measurement AS CUSTOM (
              SOURCE = #prepared,
              CHART (
                COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
                SCALES (cohorts = BAND (CHANNEL = X_OFFSET)),
                LAYERS (observations = POINT (
                  ENCODINGS (X = Distance (TYPE = QUANTITATIVE), Y = Estimate (TYPE = QUANTITATIVE),
                             X_OFFSET = Cohort (TYPE = NOMINAL, SCALE = cohorts))
                ))
              )
            );
            """;
        Assert.Empty(new Parser(new Lexer(script).Tokenize(), script).Parse().Diagnostics);
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
    [InlineData(false, "estimates =", 3, "IDENTITY")]
    [InlineData(true, "estimates =", 3, "IDENTITY")]
    [InlineData(true, "Estimate (", 2, "IDENTITY")]
    [InlineData(false, "estimates =", 3, "EM")]
    [InlineData(true, "Estimate (", 2, "BAND")]
    [InlineData(true, "Estimate (", 2, "DATA")]
    [InlineData(false, "estimates =", 3, "JITTER")]
    [InlineData(true, "Estimate (", 2, "JITTER")]
    [InlineData(true, "Id, SEED", 1, "JITTER")]
    public async Task TransposedAspectRule_RenamesScaleAndField(bool field, string token, int expectedEdits, string unit)
    {
        var script = """
            CREATE VISUAL Measurement AS CUSTOM (
              SOURCE = #prepared,
              CHART (
                COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
                SCALES (
                  distances = LINEAR (CHANNEL = X, MIN = 0, MAX = 10),
                  estimates = LINEAR (CHANNEL = Y, MIN = 0, MAX = 10)
                ),
                LAYERS (
                  observations = POINT (ENCODINGS (
                    X = Distance (TYPE = QUANTITATIVE, SCALE = distances),
                    Y = Estimate (TYPE = QUANTITATIVE, SCALE = estimates)
                  )),
                  threshold = RULE (
                    Z_INDEX = 1,
                    INHERIT_ENCODINGS = OFF,
                    ENCODINGS (Y = DATUM(5) (TYPE = QUANTITATIVE, SCALE = estimates)),
                    STYLE (LABEL = '<target>', COLOR = '#112233')
                  )
                )
              )
            );
            """;
        if (field) script = script.Replace("DATUM(5)", "Estimate", System.StringComparison.Ordinal);
        if (unit == "JITTER") script = script.Replace("Z_INDEX = 1,", "Z_INDEX = 1, POSITION = JITTER(X = 0, Y = 0.03, KEY = Id, SEED = 42),", System.StringComparison.Ordinal);
        else if (unit != "IDENTITY") script = script.Replace("Z_INDEX = 1,", $"Z_INDEX = 1, POSITION = NUDGE(X = 0, Y = 0.03, UNIT = {unit}),", System.StringComparison.Ordinal);
        Assert.Empty(new Parser(new Lexer(script).Tokenize(), script).Parse().Diagnostics);
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
    [InlineData("estimates =", 4, false, "IDENTITY", false)]
    [InlineData("estimates =", 4, false, "EM", false)]
    [InlineData("estimates =", 4, false, "BAND", false)]
    [InlineData("estimates =", 4, false, "JITTER", false)]
    [InlineData("estimates =", 4, false, "DATA", false)]
    [InlineData("LowerBound (", 1, false, "IDENTITY", false)]
    [InlineData("LowerBound (", 1, false, "EM", false)]
    [InlineData("LowerBound (", 1, false, "BAND", false)]
    [InlineData("LowerBound (", 1, false, "JITTER", false)]
    [InlineData("LowerBound (", 1, false, "DATA", false)]
    [InlineData("distances =", 4, true, "IDENTITY", false)]
    [InlineData("distances =", 4, true, "EM", false)]
    [InlineData("distances =", 4, true, "BAND", false)]
    [InlineData("distances =", 4, true, "JITTER", false)]
    [InlineData("distances =", 4, true, "DATA", false)]
    [InlineData("StartX (", 1, true, "IDENTITY", false)]
    [InlineData("StartX (", 1, true, "EM", false)]
    [InlineData("StartX (", 1, true, "BAND", false)]
    [InlineData("StartX (", 1, true, "JITTER", false)]
    [InlineData("StartX (", 1, true, "DATA", false)]
    [InlineData("Distance, SEED", 3, false, "JITTER", false)]
    [InlineData("Distance, SEED", 2, true, "JITTER", false)]
    [InlineData("distances =", 4, true, "IDENTITY", true)]
    [InlineData("estimates =", 4, true, "IDENTITY", true)]
    [InlineData("StartX (", 1, true, "IDENTITY", true)]
    [InlineData("LowerBound (", 1, true, "IDENTITY", true)]
    [InlineData("distances =", 4, true, "EM", true)]
    [InlineData("estimates =", 4, true, "EM", true)]
    [InlineData("StartX (", 1, true, "EM", true)]
    [InlineData("LowerBound (", 1, true, "EM", true)]
    [InlineData("distances =", 4, true, "BAND", true)]
    [InlineData("estimates =", 4, true, "BAND", true)]
    [InlineData("StartX (", 1, true, "BAND", true)]
    [InlineData("LowerBound (", 1, true, "BAND", true)]
    [InlineData("distances =", 4, true, "DATA", true)]
    [InlineData("estimates =", 4, true, "DATA", true)]
    [InlineData("StartX (", 1, true, "DATA", true)]
    [InlineData("LowerBound (", 1, true, "DATA", true)]
    [InlineData("distances =", 4, true, "JITTER", true)]
    [InlineData("estimates =", 4, true, "JITTER", true)]
    [InlineData("StartX (", 1, true, "JITTER", true)]
    [InlineData("LowerBound (", 1, true, "JITTER", true)]
    [InlineData("Distance, SEED", 2, true, "JITTER", true)]
    public async Task TransposedRangeGeometry_RenamesScaleAndEndpoint(string token, int count, bool diagonal, string unit, bool rectangle)
    {
        var script = """
            CREATE VISUAL Measurement AS CUSTOM (
              SOURCE = #prepared,
              CHART (
                COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
                SCALES (
                  distances = LINEAR (CHANNEL = X, MIN = 0, MAX = 10),
                  estimates = LINEAR (CHANNEL = Y, MIN = 0, MAX = 10)
                ),
                LAYERS (
                  observations = POINT (ENCODINGS (
                    X = Distance (TYPE = QUANTITATIVE, SCALE = distances),
                    Y = Estimate (TYPE = QUANTITATIVE, SCALE = estimates)
                  )),
                  threshold = RULE (
                    Z_INDEX = 1,
                    INHERIT_ENCODINGS = OFF,
                    ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = distances), Y_START = LowerBound (TYPE = QUANTITATIVE, SCALE = estimates), Y_END = UpperBound (TYPE = QUANTITATIVE, SCALE = estimates)),
                    STYLE (LABEL = '<target>', COLOR = '#112233')
                  )
                )
              )
            );
            """;
        if (unit == "JITTER") script = script.Replace("Z_INDEX = 1,", "Z_INDEX = 1, POSITION = JITTER(X = 0.02, Y = 0.03, KEY = Distance, SEED = 42),", System.StringComparison.Ordinal);
        else if (unit != "IDENTITY") script = script.Replace("Z_INDEX = 1,", $"Z_INDEX = 1, POSITION = NUDGE(X = 0.02, Y = -0.03, UNIT = {unit}),", System.StringComparison.Ordinal);
        if (rectangle) script = script.Replace("threshold = RULE", "threshold = RECT", System.StringComparison.Ordinal);
        if (diagonal) script = script.Replace("ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = distances), Y_START", "ENCODINGS (X_START = StartX (TYPE = QUANTITATIVE, SCALE = distances), X_END = EndX (TYPE = QUANTITATIVE, SCALE = distances), Y_START", System.StringComparison.Ordinal);
        Assert.Empty(new Parser(new Lexer(script).Tokenize(), script).Parse().Diagnostics);
        var (provider, uri) = Provider(script);
        var result = await provider.Handle(new RenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = PositionOf(script, token),
            NewName = "renamed"
        }, CancellationToken.None);
        var edits = Assert.IsAssignableFrom<IEnumerable<TextEdit>>(result!.Changes![uri]).ToList();
        Assert.Equal(count, edits.Count);
        Assert.All(edits, edit => Assert.Equal("renamed", edit.NewText));
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
