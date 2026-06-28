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
    public class StmtSetOpExtensionsTests
    {
        [Fact]
        public async Task UnionByName_AlignsColumnsByName()
        {
            var b = await RunFirstBatch(@"
                SELECT 1 AS a, 2 AS b
                UNION BY NAME
                SELECT 20 AS b, 10 AS a;");

            Assert.Equal(new[] { "a", "b" }, b.ColumnNames.ToArray());
            var rows = b.Rows.OrderBy(r => (decimal)r["a"]!).ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal(1m, rows[0]["a"]);
            Assert.Equal(2m, rows[0]["b"]);
            Assert.Equal(10m, rows[1]["a"]);
            Assert.Equal(20m, rows[1]["b"]);
        }

        [Fact]
        public async Task UnionAllByName_FillsMissingColumnsWithNull()
        {
            var b = await RunFirstBatch(@"
                SELECT 1 AS a, 2 AS b
                UNION ALL BY NAME
                SELECT 3 AS a;");

            Assert.Equal(new[] { "a", "b" }, b.ColumnNames.ToArray());
            var rows = b.Rows.OrderBy(r => (decimal)r["a"]!).ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal(2m, rows[0]["b"]);
            Assert.Equal(3m, rows[1]["a"]);
            Assert.Null(rows[1]["b"]);
        }

        [Fact]
        public async Task Minus_IsAliasForExcept()
        {
            var rows = await Run(@"
                SELECT x FROM (VALUES (1), (2), (3)) AS t(x)
                MINUS
                SELECT x FROM (VALUES (2)) AS s(x);");

            var vals = rows.Select(r => (decimal)r["x"]!).OrderBy(v => v).ToList();
            Assert.Equal(new[] { 1m, 3m }, vals);
        }

        private static async Task<List<Row>> Run(string sql)
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var batches = await ev.ExecuteQuery(Parse(sql).Statements[0]).ToListAsync();
            return batches.SelectMany(b => b.Rows).ToList();
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
