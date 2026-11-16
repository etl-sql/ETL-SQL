using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Services;
using ETL_SQL.Data;
using Moq;

namespace ETL_SQL.LanguageServer.Tests;

[Trait("CompatBreak", "0.22")]
public class RetiredConnectionCompletionTests
{
    [Theory]
    [InlineData("CREATE CONNECTION c AS FLATFILE(", true)]
    [InlineData("CREATE CONNECTION c ON FLATFILE WITH (", false)]
    [InlineData("CREATE CONNECTION c ON FLATFILE('data.csv') WITH (", false)]
    public async Task ConnectorOptionsAreSuggestedOnlyForTheSupportedConstructor(string sql, bool supported)
    {
        var connector = new Mock<IConnector>();
        connector.Setup(value => value.GetSupportedOptions()).Returns(new Dictionary<string, string[]>
        {
            ["HEADER"] = ["ON", "OFF"]
        });
        var metadata = new Mock<IMetadataManager>();
        metadata.Setup(value => value.GetConnector("FLATFILE")).Returns(connector.Object);
        metadata.Setup(value => value.GetConnections(It.IsAny<string>())).Returns([]);
        var service = new LanguageService(metadata.Object);
        var suggestions = await service.GetSuggestionsAsync(new SuggestionContext { ScriptBefore = sql, FullScript = sql });
        Assert.Equal(supported, suggestions.Any(value => value.Type == SuggestionType.OptionName && value.Text == "HEADER"));
    }

    [Theory]
    [InlineData("TOOLTIP ('Details' VISUALS (DetailChart))")]
    [InlineData("TOOLTIP ('Details' FIELDS (Amount))")]
    public void TooltipMarkdownRequiresASeparatorBeforeAnotherPart(string clause)
    {
        var source = $"CREATE VISUAL v AS BAR (SOURCE = (SELECT 1 AS Amount), MAPPINGS (X = Amount, Y = Amount), {clause});";
        Assert.Throws<SyntaxException>(() => new Parser(new Lexer(source).Tokenize(), source).ParseStatement());
    }

    [Theory]
    [InlineData("DROP CHART old;", "DROP VISUAL old;")]
    [InlineData("DOCKER START db;", "START DOCKER db;")]
    [InlineData("DOCKER STOP db;", "STOP DOCKER db;")]
    [InlineData("DOCKER PAUSE db;", "PAUSE DOCKER db;")]
    [InlineData("DOCKER CLOSE db;", "CLOSE DOCKER db;")]
    [InlineData("LINEAGE EXPORT AS OPENLINEAGE TO 'out.jsonl';", "EXPORT LINEAGE AS OPENLINEAGE TO 'out.jsonl';")]
    public void RetiredStatementsAreRejectedAndCanonicalReplacementsParse(string retired, string canonical)
    {
        static Script ParseGuarded(string statement)
        {
            var source = "SET WHAT_IF ON; " + statement;
            var parser = new Parser(new Lexer(source).Tokenize(), source);
            return parser.Parse();
        }

        Assert.Contains(ParseGuarded(retired).Diagnostics, diagnostic => diagnostic.Severity == ETL_SQL.Core.Common.DiagnosticSeverity.Error);
        var replacement = ParseGuarded(canonical);
        Assert.Empty(replacement.Diagnostics);
        Assert.Equal(2, replacement.Statements.Count);
    }
}
