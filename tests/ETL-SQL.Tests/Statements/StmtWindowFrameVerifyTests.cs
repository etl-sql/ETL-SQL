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
    // Verification (not new build): RANGE and GROUPS window frames already execute in-memory.
    public class StmtWindowFrameVerifyTests
    {
        [Fact]
        public async Task RangeFrame_IsValueBased_NotRowCount()
        {
            var rows = await Run(@"
                SELECT n, SUM(v) OVER (ORDER BY n RANGE BETWEEN 1 PRECEDING AND 1 FOLLOWING) AS s
                FROM (VALUES (1,10), (2,20), (2,5), (4,100)) AS t(n, v)
                ORDER BY n;");

            // n=1 -> values with n in [0,2] = 10+20+5 = 35
            // n=2 (both) -> n in [1,3] = 10+20+5 = 35
            // n=4 -> n in [3,5] = 100
            Assert.Equal(new[] { 35m, 35m, 35m, 100m }, rows.Select(r => (decimal)r["s"]!).ToArray());
        }

        [Fact]
        public async Task GroupsFrame_UsesPeerGroups()
        {
            var rows = await Run(@"
                SELECT n, SUM(v) OVER (ORDER BY n GROUPS BETWEEN 1 PRECEDING AND CURRENT ROW) AS s
                FROM (VALUES (1,10), (2,20), (2,5), (3,100)) AS t(n, v)
                ORDER BY n;");

            // groups by n: {1:[10]}, {2:[20,5]}, {3:[100]}
            // n=1 -> grp1 = 10; n=2 -> grp1+grp2 = 35; n=3 -> grp2+grp3 = 125
            Assert.Equal(new[] { 10m, 35m, 35m, 125m }, rows.Select(r => (decimal)r["s"]!).ToArray());
        }

        [Fact]
        public async Task RangeFrame_DefaultIsUnboundedToCurrentRow()
        {
            // Plain ORDER BY with no explicit frame defaults to RANGE UNBOUNDED PRECEDING .. CURRENT ROW
            var rows = await Run(@"
                SELECT n, SUM(v) OVER (ORDER BY n) AS running
                FROM (VALUES (1,10), (2,20), (3,30)) AS t(n, v)
                ORDER BY n;");

            Assert.Equal(new[] { 10m, 30m, 60m }, rows.Select(r => (decimal)r["running"]!).ToArray());
        }

        private static async Task<List<Row>> Run(string sql)
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var batches = await ev.ExecuteQuery(Parse(sql).Statements[0]).ToListAsync();
            return batches.SelectMany(b => b.Rows).ToList();
        }

        private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();
    }
}
