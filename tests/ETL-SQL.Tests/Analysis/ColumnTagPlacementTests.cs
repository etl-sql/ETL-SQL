using System.Linq;
using ETL_SQL.Core;
using Xunit;

namespace ETL_SQL.Tests.Analysis
{
    /// <summary>
    /// A tag comment attaches to its column from either side of the alias. Only the trailing
    /// placement used to parse, which made the tag examples in the lineage reference — and the
    /// natural reading order, where the tag documents the expression it follows — a syntax error.
    /// </summary>
    public class ColumnTagPlacementTests
    {
        private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize()).Parse();

        private static SelectColumn SingleColumn(string sql)
        {
            var script = Parse(sql);
            Assert.Empty(script.Diagnostics);
            return Assert.IsType<SelectStatement>(script.Statements.Single()).Columns.Single();
        }

        [Fact]
        public void ATagBeforeTheAliasAttachesToTheColumn()
        {
            var column = SingleColumn("SELECT amount /* @d: Order total; @unit: USD */ AS total FROM t;");

            Assert.Equal("total", column.Alias);
            Assert.Equal("Order total", column.Metadata["d"]);
            Assert.Equal("USD", column.Metadata["unit"]);
        }

        [Fact]
        public void ATagAfterTheAliasStillAttachesToTheColumn()
        {
            var column = SingleColumn("SELECT amount AS total /* @d: Order total; @unit: USD */ FROM t;");

            Assert.Equal("total", column.Alias);
            Assert.Equal("Order total", column.Metadata["d"]);
        }

        [Fact]
        public void ATagOnAnUnaliasedColumnStillAttaches()
        {
            var column = SingleColumn("SELECT amount /* @pii: true */ FROM t;");

            Assert.Null(column.Alias);
            Assert.Equal("true", column.Metadata["pii"]);
        }

        /// <summary>
        /// The placement must not depend on the shape of the expression: a bare column, a call, and
        /// an operator expression all failed the same way before, and a reader would have no way to
        /// tell which forms were allowed.
        /// </summary>
        [Theory]
        [InlineData("first_name")]
        [InlineData("TRIM(first_name)")]
        [InlineData("first_name + ' ' + last_name")]
        [InlineData("CAST(amount AS INT) * 2")]
        [InlineData("CASE WHEN amount > 0 THEN 'yes' ELSE 'no' END")]
        public void AnyExpressionShapeAcceptsATagBeforeTheAlias(string expression)
        {
            var column = SingleColumn($"SELECT {expression} /* @d: documented */ AS c FROM t;");

            Assert.Equal("c", column.Alias);
            Assert.Equal("documented", column.Metadata["d"]);
        }

        [Fact]
        public void TagsOnBothSidesOfTheAliasMerge()
        {
            var column = SingleColumn("SELECT amount /* @unit: USD */ AS total /* @owner: Finance */ FROM t;");

            Assert.Equal("USD", column.Metadata["unit"]);
            Assert.Equal("Finance", column.Metadata["owner"]);
        }

        /// <summary>Later wins, so a reader can predict the value without knowing parse order.</summary>
        [Fact]
        public void ATagRepeatedOnBothSidesTakesItsLaterValue()
        {
            var column = SingleColumn("SELECT amount /* @owner: Sales */ AS total /* @owner: Finance */ FROM t;");

            Assert.Equal("Finance", column.Metadata["owner"]);
        }

        [Fact]
        public void EachColumnKeepsItsOwnTagsWhenSeveralAreTagged()
        {
            var script = Parse(
                "SELECT a /* @d: first */ AS x, b /* @d: second */ AS y FROM t;");
            var columns = Assert.IsType<SelectStatement>(script.Statements.Single()).Columns;

            Assert.Equal("first", columns[0].Metadata["d"]);
            Assert.Equal("second", columns[1].Metadata["d"]);
        }

        /// <summary>An alias written without AS is still an alias, and the tag still belongs.</summary>
        [Fact]
        public void ATagBeforeAnImplicitAliasAttachesToTheColumn()
        {
            var column = SingleColumn("SELECT amount /* @d: Order total */ total FROM t;");

            Assert.Equal("total", column.Alias);
            Assert.Equal("Order total", column.Metadata["d"]);
        }
    }
}
