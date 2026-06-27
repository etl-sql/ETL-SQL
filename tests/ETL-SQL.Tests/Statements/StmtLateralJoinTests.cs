using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    public class StmtLateralJoinTests
    {
        [Fact]
        public async Task CrossJoinLateral_BehavesAsCrossApply()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var sql = @"
                SELECT t.id, x.doubled
                FROM (VALUES (1), (2), (3)) AS t(id)
                CROSS JOIN LATERAL (SELECT t.id * 2 AS doubled) AS x
                ORDER BY t.id;";

            var res = (await ev.ExecuteQuery(Parse(sql).Statements[0]).ToListAsync())
                .SelectMany(b => b.Rows).ToList();

            Assert.Equal(3, res.Count);
            Assert.Equal(2m, res[0]["doubled"]);
            Assert.Equal(4m, res[1]["doubled"]);
            Assert.Equal(6m, res[2]["doubled"]);
        }

        [Fact]
        public async Task CommaLateral_BehavesAsCrossApply()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var sql = @"
                SELECT t.id, x.tentimes
                FROM (VALUES (1), (2)) AS t(id), LATERAL (SELECT t.id * 10 AS tentimes) AS x
                ORDER BY t.id;";

            var res = (await ev.ExecuteQuery(Parse(sql).Statements[0]).ToListAsync())
                .SelectMany(b => b.Rows).ToList();

            Assert.Equal(2, res.Count);
            Assert.Equal(10m, res[0]["tentimes"]);
            Assert.Equal(20m, res[1]["tentimes"]);
        }

        [Fact]
        public async Task LeftJoinLateralOnTrue_BehavesAsOuterApply()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var sql = @"
                SELECT t.id, x.v
                FROM (VALUES (1), (2)) AS t(id)
                LEFT JOIN LATERAL (SELECT 99 AS v WHERE t.id = 1) AS x ON true
                ORDER BY t.id;";

            var res = (await ev.ExecuteQuery(Parse(sql).Statements[0]).ToListAsync())
                .SelectMany(b => b.Rows).ToList();

            Assert.Equal(2, res.Count);
            Assert.Equal(99m, res[0]["v"]);
            Assert.Null(res[1]["v"]);
        }

        [Fact]
        public async Task JoinLateralWithOnPredicate_FiltersCombinedRows()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            // ON x.v > 15 must drop the id=1 row (v=10) while keeping id=2 (20) and id=3 (30).
            var sql = @"
                SELECT t.id, x.v
                FROM (VALUES (1), (2), (3)) AS t(id)
                JOIN LATERAL (SELECT t.id * 10 AS v) AS x ON x.v > 15
                ORDER BY t.id;";

            var res = (await ev.ExecuteQuery(Parse(sql).Statements[0]).ToListAsync())
                .SelectMany(b => b.Rows).ToList();

            Assert.Equal(2, res.Count);
            Assert.Equal(2m, res[0]["id"]);
            Assert.Equal(20m, res[0]["v"]);
            Assert.Equal(3m, res[1]["id"]);
            Assert.Equal(30m, res[1]["v"]);
        }

        [Fact]
        public void Lateral_SerializesToApplyEquivalent()
        {
            var crossSql = "SELECT t.id FROM t CROSS JOIN LATERAL (SELECT 1 AS v) AS x;";
            Assert.Contains("CROSS APPLY", Parse(crossSql).Statements[0].ToSql());

            var leftSql = "SELECT t.id FROM t LEFT JOIN LATERAL (SELECT 1 AS v) AS x ON true;";
            Assert.Contains("OUTER APPLY", Parse(leftSql).Statements[0].ToSql());
        }

        private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();
    }
}
