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
    [InlineData("distances =", 2, "IDENTITY")]
    [InlineData("estimates =", 2, "IDENTITY")]
    [InlineData("Distance (", 1, "IDENTITY")]
    [InlineData("Estimate (", 1, "IDENTITY")]
    [InlineData("distances =", 2, "EM")]
    [InlineData("estimates =", 2, "EM")]
    [InlineData("Distance (", 1, "EM")]
    [InlineData("Estimate (", 1, "EM")]
    [InlineData("distances =", 2, "BAND")]
    [InlineData("estimates =", 2, "BAND")]
    [InlineData("Distance (", 1, "BAND")]
    [InlineData("Estimate (", 1, "BAND")]
    [InlineData("distances =", 2, "DATA")]
    [InlineData("estimates =", 2, "DATA")]
    [InlineData("Distance (", 1, "DATA")]
    [InlineData("Estimate (", 1, "DATA")]
    [InlineData("distances =", 2, "JITTER")]
    [InlineData("estimates =", 2, "JITTER")]
    [InlineData("Distance (", 1, "JITTER")]
    [InlineData("Estimate (", 1, "JITTER")]
    [InlineData("Id, SEED", 1, "JITTER")]
    [InlineData("distances =", 2, "IDENTITY", "SMOOTH")]
    [InlineData("Estimate (", 1, "DATA", "SMOOTH")]
    [InlineData("estimates =", 2, "EM", "STEP_BEFORE")]
    [InlineData("Distance (", 1, "BAND", "STEP_BEFORE")]
    [InlineData("distances =", 2, "JITTER", "STEP_AFTER")]
    [InlineData("Id, SEED", 1, "JITTER", "STEP_AFTER")]
    public async Task TransposedAspectLine_RenamesScalesAndFields(string token, int expectedEdits, string unit, string interpolation = "LINEAR")
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
        if (unit == "JITTER") script = script.Replace("NULL_HANDLING = GAP,", "NULL_HANDLING = GAP, POSITION = JITTER(X = 0.02, Y = 0.03, KEY = Id, SEED = 42),", System.StringComparison.Ordinal);
        else if (unit != "IDENTITY") script = script.Replace("NULL_HANDLING = GAP,", $"NULL_HANDLING = GAP, POSITION = NUDGE(X = 0.02, Y = -0.03, UNIT = {unit}),", System.StringComparison.Ordinal);
        script = script.Replace("INTERPOLATION = 'LINEAR'", $"INTERPOLATION = '{interpolation}'", System.StringComparison.Ordinal);
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
    [InlineData("distances =", 2, true)]
    [InlineData("estimates =", 3, true)]
    [InlineData("Distance (", 1, true)]
    [InlineData("LowerBound (", 1, true)]
    [InlineData("UpperBound (", 1, true)]
    [InlineData("distances =", 2, false)]
    [InlineData("estimates =", 2, false)]
    [InlineData("UpperBound (", 1, false)]
    [InlineData("distances =", 2, true, "EM")]
    [InlineData("estimates =", 3, true, "EM")]
    [InlineData("LowerBound (", 1, true, "EM")]
    [InlineData("UpperBound (", 1, false, "EM")]
    [InlineData("distances =", 2, true, "BAND")]
    [InlineData("estimates =", 3, true, "BAND")]
    [InlineData("LowerBound (", 1, true, "BAND")]
    [InlineData("UpperBound (", 1, false, "BAND")]
    [InlineData("distances =", 2, true, "DATA")]
    [InlineData("estimates =", 3, true, "DATA")]
    [InlineData("LowerBound (", 1, true, "DATA")]
    [InlineData("UpperBound (", 1, false, "DATA")]
    [InlineData("distances =", 2, true, "JITTER")]
    [InlineData("distances =", 2, false, "JITTER")]
    [InlineData("estimates =", 3, true, "JITTER")]
    [InlineData("estimates =", 2, false, "JITTER")]
    [InlineData("LowerBound (", 1, true, "JITTER")]
    [InlineData("UpperBound (", 1, false, "JITTER")]
    [InlineData("Id, SEED", 1, true, "JITTER")]
    [InlineData("Id, SEED", 1, false, "JITTER")]
    [InlineData("distances =", 2, true, "IDENTITY", "SMOOTH")]
    [InlineData("estimates =", 2, false, "DATA", "SMOOTH")]
    [InlineData("LowerBound (", 1, true, "EM", "STEP_BEFORE")]
    [InlineData("UpperBound (", 1, false, "BAND", "STEP_BEFORE")]
    [InlineData("Id, SEED", 1, true, "JITTER", "STEP_AFTER")]
    [InlineData("Distance (", 1, false, "JITTER", "STEP_AFTER")]
    [InlineData("estimates =", 3, true, "IDENTITY", "LINEAR", true)]
    [InlineData("LowerBound (", 1, true, "DATA", "SMOOTH", true)]
    [InlineData("UpperBound (", 1, true, "BAND", "STEP_BEFORE", true)]
    [InlineData("Id, SEED", 1, true, "JITTER", "STEP_AFTER", true)]
    public async Task TransposedAspectArea_RenamesSharedScalesAndFields(string token, int expectedEdits, bool ribbon, string unit = "IDENTITY", string interpolation = "LINEAR", bool confidence = false)
    {
        var script = """
            CREATE VISUAL Envelope AS CUSTOM (SOURCE = #prepared, CHART (
              COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
              SCALES (distances = LINEAR (CHANNEL = X), estimates = LINEAR (CHANNEL = Y)),
              LAYERS (envelope = AREA (
                NULL_HANDLING = GAP,
                ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = distances),
                           Y_START = LowerBound (TYPE = QUANTITATIVE, SCALE = estimates),
                           Y_END = UpperBound (TYPE = QUANTITATIVE, SCALE = estimates)),
                STYLE (INTERPOLATION = 'LINEAR')
              ))
            ));
            """;
        script = script.Replace("\r\n", "\n", System.StringComparison.Ordinal);
        if (!ribbon) script = script.Replace("NULL_HANDLING = GAP,", "NULL_HANDLING = GAP, AREA_BASELINE = ZERO,", System.StringComparison.Ordinal)
            .Replace("Y_START = LowerBound (TYPE = QUANTITATIVE, SCALE = estimates),\n               Y_END = UpperBound", "Y = UpperBound", System.StringComparison.Ordinal);
        if (unit == "JITTER") script = script.Replace("NULL_HANDLING = GAP,", "NULL_HANDLING = GAP, POSITION = JITTER(X = 0.02, Y = 0.03, KEY = Id, SEED = 42),", System.StringComparison.Ordinal);
        else if (unit != "IDENTITY") script = script.Replace("NULL_HANDLING = GAP,", $"NULL_HANDLING = GAP, POSITION = NUDGE(X = 0.02, Y = -0.03, UNIT = {unit}),", System.StringComparison.Ordinal);
        script = script.Replace("INTERPOLATION = 'LINEAR'", $"INTERPOLATION = '{interpolation}'", System.StringComparison.Ordinal);
        if (confidence) script = script.Replace("Y_START", "CONFIDENCE_LOW", System.StringComparison.Ordinal).Replace("Y_END", "CONFIDENCE_HIGH", System.StringComparison.Ordinal);
        var parsed = new Parser(new Lexer(script).Tokenize(), script).Parse();
        Assert.Empty(parsed.Diagnostics);
        Assert.Empty(ETL_SQL.Core.AdvancedChartSemanticValidator.Validate(Assert.Single(parsed.Statements.OfType<ETL_SQL.Core.CreateVisualStatement>())));
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
    [InlineData("Category (")]
    [InlineData("Amount (")]
    [InlineData("Series (")]
    public async Task RadialStack_RenamesBindings(string token)
    {
        var script = """
            CREATE VISUAL Totals AS CUSTOM (SOURCE = #prepared, CHART (
              COORDINATE (TYPE = POLAR),
              LAYERS (rings = ARC (ENCODINGS (
                THETA = Category (TYPE = NOMINAL),
                RADIUS = Amount (TYPE = QUANTITATIVE, STACK = NORMALIZE),
                COLOR = Series (TYPE = NOMINAL)
              )))
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
        Assert.Equal("renamed", Assert.Single(edits).NewText);
    }

    public static IEnumerable<object[]> DecoratedConnectionRenameCases()
    {
        foreach (var form in new[] { "LINE", "AREA", "RIBBON" })
            foreach (var coordinate in new[] { "CARTESIAN", "TRANSPOSED_CARTESIAN", "TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2" })
                foreach (var channel in new[] { "SIZE", "SHAPE", "TEXT" })
                    foreach (var interpolation in new[] { "LINEAR", "SMOOTH", "STEP_BEFORE", "STEP_AFTER" }) yield return [form, coordinate, channel, interpolation];
    }

    [Theory]
    [MemberData(nameof(DecoratedConnectionRenameCases))]
    public async Task DecoratedConnections_RenameBindingAndAnnotationPredicate(string form, string coordinate, string channel, string interpolation)
    {
        var script = $$"""
            CREATE VISUAL Route AS CUSTOM (SOURCE = #prepared, CHART (
              COORDINATE (TYPE = {{coordinate}}), FACET (WRAP = Cohort, COLUMNS = 2),
              LAYERS (route = {{(form == "LINE" ? "LINE" : "AREA")}} (NULL_HANDLING = ZERO,
                {{(form == "AREA" ? "AREA_BASELINE = ZERO," : "")}}
                ENCODINGS (X = Distance (TYPE = QUANTITATIVE),
                  {{(form == "RIBBON" ? "Y_START = Lower (TYPE = QUANTITATIVE), Y_END = Upper (TYPE = QUANTITATIVE)" : "Y = Estimate (TYPE = QUANTITATIVE)")}}, COLOR = Series (TYPE = NOMINAL)),
                STYLE (INTERPOLATION = '{{interpolation}}'),
                CONDITIONS ({{channel}} WHEN Distance < 2 THEN {{(channel == "SIZE" ? "7" : channel == "SHAPE" ? "'SQUARE'" : "'alert'")}})))
            ));
            """;
        var (provider, uri) = Provider(script);
        var result = await provider.Handle(new RenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = PositionOf(script, "Distance ("),
            NewName = "renamed"
        }, CancellationToken.None);
        var edits = Assert.IsAssignableFrom<IEnumerable<TextEdit>>(result!.Changes![uri]).ToList();
        Assert.Equal(2, edits.Count);
        Assert.All(edits, edit => Assert.Equal("renamed", edit.NewText));
    }

    public static IEnumerable<object[]> ConnectedRenameCases()
    {
        foreach (var area in new[] { false, true })
            foreach (var policy in new[] { "GAP", "CONNECT", "ZERO" })
                foreach (var transposed in new[] { false, true }) yield return [area, policy, transposed];
    }

    [Theory]
    [MemberData(nameof(ConnectedRenameCases))]
    public async Task ConnectedMarks_RenameBindingAndConditionTogether(bool area, string policy, bool transposed)
    {
        var script = """
            CREATE VISUAL Route AS CUSTOM (SOURCE = #prepared, CHART (
              COORDINATE (TYPE = CARTESIAN),
              LAYERS (route = LINE (NULL_HANDLING = GAP,
                ENCODINGS (X = Distance (TYPE = QUANTITATIVE), Y = Estimate (TYPE = QUANTITATIVE)),
                STYLE (INTERPOLATION = 'LINEAR'),
                CONDITIONS (COLOR WHEN Distance < 2 THEN '#ff0000' ELSE '#0000ff')))
            ));
            """;
        if (area) script = script.Replace("LINE (NULL_HANDLING", "AREA (AREA_BASELINE = ZERO, NULL_HANDLING", StringComparison.Ordinal);
        script = script.Replace("NULL_HANDLING = GAP", "NULL_HANDLING = " + policy, StringComparison.Ordinal);
        if (transposed) script = script.Replace("TYPE = CARTESIAN", "TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2", StringComparison.Ordinal);
        var (provider, uri) = Provider(script);
        var result = await provider.Handle(new RenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = PositionOf(script, "Distance ("),
            NewName = "renamed"
        }, CancellationToken.None);
        var edits = Assert.IsAssignableFrom<IEnumerable<TextEdit>>(result!.Changes![uri]).ToList();
        Assert.Equal(2, edits.Count);
        Assert.All(edits, edit => Assert.Equal("renamed", edit.NewText));
    }

    [Theory]
    [InlineData("Lower (")]
    [InlineData("Upper (")]
    public async Task ConditionalRibbon_RenamesEndpoints(string token)
    {
        const string script = """
            CREATE VISUAL Route AS CUSTOM (SOURCE = #prepared, CHART (
              COORDINATE (TYPE = CARTESIAN),
              LAYERS (route = AREA (NULL_HANDLING = GAP,
                ENCODINGS (X = Distance (TYPE = QUANTITATIVE),
                  Y_START = Lower (TYPE = QUANTITATIVE), Y_END = Upper (TYPE = QUANTITATIVE)),
                STYLE (INTERPOLATION = 'LINEAR'),
                CONDITIONS (COLOR WHEN Distance < 2 THEN '#ff0000' ELSE '#0000ff')))
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
        Assert.Equal("renamed", Assert.Single(edits).NewText);
    }

    [Theory]
    [InlineData("GAP", false)]
    [InlineData("CONNECT", false)]
    [InlineData("ZERO", false)]
    [InlineData("GAP", true)]
    [InlineData("CONNECT", true)]
    [InlineData("ZERO", true)]
    public async Task ConditionalRibbon_RenamesSharedScaleAndBothBounds(string policy, bool transposed)
    {
        var script = """
            CREATE VISUAL Ranges AS CUSTOM (SOURCE = #prepared, CHART (
              COORDINATE (TYPE = CARTESIAN),
              SCALES (bounds = LINEAR (CHANNEL = Y, MIN = 100, MAX = 200)),
              LAYERS (ribbon = AREA (NULL_HANDLING = GAP,
                ENCODINGS (X = Distance (TYPE = QUANTITATIVE),
                  Y_START = Lower (TYPE = QUANTITATIVE, SCALE = bounds),
                  Y_END = Upper (TYPE = QUANTITATIVE, SCALE = bounds)),
                STYLE (INTERPOLATION = 'LINEAR'),
                CONDITIONS (COLOR WHEN Distance < 2 THEN '#ff0000' ELSE '#0000ff')))
            ));
            """;
        script = script.Replace("NULL_HANDLING = GAP", "NULL_HANDLING = " + policy, StringComparison.Ordinal);
        if (transposed) script = script.Replace("TYPE = CARTESIAN", "TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2", StringComparison.Ordinal);
        var (provider, uri) = Provider(script);
        var result = await provider.Handle(new RenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = PositionOf(script, "bounds ="),
            NewName = "interval_scale"
        }, CancellationToken.None);
        var edits = Assert.IsAssignableFrom<IEnumerable<TextEdit>>(result!.Changes![uri]).ToList();
        Assert.Equal(3, edits.Count);
        Assert.All(edits, edit => Assert.Equal("interval_scale", edit.NewText));
    }

    public static IEnumerable<object[]> ConnectedCompositionRenameCases()
    {
        foreach (var (token, count) in new[] { ("Cohort,", 3), ("Phase)", 1), ("Series (", 2), ("Distance (", 4), ("vertical =", 5), ("horizontal =", 4) })
            foreach (var zero in new[] { false, true })
                foreach (var transposed in new[] { false, true }) yield return [token, count, zero, transposed];
    }

    [Theory]
    [MemberData(nameof(ConnectedCompositionRenameCases))]
    public async Task ConnectedComposition_RenamesFacetsSeriesPredicatesAndSharedScales(string token, int count, bool zero, bool transposed)
    {
        var script = """
            CREATE VISUAL Routes AS CUSTOM (SOURCE = #prepared, CHART (
              COORDINATE (TYPE = CARTESIAN),
              SCALES (horizontal = LINEAR (CHANNEL = X), vertical = LINEAR (CHANNEL = Y)),
              FACET (ROW = Cohort, COLUMN = Phase),
              RESOLVE (X = INDEPENDENT, Y = INDEPENDENT),
              LAYERS (
                uncertainty = AREA (INHERIT_ENCODINGS = OFF, NULL_HANDLING = CONNECT,
                  ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = horizontal),
                    Y_START = Lower (TYPE = QUANTITATIVE, SCALE = vertical),
                    Y_END = Upper (TYPE = QUANTITATIVE, SCALE = vertical), COLOR = Series (TYPE = NOMINAL)),
                  STYLE (INTERPOLATION = 'LINEAR'),
                  CONDITIONS (COLOR WHEN Cohort = 'A' AND Distance > 0 THEN '#ff0000' ELSE '#0000ff')),
                route = LINE (INHERIT_ENCODINGS = OFF, NULL_HANDLING = GAP,
                  ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = horizontal),
                    Y = Estimate (TYPE = QUANTITATIVE, SCALE = vertical), COLOR = Series (TYPE = NOMINAL)),
                  STYLE (INTERPOLATION = 'LINEAR'),
                  CONDITIONS (OPACITY WHEN Cohort = 'A' THEN 0.5 ELSE 1)),
                observations = POINT (INHERIT_ENCODINGS = OFF,
                  ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = horizontal),
                    Y = Estimate (TYPE = QUANTITATIVE, SCALE = vertical))))
            ));
            """;
        if (zero) script = script.Replace("NULL_HANDLING = GAP", "NULL_HANDLING = ZERO", StringComparison.Ordinal);
        if (transposed) script = script.Replace("TYPE = CARTESIAN", "TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2", StringComparison.Ordinal);
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
