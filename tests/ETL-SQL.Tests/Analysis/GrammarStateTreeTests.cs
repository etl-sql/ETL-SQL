using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Analysis.Linting.Grammar;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Services;
using Xunit;

namespace ETL_SQL.Tests.Analysis
{
    public class GrammarStateTreeTests
    {
        private Token CreateToken(TokenType type, string value)
        {
            return new Token(type, value, 1, 1, 1, value.Length + 1);
        }

        [Fact]
        public void ValidateSequence_WithSimpleManualPath_SucceedsAndFailsCorrectly()
        {
            var tree = new GrammarStateTree();
            
            var fooNode = new StateNode("FOO");
            var barNode = new StateNode("BAR");

            // Define transitions: FOO --"bar"--> BAR
            fooNode.Transitions.Add(new StateTransition(
                t => t.Value.Equals("bar", StringComparison.OrdinalIgnoreCase),
                barNode,
                "bar"
            ));

            tree.RegisterStartNode("foo", fooNode);

            // Test sequence: foo -> bar
            var tokens1 = new List<Token>
            {
                CreateToken(TokenType.IDENTIFIER, "foo"),
                CreateToken(TokenType.IDENTIFIER, "bar")
            };

            bool success1 = tree.ValidateSequence(tokens1, out var error1);
            Assert.True(success1);
            Assert.Null(error1);

            // Test invalid sequence: foo -> baz
            var tokens2 = new List<Token>
            {
                CreateToken(TokenType.IDENTIFIER, "foo"),
                CreateToken(TokenType.IDENTIFIER, "baz")
            };

            bool success2 = tree.ValidateSequence(tokens2, out var error2);
            Assert.False(success2);
            Assert.NotNull(error2);
            Assert.Contains("Unexpected token 'baz'", error2);
        }

        [Fact]
        public void TokenWalker_ProcessesAmbiguousTransitions_MaintainsAllValidStates()
        {
            var tree = new GrammarStateTree();
            
            var fooNode = new StateNode("FOO");
            var pathA = new StateNode("PATH_A");
            var pathB = new StateNode("PATH_B");

            // From FOO node, both "bar" (PATH_A) and "bar" (PATH_B) are transitioned into
            fooNode.Transitions.Add(new StateTransition(
                t => t.Value.Equals("bar", StringComparison.OrdinalIgnoreCase),
                pathA,
                "bar"
            ));

            fooNode.Transitions.Add(new StateTransition(
                t => t.Value.Equals("bar", StringComparison.OrdinalIgnoreCase),
                pathB,
                "bar"
            ));

            tree.RegisterStartNode("foo", fooNode);

            var walker = new TokenWalker(tree);
            
            // Consume first token "foo" (starts the state tree transition to FOO node)
            bool result1 = walker.Consume(CreateToken(TokenType.IDENTIFIER, "foo"));
            Assert.True(result1);
            Assert.Single(walker.ActiveStates);
            Assert.Contains(fooNode, walker.ActiveStates);

            // Consume second token "bar" (transitions to both PATH_A and PATH_B)
            bool result2 = walker.Consume(CreateToken(TokenType.IDENTIFIER, "bar"));
            Assert.True(result2);
            
            // Both pathA and pathB should be active states
            Assert.Equal(2, walker.ActiveStates.Count);
            Assert.Contains(pathA, walker.ActiveStates);
            Assert.Contains(pathB, walker.ActiveStates);
        }

        [Fact]
        public void TokenWalker_AmbiguousBranches_KeepIndependentStateBags()
        {
            var tree = new GrammarStateTree();
            var start = new StateNode("START");
            var pathA = new StateNode("PATH_A");
            var pathB = new StateNode("PATH_B");

            start.AddTransition(new StateTransition(_ => true, pathA, onTransition: (_, walker) => walker.StateBag["depth"] = 1));
            start.AddTransition(new StateTransition(_ => true, pathB, onTransition: (_, walker) => walker.StateBag["depth"] = 2));
            tree.RegisterStartNode("GO", start);

            var walker = new TokenWalker(tree);
            Assert.True(walker.Consume(CreateToken(TokenType.IDENTIFIER, "GO")));
            Assert.True(walker.Consume(CreateToken(TokenType.IDENTIFIER, "branch")));

            Assert.Equal(1, walker.GetStateBag(pathA)["depth"]);
            Assert.Equal(2, walker.GetStateBag(pathB)["depth"]);
        }

        private IEnumerable<Token> Tokenize(string sql)
        {
            var lexer = new Lexer(sql);
            return lexer.Tokenize();
        }

        [Fact]
        public void CreateConnection_ValidGrammar_Succeeds()
        {
            var tree = DefaultGrammar.Build();

            // Scenario 1: Basic connection definition
            var tokens1 = Tokenize("CREATE CONNECTION my_conn AS FLATFILE ( PATH = 'C:\\data.csv', COMPRESS = ON )").ToList();
            bool result1 = tree.ValidateSequence(tokens1, out var error1);
            Assert.Null(error1);
            Assert.True(result1);

            // Scenario 2: Connection with multiple options
            var tokens2 = Tokenize("CREATE CONNECTION c AS MSSQL ( PASSWORD = 'abc', STRICT_SCHEMA = OFF )").ToList();
            bool result2 = tree.ValidateSequence(tokens2, out var error2);
            Assert.Null(error2);
            Assert.True(result2);
        }

        [Fact]
        public void CreateConnection_InvalidGrammar_Fails()
        {
            var tree = DefaultGrammar.Build();

            // Scenario 1: Missing AS
            var tokens1 = Tokenize("CREATE CONNECTION my_conn FLATFILE ( PATH = 'x' )").ToList();
            bool result1 = tree.ValidateSequence(tokens1, out var error1);
            Assert.False(result1);
            Assert.NotNull(error1);
            Assert.Contains("Expected one of: AS", error1);

            // Scenario 2: Missing option value (just option name followed by close paren)
            var tokens2 = Tokenize("CREATE CONNECTION my_conn AS FLATFILE ( PATH )").ToList();
            bool result2 = tree.ValidateSequence(tokens2, out var error2);
            Assert.False(result2);
            Assert.NotNull(error2);
            Assert.Contains("Unexpected token ')'", error2);

            Assert.False(tree.ValidateSequence(Tokenize("CREATE MSSQL CONNECTION c AS MSSQL()"), out _));
            Assert.False(tree.ValidateSequence(Tokenize("CREATE CONNECTION c WITH(PATH='x')"), out _));
            Assert.False(tree.ValidateSequence(Tokenize("CREATE CONNECTION c AS MSSQL PATH='x'"), out _));
            Assert.False(tree.ValidateSequence(Tokenize("CREATE CONNECTION c AS MSSQL(PATH='x' USER='u')"), out _));

            // A valid prefix is not a complete statement.
            Assert.False(tree.ValidateSequence(Tokenize("CREATE"), out var incompleteError));
            Assert.Contains("Production parser rejected", incompleteError);
        }

        [Fact]
        public void CompressFile_ValidGrammar_Succeeds()
        {
            var tree = DefaultGrammar.Build();

            // Scenario 1: Standard SQL-style compress file with destination and overwrite options
            var tokens1 = Tokenize("COMPRESS FILE 'C:\\raw.csv' TO 'C:\\raw.zip' WITH ( OVERWRITE = ON )").ToList();
            bool result1 = tree.ValidateSequence(tokens1, out var error1);
            Assert.True(result1);
            Assert.Null(error1);

            // The grammar must not certify the legacy short form rejected by the parser.
            var tokens2 = Tokenize("COMPRESS 'C:\\raw.csv'").ToList();
            bool result2 = tree.ValidateSequence(tokens2, out var error2);
            Assert.False(result2);
            Assert.NotNull(error2);
        }

        [Fact]
        public void EncryptFile_ValidGrammar_Succeeds()
        {
            var tree = DefaultGrammar.Build();

            // Scenario 1: Encrypt with Password
            var tokens1 = Tokenize("ENCRYPT FILE 'C:\\raw.csv' TO 'C:\\raw.pgp' PASSWORD 'Secret123'").ToList();
            bool result1 = tree.ValidateSequence(tokens1, out var error1);
            Assert.True(result1);
            Assert.Null(error1);

            // Scenario 2: Encrypt with Keyfile
            var tokens2 = Tokenize("ENCRYPT FILE 'C:\\raw.csv' KEYFILE 'C:\\id_rsa' WITH ( OVERWRITE = OFF )").ToList();
            bool result2 = tree.ValidateSequence(tokens2, out var error2);
            Assert.True(result2);
            Assert.Null(error2);

            // PGP_KEY is not part of the production parser's DECRYPT syntax.
            var tokens3 = Tokenize("DECRYPT 'C:\\raw.pgp' PGP_KEY 'C:\\pub.asc'").ToList();
            bool result3 = tree.ValidateSequence(tokens3, out var error3);
            Assert.False(result3);
            Assert.NotNull(error3);
        }

        [Fact]
        public void TokenWalker_Suggestions_ReturnsCorrectCandidates()
        {
            var tree = DefaultGrammar.Build();
            var walker = new TokenWalker(tree);

            // Cursor at empty starting point -> walker is at ROOT, can suggest start keywords
            var context = new SuggestionContext { Prefix = "" };
            var suggestionsRoot = walker.GetSuggestions(context);
            // ROOT node does not have direct suggestions (the transitions are registered on startNode keywords)
            // But we can check transition targets or verify suggestions after consuming CREATE
            
            bool res1 = walker.Consume(CreateToken(TokenType.CREATE, "CREATE"));
            Assert.True(res1);
            var suggestionsCreate = walker.GetSuggestions(context);
            Assert.Contains(suggestionsCreate, s => s.Text == "CONNECTION" && s.Type == SuggestionType.Keyword);

            // Consume CONNECTION
            bool res2 = walker.Consume(CreateToken(TokenType.CONNECTION, "CONNECTION"));
            Assert.True(res2);

            // Consume connection name
            bool res3 = walker.Consume(CreateToken(TokenType.IDENTIFIER, "my_conn"));
            Assert.True(res3);

            // Consume AS
            bool res4 = walker.Consume(CreateToken(TokenType.AS, "AS"));
            Assert.True(res4);

            // We are after AS -> should suggest connection types
            var suggestionsAs = walker.GetSuggestions(context);
            Assert.Contains(suggestionsAs, s => s.Text == "FLATFILE" && s.Type == SuggestionType.Connection);
            Assert.Contains(suggestionsAs, s => s.Text == "MSSQL" && s.Type == SuggestionType.Connection);
        }
    }
}
