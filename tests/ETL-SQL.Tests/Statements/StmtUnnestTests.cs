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
    public class StmtUnnestTests
    {
        [Fact]
        public async Task Unnest_ArrayLiteral_ExpandsToRows()
        {
            var rows = await Run("SELECT u.Value AS v FROM UNNEST([10, 20, 30]) AS u ORDER BY u.Value;");
            Assert.Equal(new[] { 10m, 20m, 30m }, rows.Select(r => (decimal)r["v"]!).ToArray());
        }

        [Fact]
        public async Task Unnest_CrossApply_Correlated()
        {
            var rows = await Run(@"
                SELECT t.x AS x, u.Value AS v
                FROM (VALUES (1), (2)) AS t(x)
                CROSS APPLY UNNEST([t.x, t.x * 10]) AS u
                ORDER BY t.x, u.Value;");

            Assert.Equal(4, rows.Count);
            Assert.Equal(1m, rows[0]["v"]);   // t.x=1 -> 1
            Assert.Equal(10m, rows[1]["v"]);  // t.x=1 -> 10
            Assert.Equal(2m, rows[2]["v"]);   // t.x=2 -> 2
            Assert.Equal(20m, rows[3]["v"]);  // t.x=2 -> 20
        }

        [Fact]
        public async Task Flatten_NestedLists_FlattensOneLevel()
        {
            // Note: inner lists need 2+ elements; a single-element [x] is parsed as a quoted identifier.
            var rows = await Run("SELECT u.Value AS v FROM FLATTEN([[1, 2], [3, 4]]) AS u;");
            Assert.Equal(new[] { 1m, 2m, 3m, 4m }, rows.Select(r => (decimal)r["v"]!).OrderBy(v => v).ToArray());
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
