using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Common;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Core.Parsing
{
    /// <summary>
    /// Guards the coupling between <see cref="TokenType"/> and <see cref="LanguageMetadata"/>.
    /// <para>
    /// The lexer's keyword table is the <b>intersection</b> of the two: a word lexes as a keyword
    /// only when it appears in <c>LanguageMetadata.GetAllKeywords()</c> <i>and</i> a
    /// <c>TokenType</c> member of the same name exists. Neither half fails loudly on its own, and
    /// both directions have shipped bugs:
    /// </para>
    /// <list type="bullet">
    /// <item>Adding only a <c>TokenType</c> — the parser dispatches on a token the lexer never
    /// produces, so the statement is unreachable and reports a baffling unrelated error
    /// (<c>SET DATA_QUALITY_DRY_RUN</c> reported "left-hand side of a SET statement must be a
    /// variable").</item>
    /// <item>Adding both — the word becomes reserved <i>everywhere</i>, breaking scripts that used
    /// it as a table or connection name (<c>QUARANTINE</c> broke
    /// <c>CREATE CONNECTION quarantine</c>; <c>DATASETS</c> broke <c>SHOW DATASETS</c> because the
    /// parser still matched it as an identifier).</item>
    /// </list>
    /// </summary>
    public class KeywordTokenContractTests
    {
        /// <summary>
        /// Token types that are not words: punctuation, literals, and lexer-internal markers. These
        /// are produced by dedicated scanner paths, never by keyword lookup.
        /// </summary>
        private static readonly HashSet<string> NonKeywordTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            "IDENTIFIER", "STRING_LITERAL", "NUMBER", "VARIABLE", "COLUMN_TAG", "EOF",
            "PLUS", "MINUS", "STAR", "SLASH", "MODULO", "EQUALS", "NOT_EQUALS",
            "LESS_THAN", "GREATER_THAN", "LESS_EQUALS", "GREATER_EQUALS",
            "LPAREN", "RPAREN", "COMMA", "SEMICOLON", "DOT", "COLON",
            "LBRACKET", "RBRACKET", "LBRACE", "RBRACE",
        };

        /// <summary>
        /// Words the lexer deliberately maps to a <i>different</i> token than their own name —
        /// spelling aliases, registered explicitly in <c>Lexer.InitializeKeywords</c>. Listed here
        /// so an intentional alias reads as intentional rather than as a contract violation.
        /// </summary>
        private static readonly Dictionary<string, string> IntentionalAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["FILE_SEND"] = "SEND_FILE",
            ["FILE_RECEIVE"] = "RECEIVE_FILE",
            ["TAB"] = "NAV_TAB",
        };

        /// <summary>
        /// Every word-shaped token type the metadata claims is a keyword must actually lex to that
        /// token. A token the lexer never emits is a statement the parser can never reach.
        /// </summary>
        [Fact]
        public void EveryLexableTokenType_LexesFromItsOwnName()
        {
            var keywords = new HashSet<string>(
                LanguageMetadata.GetAllKeywords().Concat(LanguageMetadata.Functions),
                StringComparer.OrdinalIgnoreCase);

            var unreachable = new List<string>();
            foreach (var name in Enum.GetNames<TokenType>())
            {
                if (NonKeywordTokens.Contains(name)) continue;
                if (IntentionalAliases.ContainsKey(name)) continue;
                // A TokenType with no metadata entry may still be registered explicitly in the
                // lexer; that route is covered by the dispatch test below, which checks the
                // property that actually matters — that the word lexes to its own token.
                if (!keywords.Contains(name)) continue;

                var tokens = new Lexer(name).Tokenize();
                if (tokens.Count == 0) { unreachable.Add($"{name} (lexed to nothing)"); continue; }
                if (tokens[0].Type.ToString() != name)
                    unreachable.Add($"{name} (lexed as {tokens[0].Type})");
            }

            Assert.True(unreachable.Count == 0,
                "These words are listed as keywords and have a matching TokenType, but do not lex "
                + "to that token — the parser cannot reach anything that dispatches on them:\n  "
                + string.Join("\n  ", unreachable));
        }

        /// <summary>Intentional aliases still resolve to the token they are meant to alias.</summary>
        [Fact]
        public void IntentionalAliases_ResolveToTheirTargetToken()
        {
            foreach (var (alias, target) in IntentionalAliases)
                Assert.Equal(target, new Lexer(alias).Tokenize()[0].Type.ToString());
        }

        /// <summary>
        /// The inverse: a <c>TokenType</c> that no metadata entry backs never reaches the lexer, so
        /// any parser branch dispatching on it is dead code. This is the shape of the
        /// <c>DATA_QUALITY_DRY_RUN</c> bug.
        /// </summary>
        [Fact]
        public void TokenTypesUsedForStatementDispatch_ActuallyLex()
        {
            // Statement-introducing and SET/SHOW sub-command tokens: if these do not lex, whole
            // statements silently become unreachable. Kept explicit rather than reflected so the
            // list documents what the language guarantees.
            string[] mustLex =
            [
                "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "TRUNCATE", "CREATE", "DROP",
                "ALTER", "SET", "SHOW", "DECLARE", "IF", "WHILE", "BEGIN", "END", "TRY", "CATCH",
                "THROW", "ASSERT", "EXPECT", "REPLAY", "PRINT", "GOTO", "RETURN", "EXPORT",
                "CASE_SENSITIVE", "DATA_QUALITY_DRY_RUN", "DATASETS", "LINEAGE",
            ];

            // A word may be registered through metadata, the function list, or an explicit lexer
            // entry — three routes. What matters is the outcome, so assert the outcome.
            var missing = mustLex
                .Where(word => !Enum.TryParse<TokenType>(word, ignoreCase: true, out _)
                    || new Lexer(word).Tokenize()[0].Type.ToString() != word)
                .ToList();

            Assert.True(missing.Count == 0,
                "These statement/sub-command words do not lex to their own TokenType, so any parser "
                + "branch dispatching on them is unreachable. A TokenType member alone is not "
                + "enough — the word must also be registered as a keyword, either in "
                + "LanguageMetadata or explicitly in Lexer.InitializeKeywords:\n  "
                + string.Join("\n  ", missing));
        }

        /// <summary>
        /// Domain nouns a user will reasonably name a table, column, or connection after. Reserving
        /// one of these breaks real scripts, and the breakage is invisible until someone uses the
        /// word — <c>quarantine</c> is exactly the name a steward gives a quarantine table.
        /// </summary>
        [Theory]
        [InlineData("quarantine")]
        [InlineData("rules")]
        [InlineData("quality")]
        [InlineData("owner")]
        [InlineData("steward")]
        [InlineData("trend")]
        [InlineData("failure")]
        [InlineData("expectation")]
        [InlineData("dq")]
        public void DomainNouns_RemainUsableAsIdentifiers(string word)
        {
            var tokens = new Lexer(word).Tokenize();

            Assert.True(tokens[0].Type == TokenType.IDENTIFIER,
                $"'{word}' lexes as {tokens[0].Type}, so it can no longer be used as a table, "
                + "column, or connection name. If a grammar position needs this word, match it "
                + "contextually by token text instead of promoting it to a TokenType — that is how "
                + "ON FAILURE QUARANTINE and REPLAY QUARANTINE match it today.");
        }

        /// <summary>
        /// Words that are <i>already</i> reserved and would be plausible identifiers. Not a defect —
        /// they predate this guard and unreserving them now would be a breaking change — but
        /// recorded so the cost is visible when someone hits it, and so an accidental change in
        /// either direction shows up as a failing test rather than silently.
        /// </summary>
        [Theory]
        [InlineData("source")]
        [InlineData("history")]
        [InlineData("alert")]
        [InlineData("target")]
        public void KnownReservedDomainNouns_AreStillReserved(string word)
        {
            Assert.NotEqual(TokenType.IDENTIFIER, new Lexer(word).Tokenize()[0].Type);
        }
    }
}
