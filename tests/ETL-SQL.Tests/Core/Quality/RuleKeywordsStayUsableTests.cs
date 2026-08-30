using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Core.Quality
{
    /// <summary>
    /// The rule vocabulary must not cost the language its ordinary words. Every rule keyword except
    /// <c>EXPECT</c> is matched contextually inside the clause, so <c>quarantine</c>, <c>steward</c>,
    /// <c>matches</c>, and the rest stay usable as table, column, and alias names — the reason
    /// <c>TokenType.cs</c> refuses to make <c>QUARANTINE</c> a token in the first place: it is the
    /// most natural name for a quarantine table.
    /// </summary>
    public class RuleKeywordsStayUsableTests
    {
        [Theory]
        [InlineData("quarantine")]
        [InlineData("matches")]
        [InlineData("blank")]
        [InlineData("expr")]
        [InlineData("steward")]
        [InlineData("handling")]
        [InlineData("notify")]
        [InlineData("castable")]
        public void RuleWords_AreStillUsableAsTableAndColumnNames(string word)
        {
            var select = (SelectStatement)Parse($"SELECT {word} FROM {word};");

            Assert.Equal(word, ((IdentifierExpression)select.Columns[0].Expression).Name);
            Assert.Equal(word, select.FromTable.TableName);
        }

        [Theory]
        [InlineData("quarantine")]
        [InlineData("matches")]
        [InlineData("steward")]
        public void RuleWords_AreStillUsableAsAnImplicitAlias(string word)
        {
            var select = (SelectStatement)Parse($"SELECT Id {word} FROM src;");

            Assert.Equal(word, select.Columns[0].Alias);
        }

        [Fact]
        public void QuarantineIsStillTheMostNaturalNameForAQuarantineTable()
        {
            var select = (SelectStatement)Parse(
                "SELECT Id EXPECT NOT NULL ON FAILURE QUARANTINE "
                + "INTO clean FROM src ON FAILURE QUARANTINE TO quarantine;");

            Assert.Equal("quarantine", Assert.Single(select.OnFailureActions!).Target);
        }

        [Fact]
        public void ExpectIsReserved_AndThatIsTheWholeKeywordCost()
        {
            // EXPECT leads the clause, so it cannot also be an implicit alias — the one word the
            // rule surface takes. Written explicitly after AS it still works.
            var select = (SelectStatement)Parse("SELECT Id AS expect FROM src;");
            Assert.Equal("expect", select.Columns[0].Alias);
        }

        private static Statement Parse(string sql) =>
            new ETL_SQL.Core.Parser.Parser(new Lexer(sql).Tokenize(), sql).ParseStatement();
    }
}
