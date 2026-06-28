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
    /// <summary>
    /// Lateral column aliases: a SELECT item may reference an alias defined by an earlier
    /// item in the same SELECT list. Implemented by inlining the earlier expression at bind
    /// time. A real source column always wins over an alias of the same name.
    /// </summary>
    public class StmtLateralColumnAliasTests
    {
        [Fact]
        public async Task LateralAlias_BasicArithmetic()
        {
            var b = await RunFirstBatch(
                "SELECT a + b AS total, total * 2 AS dt FROM (VALUES (1, 2)) AS t(a, b);");
            Assert.Equal(3m, b.Rows[0]["total"]);
            Assert.Equal(6m, b.Rows[0]["dt"]);
        }

        [Fact]
        public async Task LateralAlias_Chained()
        {
            var b = await RunFirstBatch(
                "SELECT a * 2 AS x, x + 1 AS y, y * 10 AS z FROM (VALUES (5)) AS t(a);");
            Assert.Equal(10m, b.Rows[0]["x"]);
            Assert.Equal(11m, b.Rows[0]["y"]);
            Assert.Equal(110m, b.Rows[0]["z"]);
        }

        [Fact]
        public async Task LateralAlias_UsedInFunctionCall()
        {
            var b = await RunFirstBatch(
                "SELECT a + b AS s, ABS(s - 100) AS d FROM (VALUES (10, 20)) AS t(a, b);");
            Assert.Equal(30m, b.Rows[0]["s"]);
            Assert.Equal(70m, b.Rows[0]["d"]);
        }

        [Fact]
        public async Task LateralAlias_UsedInCase()
        {
            var b = await RunFirstBatch(
                "SELECT a + b AS total, CASE WHEN total > 5 THEN 'big' ELSE 'small' END AS label " +
                "FROM (VALUES (4, 5)) AS t(a, b);");
            Assert.Equal(9m, b.Rows[0]["total"]);
            Assert.Equal("big", b.Rows[0]["label"]);
        }

        [Fact]
        public async Task RealColumn_WinsOverAliasOfSameName()
        {
            // 'a' is a real source column; an alias named 'a' must not shadow it for later items.
            var b = await RunFirstBatch(
                "SELECT b AS a, a + 100 AS r FROM (VALUES (1, 2)) AS t(a, b);");
            Assert.Equal(2m, b.Rows[0]["a"]);   // alias a = b = 2
            Assert.Equal(101m, b.Rows[0]["r"]); // a here is the SOURCE column a = 1, so 1 + 100
        }

        [Fact]
        public async Task OrderBy_ByAlias_Works()
        {
            var b = await RunFirstBatch(
                "SELECT a, a * -1 AS neg FROM (VALUES (1), (3), (2)) AS t(a) ORDER BY neg;");
            Assert.Equal(3m, b.Rows[0]["a"]);
            Assert.Equal(2m, b.Rows[1]["a"]);
            Assert.Equal(1m, b.Rows[2]["a"]);
        }

        private static async Task<DataTable> RunFirstBatch(string sql)
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var batches = await ev.ExecuteQuery(Parse(sql).Statements[0]).ToListAsync();
            return batches[0];
        }

        private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();
    }
}
