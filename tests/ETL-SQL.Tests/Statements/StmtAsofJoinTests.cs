using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    public class StmtAsofJoinTests
    {
        [Fact]
        public async Task AsofJoin_GreaterEqual_PicksMostRecentAtOrBefore()
        {
            var ev = await Setup(
                "CREATE TABLE #t (id INT, ts INT);",
                "INSERT INTO #t VALUES (1, 10), (2, 25), (3, 5);",
                "CREATE TABLE #q (bid INT, ts INT);",
                "INSERT INTO #q VALUES (100, 0), (200, 20), (300, 30);");

            var rows = await Query(ev, "SELECT t.id, q.bid FROM #t t ASOF JOIN #q q ON t.ts >= q.ts ORDER BY t.id;");

            Assert.Equal(3, rows.Count);
            Assert.Equal(100m, rows[0]["bid"]); // ts=10 -> q.ts=0
            Assert.Equal(200m, rows[1]["bid"]); // ts=25 -> q.ts=20
            Assert.Equal(100m, rows[2]["bid"]); // ts=5  -> q.ts=0
        }

        [Fact]
        public async Task AsofJoin_WithEqualityKey()
        {
            var ev = await Setup(
                "CREATE TABLE #t (sym NVARCHAR(5), id INT, ts INT);",
                "INSERT INTO #t VALUES ('A', 1, 10), ('B', 2, 10);",
                "CREATE TABLE #q (sym NVARCHAR(5), bid INT, ts INT);",
                "INSERT INTO #q VALUES ('A', 100, 5), ('A', 101, 12), ('B', 200, 8);");

            var rows = await Query(ev, "SELECT t.id, q.bid FROM #t t ASOF JOIN #q q ON t.sym = q.sym AND t.ts >= q.ts ORDER BY t.id;");

            Assert.Equal(2, rows.Count);
            Assert.Equal(100m, rows[0]["bid"]); // A, ts<=10 -> q.ts=5 (12 excluded)
            Assert.Equal(200m, rows[1]["bid"]); // B, ts<=10 -> q.ts=8
        }

        [Fact]
        public async Task AsofJoin_Inner_DropsUnmatched_LeftKeepsThem()
        {
            var ev = await Setup(
                "CREATE TABLE #t (id INT, ts INT);",
                "INSERT INTO #t VALUES (1, 1);",
                "CREATE TABLE #q (bid INT, ts INT);",
                "INSERT INTO #q VALUES (100, 5);");

            var inner = await Query(ev, "SELECT t.id, q.bid FROM #t t ASOF JOIN #q q ON t.ts >= q.ts;");
            Assert.Empty(inner); // no q.ts <= 1

            var left = await Query(ev, "SELECT t.id, q.bid FROM #t t ASOF LEFT JOIN #q q ON t.ts >= q.ts;");
            Assert.Single(left);
            Assert.Equal(1m, left[0]["id"]);
            Assert.Null(left[0]["bid"]);
        }

        [Fact]
        public async Task AsofJoin_LessEqual_PicksNearestAtOrAfter()
        {
            var ev = await Setup(
                "CREATE TABLE #t (id INT, ts INT);",
                "INSERT INTO #t VALUES (1, 10);",
                "CREATE TABLE #q (bid INT, ts INT);",
                "INSERT INTO #q VALUES (100, 5), (200, 12), (300, 20);");

            var rows = await Query(ev, "SELECT t.id, q.bid FROM #t t ASOF JOIN #q q ON t.ts <= q.ts;");

            Assert.Single(rows);
            Assert.Equal(200m, rows[0]["bid"]); // q.ts >= 10 -> min is 12
        }

        [Fact]
        public void AsofJoin_SerializesWithAsofKeyword()
        {
            var sql = "SELECT * FROM t ASOF JOIN q ON t.ts >= q.ts;";
            Assert.Contains("ASOF JOIN", Parse(sql).Statements[0].ToSql());

            var leftSql = "SELECT * FROM t ASOF LEFT JOIN q ON t.ts >= q.ts;";
            Assert.Contains("ASOF LEFT JOIN", Parse(leftSql).Statements[0].ToSql());
        }

        private static async Task<Evaluator> Setup(params string[] statements)
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            foreach (var s in statements) await ev.Evaluate(Parse(s));
            return ev;
        }

        private static async Task<List<Row>> Query(Evaluator ev, string sql)
        {
            var batches = await ev.ExecuteQuery(Parse(sql).Statements[0]).ToListAsync();
            return batches.SelectMany(b => b.Rows).ToList();
        }

        private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();
    }
}
