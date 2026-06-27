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
    public class StmtTier1SyntaxTests
    {
        [Fact]
        public async Task UnderscoreDigitSeparators_ParseAsNumbers()
        {
            var b = await RunFirstBatch("SELECT 1_000_000 AS big, 3_14 AS m FROM (VALUES (1)) AS t(x);");
            Assert.Equal(1000000m, b.Rows[0]["big"]);
            Assert.Equal(314m, b.Rows[0]["m"]);
        }

        [Fact]
        public async Task CountShorthand_EqualsCountStar()
        {
            var b = await RunFirstBatch("SELECT COUNT() AS c FROM (VALUES (1), (2), (3)) AS t(x);");
            Assert.Equal(3m, b.Rows[0]["c"]);
        }

        [Fact]
        public async Task TrailingCommas_AreToleratedInSelectGroupAndOrder()
        {
            var rows = await Run(@"
                SELECT region, SUM(amount) AS total,
                FROM (VALUES ('N',10), ('N',5), ('S',7)) AS t(region, amount)
                GROUP BY region,
                ORDER BY region,;");

            Assert.Equal(2, rows.Count);
            Assert.Equal("N", rows[0]["region"]);
            Assert.Equal(15m, rows[0]["total"]);
            Assert.Equal("S", rows[1]["region"]);
            Assert.Equal(7m, rows[1]["total"]);
        }

        [Fact]
        public async Task OrderByAll_SortsByEveryColumn()
        {
            var rows = await Run("SELECT region, amount FROM (VALUES ('S',2), ('N',1), ('N',3)) AS t(region, amount) ORDER BY ALL;");
            Assert.Equal(new[] { "N", "N", "S" }, rows.Select(r => r["region"]?.ToString()).ToArray());
            Assert.Equal(new[] { 1m, 3m, 2m }, rows.Select(r => (decimal)r["amount"]!).ToArray());
        }

        [Fact]
        public async Task OrderByAllDesc_SortsDescending()
        {
            var rows = await Run("SELECT region, amount FROM (VALUES ('S',2), ('N',1), ('N',3)) AS t(region, amount) ORDER BY ALL DESC;");
            Assert.Equal(new[] { "S", "N", "N" }, rows.Select(r => r["region"]?.ToString()).ToArray());
            Assert.Equal(new[] { 2m, 3m, 1m }, rows.Select(r => (decimal)r["amount"]!).ToArray());
        }

        [Fact]
        public async Task SelectStarExclude_DropsColumns()
        {
            var b = await RunFirstBatch("SELECT * EXCLUDE (secret) FROM (VALUES (1, 'a', 'x')) AS t(id, name, secret);");
            Assert.Contains("id", b.ColumnNames);
            Assert.Contains("name", b.ColumnNames);
            Assert.DoesNotContain("secret", b.ColumnNames);
        }

        [Fact]
        public async Task SelectStarReplace_SubstitutesColumnExpression()
        {
            var b = await RunFirstBatch("SELECT * REPLACE (UPPER(name) AS name) FROM (VALUES (1, 'abc')) AS t(id, name);");
            Assert.Equal(1m, b.Rows[0]["id"]);
            Assert.Equal("ABC", b.Rows[0]["name"]);
        }

        [Fact]
        public async Task SelectStarRename_RenamesColumns()
        {
            var b = await RunFirstBatch("SELECT * RENAME (id AS identifier) FROM (VALUES (1, 'a')) AS t(id, name);");
            Assert.Contains("identifier", b.ColumnNames);
            Assert.DoesNotContain("id", b.ColumnNames);
            Assert.Equal(1m, b.Rows[0]["identifier"]);
        }

        private static async Task<List<Row>> Run(string sql)
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var batches = await ev.ExecuteQuery(new Parser(new Lexer(sql).Tokenize(), sql).Parse().Statements[0]).ToListAsync();
            return batches.SelectMany(b => b.Rows).ToList();
        }

        private static async Task<DataTable> RunFirstBatch(string sql)
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var batches = await ev.ExecuteQuery(new Parser(new Lexer(sql).Tokenize(), sql).Parse().Statements[0]).ToListAsync();
            return batches[0];
        }
    }
}
