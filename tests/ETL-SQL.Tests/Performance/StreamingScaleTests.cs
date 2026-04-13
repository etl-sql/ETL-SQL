using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ETL_SQL.Core;
using ETL_SQL.App;
using ETL_SQL.Engine.Engines;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Common;

namespace ETL_SQL.Tests.Performance
{
    public class StreamingScaleTests
    {
        private readonly ITestOutputHelper _output;

        public StreamingScaleTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static Evaluator NewEvaluator() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        private static async Task<Evaluator> EvaluatorWithLargeSource(int rowCount, int categoryCount = 3)
        {
            var e = NewEvaluator();
            var schema = new TableSchema(new[] { "category", "value" });
            var table  = new DataTable();
            table.SetColumns(new[] { "category", "value" });

            for (int i = 0; i < rowCount; i++)
            {
                var r = new Row(schema);
                r["category"] = ((char)('A' + (i % categoryCount))).ToString();
                r["value"]    = (decimal)(i + 1);
                await table.AddRowAsync(r);
            }

            var mem = new InMemoryDataSource();
            await mem.WriteBatches(new[] { table }.ToAsyncEnumerable());
            e.Connections["#large"] = mem;
            return e;
        }

        [Fact]
        public async Task CR_T1_StreamingAggregate_WithHaving_FiltersCorrectly()
        {
            // 150k rows, 3 categories (A/B/C) -> 50k each.
            // HAVING COUNT(*) > 50000 -> Should return 0 rows if we use > 50000.
            // HAVING COUNT(*) = 50000 -> Should return all 3 categories.
            const int ROWS = 150_000;
            const int CATS = 3;
            var e = await EvaluatorWithLargeSource(ROWS, CATS);

            _output.WriteLine("Testing HAVING COUNT(*) = 50000 over 150k rows...");
            
            await e.Evaluate(new Parser(new Lexer(
                "SELECT category, COUNT(*) AS cnt FROM #large GROUP BY category HAVING COUNT(*) = 50000;")
                .Tokenize()).Parse());

            Assert.NotNull(e.LastResult);
            Assert.Equal(CATS, e.LastResult.Rows.Count);

            _output.WriteLine("Testing HAVING category = 'A' over 150k rows...");
            await e.Evaluate(new Parser(new Lexer(
                "SELECT category, COUNT(*) AS cnt FROM #large GROUP BY category HAVING category = 'A';")
                .Tokenize()).Parse());

            Assert.NotNull(e.LastResult);
            Assert.Single(e.LastResult.Rows);
            Assert.Equal("A", e.LastResult.Rows[0]["category"]?.ToString());
        }

        [Fact]
        public async Task CR_T2_ExternalSort_HandlesDuplicateKeys_Correctly()
        {
            // 250k rows, all with EXACTLY the same value in one column.
            // This tests that PriorityQueue in MergeChunks handles stability/priority correctly.
            const int ROWS = 250_000;
            var e = NewEvaluator();
            var schema = new TableSchema(new[] { "ID", "DupKey" });
            var rows = new List<Row>(ROWS);
            
            for (int i = 0; i < ROWS; i++)
            {
                var r = new Row(schema);
                r["ID"] = i;
                r["DupKey"] = 100; // All same
                rows.Add(r);
            }

            _output.WriteLine("Sorting 250k rows with duplicate keys...");
            var engine = new ExternalSortEngine(e, NullLogger.Instance);
            var orderBy = new List<OrderByClause> { 
                new OrderByClause(new IdentifierExpression("DupKey"), false),
                new OrderByClause(new IdentifierExpression("ID"), true) // Secondary sort (descending IDs)
            };

            var result = await engine.SortExternal(rows, orderBy);

            Assert.Equal(ROWS, result.Count);
            // Verify secondary sort preserved correctness
            Assert.Equal(ROWS - 1, Convert.ToInt32(result[0]["ID"]));
            Assert.Equal(0, Convert.ToInt32(result[ROWS - 1]["ID"]));
        }
    }
}
