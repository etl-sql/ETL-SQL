using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ETL_SQL.Core;
using ETL_SQL.App;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Common;

namespace ETL_SQL.Tests.Performance
{
    public class LargeScaleJoinPersistenceTests
    {
        private readonly ITestOutputHelper _output;

        public LargeScaleJoinPersistenceTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static Evaluator NewEvaluator() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        [Fact]
        public async Task SelectFallback_PreservesAllRows_CRB1()
        {
            // Left (primary) table has 120k rows. 
            // Fallback triggers at 100k. 
            // If CR-B1 is present, the first 100k rows are lost. 
            // Fixed version should return all 20k matches (Join to 20k rows on Right).
            
            const int LEFT_ROWS = 120_000;
            const int RIGHT_ROWS = 20_000;
            
            var e = NewEvaluator();
            
            // Build Left: 0 to 119,999
            var leftSchema = new TableSchema(new[] { "Id", "Val" });
            var leftTable = new DataTable();
            leftTable.SetColumns(new[] { "Id", "Val" });
            for (int i = 0; i < LEFT_ROWS; i++)
            {
                var r = new Row(leftSchema); 
                r["Id"] = (decimal)i; 
                r["Val"] = "L" + i; 
                r["left.Id"] = (decimal)i; 
                r["left.Val"] = "L" + i; 
                await leftTable.AddRowAsync(r);
            }
            var leftMem = new InMemoryDataSource();
            await leftMem.WriteBatches(new[] { leftTable }.ToAsyncEnumerable());
            e.Connections["#left"] = leftMem;

            // Build Right: 0 to 19,999 (should match first 20k of Left)
            var rightSchema = new TableSchema(new[] { "Id", "Val" });
            var rightTable = new DataTable();
            rightTable.SetColumns(new[] { "Id", "Val" });
            for (int i = 0; i < RIGHT_ROWS; i++)
            {
                var r = new Row(rightSchema); 
                r["Id"] = (decimal)i; 
                r["Val"] = "R" + i; 
                r["right.Id"] = (decimal)i; 
                r["right.Val"] = "R" + i; 
                await rightTable.AddRowAsync(r);
            }
            var rightMem = new InMemoryDataSource();
            await rightMem.WriteBatches(new[] { rightTable }.ToAsyncEnumerable());
            e.Connections["#right"] = rightMem;

            long before = e.TotalSpilledBytes;
            
            // This triggers the complex pipeline in SelectStatementHandler because of the JOIN
            var script = "SELECT L.Id FROM #left AS L INNER JOIN #right AS R ON L.Id = R.Id ORDER BY L.Id;";
            await e.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse());

            Assert.NotNull(e.LastResult);
            
            // If CR-B1 exists, we only get matches from rows 100k+, which is zero in this test case.
            // If fixed, we get all 20k matches.
            Assert.Equal(RIGHT_ROWS, e.LastResult.TotalRowsMatched);
            Assert.True(e.TotalSpilledBytes > before, "External fallback should have spilled bytes.");
            
            _output.WriteLine($"Total Matches: {e.LastResult.TotalRowsMatched:N0}. Spilled: {e.TotalSpilledBytes - before:N0} bytes.");
        }

        [Fact]
        public async Task ExternalJoin_NumericKeyConsistency_CRB4()
        {
            // Verifies that decimal keys match even after round-tripping through JSON on disk.
            // If CR-B4 is present, numbers are deserialized as 'long', and decimal != long comparison fails.

            const int COUNT = 1000;
            var e = NewEvaluator();
            
            var schema = new TableSchema(new[] { "Id", "V" });
            
            async IAsyncEnumerable<Row> Stream(decimal start, string prefix)
            {
                for (int i = 0; i < COUNT; i++)
                {
                    var r = new Row(schema);
                    var val = start + (decimal)i;
                    r["Id"] = val;
                    r[$"{prefix}.Id"] = val;
                    r["V"] = i;
                    yield return r;
                }
            }

            var join = new JoinClause("INNER", new TableReference("right"), 
                new BinaryExpression(new IdentifierExpression("left.Id"), TokenType.EQUALS, new IdentifierExpression("right.Id")));
            
            var engine = new ETL_SQL.Engine.Engines.ExternalJoinEngine(e, NullLogger.Instance);
            
            // Partitioning will force numbers into JSON; Reading will unwrap them.
            var result = await engine.ApplyHashJoinExternal(
                Stream(10.5m, "left"), Stream(10.5m, "right"), join, 
                new List<string> { "left.Id" }, new List<string> { "right.Id" });

            Assert.Equal(COUNT, result.Count);
            _output.WriteLine($"Joined {result.Count} rows with decimal keys successfully.");
        }
    }
}
