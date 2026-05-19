using System.Linq;
using Xunit;
using ETL_SQL.TUI.UI;

namespace ETL_SQL.Tests.Integration.UI
{
    /// <summary>
    /// Pure tokenization logic — no terminal UI initialization required.
    /// </summary>
    public class EtlSqlHighlighterTests
    {
        private readonly EtlSqlHighlighter _hl = new();

        private HighlightToken[] Tokenize(string line) =>
            _hl.Tokenize(line).ToArray();

        // ── Keywords ─────────────────────────────────────────────────────────

        [Fact]
        public void Tokenize_SelectKeyword_ReturnsBlueToken()
        {
            var tokens = Tokenize("SELECT");
            var t = tokens.Single(t => t.Start == 0 && t.Length == 6);
            Assert.Equal(HighlightColor.Keyword, t.Color);
        }

        [Fact]
        public void Tokenize_FromKeyword_ReturnsBlueToken()
        {
            var tokens = Tokenize("FROM orders");
            Assert.Contains(tokens, t => t.Start == 0 && t.Length == 4 && t.Color == HighlightColor.Keyword);
        }

        [Fact]
        public void Tokenize_DdlKeyword_ReturnsDdlToken()
        {
            var tokens = Tokenize("CREATE TABLE Foo");
            Assert.Contains(tokens, t => t.Start == 0 && t.Length == 6 && t.Color == HighlightColor.DdlKeyword);
        }

        [Fact]
        public void Tokenize_ControlFlowKeyword_ReturnsControlFlowToken()
        {
            var tokens = Tokenize("IF @x > 0");
            Assert.Contains(tokens, t => t.Start == 0 && t.Length == 2 && t.Color == HighlightColor.ControlFlow);
        }

        [Fact]
        public void Tokenize_UnknownWord_ProducesNoToken()
        {
            var tokens = Tokenize("MyColumn");
            Assert.Empty(tokens);
        }

        // ── Comments ─────────────────────────────────────────────────────────

        [Fact]
        public void Tokenize_LineComment_ReturnsCommentToken()
        {
            var tokens = Tokenize("-- this is a comment");
            Assert.Single(tokens);
            Assert.Equal(0, tokens[0].Start);
            Assert.Equal(HighlightColor.Comment, tokens[0].Color);
        }

        [Fact]
        public void Tokenize_InlineComment_StartsAtDashes()
        {
            var line = "SELECT 1 -- inline";
            var tokens = Tokenize(line);
            var comment = tokens.Single(t => t.Color == HighlightColor.Comment);
            Assert.Equal(line.IndexOf("--"), comment.Start);
        }

        // ── Strings ───────────────────────────────────────────────────────────

        [Fact]
        public void Tokenize_SingleQuotedString_ReturnsStringToken()
        {
            var tokens = Tokenize("'hello world'");
            Assert.Single(tokens);
            Assert.Equal(0, tokens[0].Start);
            Assert.Equal("'hello world'".Length, tokens[0].Length);
            Assert.Equal(HighlightColor.String, tokens[0].Color);
        }

        [Fact]
        public void Tokenize_DoubleQuotedString_ReturnsStringToken()
        {
            var tokens = Tokenize("\"hello\"");
            Assert.Contains(tokens, t => t.Color == HighlightColor.String);
        }

        // ── Variables ─────────────────────────────────────────────────────────

        [Fact]
        public void Tokenize_Variable_ReturnsVariableToken()
        {
            var tokens = Tokenize("@MyVar");
            Assert.Single(tokens);
            Assert.Equal(0, tokens[0].Start);
            Assert.Equal("@MyVar".Length, tokens[0].Length);
            Assert.Equal(HighlightColor.Variable, tokens[0].Color);
        }

        // ── Brackets ──────────────────────────────────────────────────────────

        [Fact]
        public void Tokenize_BracketedIdentifier_ReturnsBracketToken()
        {
            var tokens = Tokenize("[MyTable]");
            Assert.Single(tokens);
            Assert.Equal(HighlightColor.Bracket, tokens[0].Color);
        }

        // ── No overlap ────────────────────────────────────────────────────────

        [Fact]
        public void Tokenize_MixedLine_NoOverlappingTokens()
        {
            var tokens = Tokenize("SELECT 'literal' FROM [Table] WHERE @x = 1 -- end");
            for (int i = 0; i < tokens.Length; i++)
            for (int j = i + 1; j < tokens.Length; j++)
            {
                int endI = tokens[i].Start + tokens[i].Length;
                int endJ = tokens[j].Start + tokens[j].Length;
                bool overlaps = tokens[i].Start < endJ && tokens[j].Start < endI;
                Assert.False(overlaps,
                    $"Tokens overlap: [{tokens[i].Start}..{endI}) and [{tokens[j].Start}..{endJ})");
            }
        }

        [Fact]
        public void Tokenize_EmptyLine_ReturnsNoTokens()
        {
            Assert.Empty(Tokenize(""));
        }

        [Fact]
        public void Tokenize_CommentAfterKeyword_BothTokenized()
        {
            var tokens = Tokenize("SELECT -- comment");
            Assert.Contains(tokens, t => t.Color == HighlightColor.Keyword);
            Assert.Contains(tokens, t => t.Color == HighlightColor.Comment);
        }
    }
}
