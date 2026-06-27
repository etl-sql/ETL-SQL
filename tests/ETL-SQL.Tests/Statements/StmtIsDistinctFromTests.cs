using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    public class StmtIsDistinctFromTests
    {
        [Fact]
        public async Task IsDistinctFrom_TruthTable_TreatsNullAsValue()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var sql = @"
                SELECT
                    CASE WHEN 1 IS DISTINCT FROM 2 THEN 'T' ELSE 'F' END AS a,
                    CASE WHEN 1 IS DISTINCT FROM 1 THEN 'T' ELSE 'F' END AS b,
                    CASE WHEN NULL IS DISTINCT FROM 1 THEN 'T' ELSE 'F' END AS c,
                    CASE WHEN NULL IS DISTINCT FROM NULL THEN 'T' ELSE 'F' END AS d,
                    CASE WHEN NULL IS NOT DISTINCT FROM NULL THEN 'T' ELSE 'F' END AS e,
                    CASE WHEN 2 IS NOT DISTINCT FROM 2 THEN 'T' ELSE 'F' END AS f,
                    CASE WHEN 1 IS NOT DISTINCT FROM 2 THEN 'T' ELSE 'F' END AS g,
                    CASE WHEN NULL IS NOT DISTINCT FROM 1 THEN 'T' ELSE 'F' END AS h
                FROM (VALUES (1)) AS t(x);";

            var res = await ev.ExecuteQuery(Parse(sql).Statements[0]).FirstAsync();

            Assert.Equal("T", res.Rows[0]["a"]); // 1 <> 2          -> distinct
            Assert.Equal("F", res.Rows[0]["b"]); // 1 = 1           -> not distinct
            Assert.Equal("T", res.Rows[0]["c"]); // NULL vs 1       -> distinct
            Assert.Equal("F", res.Rows[0]["d"]); // NULL vs NULL    -> not distinct
            Assert.Equal("T", res.Rows[0]["e"]); // NULL = NULL     -> null-safe equal
            Assert.Equal("T", res.Rows[0]["f"]); // 2 = 2           -> null-safe equal
            Assert.Equal("F", res.Rows[0]["g"]); // 1 <> 2          -> not equal
            Assert.Equal("F", res.Rows[0]["h"]); // NULL vs 1       -> not equal
        }

        [Fact]
        public async Task IsNotDistinctFrom_MatchesNullsInWhere()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var sql = @"
                SELECT id
                FROM (VALUES (1, 'a'), (2, NULL), (3, 'b')) AS t(id, val)
                WHERE val IS NOT DISTINCT FROM NULL;";

            var res = await ev.ExecuteQuery(Parse(sql).Statements[0]).FirstAsync();

            Assert.Single(res.Rows);
            Assert.Equal(2m, res.Rows[0]["id"]);
        }

        [Fact]
        public async Task IsDistinctFrom_IncludesNullRows_UnlikePlainInequality()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            // Plain `val <> 'a'` would drop the NULL row (3VL); IS DISTINCT FROM keeps it.
            var sql = @"
                SELECT id
                FROM (VALUES (1, 'a'), (2, NULL), (3, 'b')) AS t(id, val)
                WHERE val IS DISTINCT FROM 'a'
                ORDER BY id;";

            var res = await ev.ExecuteQuery(Parse(sql).Statements[0]).FirstAsync();

            Assert.Equal(2, res.Rows.Count);
            Assert.Equal(2m, res.Rows[0]["id"]);
            Assert.Equal(3m, res.Rows[1]["id"]);
        }

        [Fact]
        public void IsDistinctFrom_RoundTripsThroughToSql()
        {
            var sql = "SELECT x FROM t WHERE a IS NOT DISTINCT FROM b;";
            var serialized = Parse(sql).Statements[0].ToSql();
            Assert.Contains("IS NOT DISTINCT FROM", serialized);

            var sql2 = "SELECT x FROM t WHERE a IS DISTINCT FROM b;";
            var serialized2 = Parse(sql2).Statements[0].ToSql();
            Assert.Contains("IS DISTINCT FROM", serialized2);
            Assert.DoesNotContain("IS NOT DISTINCT FROM", serialized2);
        }

        private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();
    }
}
