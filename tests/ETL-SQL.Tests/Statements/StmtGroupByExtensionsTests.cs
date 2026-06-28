using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    public class StmtGroupByExtensionsTests
    {
        [Fact]
        public async Task GroupByAll_GroupsByNonAggregateColumns()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var sql = @"
                SELECT region, COUNT(*) AS n, SUM(amount) AS total
                FROM (VALUES ('N', 10), ('N', 30), ('S', 20)) AS t(region, amount)
                GROUP BY ALL
                ORDER BY region;";

            var res = await ev.ExecuteQuery(Parse(sql).Statements[0]).FirstAsync();

            Assert.Equal(2, res.Rows.Count);
            Assert.Equal("N", res.Rows[0]["region"]);
            Assert.Equal(2m, res.Rows[0]["n"]);
            Assert.Equal(40m, res.Rows[0]["total"]);
            Assert.Equal("S", res.Rows[1]["region"]);
            Assert.Equal(1m, res.Rows[1]["n"]);
            Assert.Equal(20m, res.Rows[1]["total"]);
        }

        [Fact]
        public async Task GroupByAll_GroupsByMultipleColumnsAndExpressions()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var sql = @"
                SELECT region, UPPER(cat) AS c, SUM(amount) AS total
                FROM (VALUES ('N', 'a', 10), ('N', 'a', 5), ('N', 'b', 20)) AS t(region, cat, amount)
                GROUP BY ALL
                ORDER BY region, c;";

            var res = await ev.ExecuteQuery(Parse(sql).Statements[0]).FirstAsync();

            Assert.Equal(2, res.Rows.Count);
            Assert.Equal("A", res.Rows[0]["c"]);
            Assert.Equal(15m, res.Rows[0]["total"]);
            Assert.Equal("B", res.Rows[1]["c"]);
            Assert.Equal(20m, res.Rows[1]["total"]);
        }

        [Fact]
        public async Task GroupByAndOrderByPositional_ResolveToSelectItems()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var sql = @"
                SELECT region, SUM(amount) AS total
                FROM (VALUES ('N', 10), ('N', 30), ('S', 20)) AS t(region, amount)
                GROUP BY 1
                ORDER BY 1;";

            var res = await ev.ExecuteQuery(Parse(sql).Statements[0]).FirstAsync();

            Assert.Equal(2, res.Rows.Count);
            Assert.Equal("N", res.Rows[0]["region"]);
            Assert.Equal(40m, res.Rows[0]["total"]);
            Assert.Equal("S", res.Rows[1]["region"]);
            Assert.Equal(20m, res.Rows[1]["total"]);
        }

        [Fact]
        public async Task OrderByPositional_SortsByAggregateColumn()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var sql = @"
                SELECT region, SUM(amount) AS total
                FROM (VALUES ('N', 10), ('S', 30)) AS t(region, amount)
                GROUP BY region
                ORDER BY 2 DESC;";

            var res = await ev.ExecuteQuery(Parse(sql).Statements[0]).FirstAsync();

            Assert.Equal("S", res.Rows[0]["region"]);
            Assert.Equal(30m, res.Rows[0]["total"]);
            Assert.Equal("N", res.Rows[1]["region"]);
            Assert.Equal(10m, res.Rows[1]["total"]);
        }

        [Fact]
        public void GroupByPositional_OutOfRange_ReportsDiagnostic()
        {
            var sql = "SELECT region FROM (VALUES ('N')) AS t(region) GROUP BY 5;";
            var script = Parse(sql);
            Assert.Contains(script.Diagnostics, d => d.Message.Contains("out of range"));
        }

        [Fact]
        public void Positional_WithStarInSelectList_ReportsDiagnostic()
        {
            var sql = "SELECT * FROM (VALUES ('N', 1)) AS t(a, b) ORDER BY 1;";
            var script = Parse(sql);
            Assert.Contains(script.Diagnostics, d => d.Message.Contains("star projection"));
        }

        [Fact]
        public void Positional_WithQualifiedStarOrStarModifiers_ReportsDiagnostic()
        {
            var sql1 = "SELECT t.* FROM (VALUES ('N', 1)) AS t(a, b) ORDER BY 1;";
            Assert.Contains(Parse(sql1).Diagnostics, d => d.Message.Contains("star projection"));

            var sql2 = "SELECT * EXCLUDE (b) FROM (VALUES ('N', 1)) AS t(a, b) ORDER BY 1;";
            Assert.Contains(Parse(sql2).Diagnostics, d => d.Message.Contains("star projection"));
        }

        [Fact]
        public async Task GroupByAll_WithStarExclude_GroupsCorrectly()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var sql = @"
                SELECT * EXCLUDE (amount), SUM(amount) AS total
                FROM (VALUES ('N', 10), ('N', 30), ('S', 20)) AS t(region, amount)
                GROUP BY ALL
                ORDER BY region;";

            var res = await ev.ExecuteQuery(Parse(sql).Statements[0]).FirstAsync();

            Assert.Equal(2, res.Rows.Count);
            Assert.Equal("N", res.Rows[0]["region"]);
            Assert.Equal(40m, res.Rows[0]["total"]);
            Assert.Equal("S", res.Rows[1]["region"]);
            Assert.Equal(20m, res.Rows[1]["total"]);
        }

        [Fact]
        public void GroupByExpression_IsNotTreatedAsPositional()
        {
            // `1 + 1` is an arithmetic expression, not a position reference; it must parse without error.
            var sql = "SELECT COUNT(*) AS n FROM (VALUES (1), (2)) AS t(x) GROUP BY 1 + 1;";
            var serialized = Parse(sql).Statements[0].ToSql();
            Assert.Contains("GROUP BY", serialized);
        }

        [Fact]
        public void GroupByAll_RoundTripsThroughToSql()
        {
            var sql = "SELECT region, SUM(amount) AS total FROM t GROUP BY ALL;";
            var serialized = Parse(sql).Statements[0].ToSql();
            Assert.Contains("GROUP BY ALL", serialized);
        }

        private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();
    }
}
