using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Engines;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Operations.Operations
{
    /// <summary>
    /// Verification tests for Grouping Set (ROLLUP, CUBE) spilling functionality in ExternalAggregateEngine.
    /// This ensures that multi-dimensional grouping correctly expands rows and substitutes NULLs.
    /// </summary>
    public class GroupingSetSpillingTests
    {
        private static (Evaluator eval, ILogger logger) BuildContext()
        {
            var sp = DependencyInjectionSetup.BuildServiceProvider();
            var eval = sp.GetRequiredService<Evaluator>();
            var logger = sp.GetRequiredService<ETL_SQL.Common.ILogger>();
            return (eval, logger);
        }

        private static IAsyncEnumerable<Row> MakeRows(int count, string[] cats, int[] subs)
        {
            return CreateRows(count, cats, subs).ToAsyncEnumerable();

            static IEnumerable<Row> CreateRows(int n, string[] cats, int[] subs)
            {
                for (int i = 0; i < n; i++)
                {
                    var row = new Row();
                    row["cat"] = cats[i % cats.Length];
                    row["sub"] = subs[i % subs.Length];
                    row["val"] = 1.0m;
                    yield return row;
                }
            }
        }

        [Fact]
        public async Task ROLLUP_Expansion_ProducesSubtotalsAndGrandTotal()
        {
            var (eval, logger) = BuildContext();
            var engine = new ExternalAggregateEngine(eval, logger);

            // 12 rows: explicitly interleaving to get (A,1), (A,2), (B,1), (B,2)
            var rows = new List<Row>
            {
                new Row {["cat"]="A", ["sub"]=1, ["val"]=1m},
                new Row {["cat"]="A", ["sub"]=2, ["val"]=1m},
                new Row {["cat"]="B", ["sub"]=1, ["val"]=1m},
                new Row {["cat"]="B", ["sub"]=2, ["val"]=1m},
                new Row {["cat"]="A", ["sub"]=1, ["val"]=1m},
                new Row {["cat"]="A", ["sub"]=2, ["val"]=1m},
                new Row {["cat"]="B", ["sub"]=1, ["val"]=1m},
                new Row {["cat"]="B", ["sub"]=2, ["val"]=1m},
                new Row {["cat"]="A", ["sub"]=1, ["val"]=1m},
                new Row {["cat"]="A", ["sub"]=2, ["val"]=1m},
                new Row {["cat"]="B", ["sub"]=1, ["val"]=1m},
                new Row {["cat"]="B", ["sub"]=2, ["val"]=1m},
            }.ToAsyncEnumerable();

            var groupBy = new List<Expression> { new IdentifierExpression("cat"), new IdentifierExpression("sub") };

            // ROLLUP(cat, sub)
            var groupingSet = new GroupingSetClause(GroupingSetType.Rollup, new List<List<Expression>>
            {
                new List<Expression> { new IdentifierExpression("cat"), new IdentifierExpression("sub") }
            });

            var finalColumns = new List<SelectColumn>
            {
                new SelectColumn(new IdentifierExpression("cat"), "cat"),
                new SelectColumn(new IdentifierExpression("sub"), "sub"),
                new SelectColumn(new FunctionCallExpression("SUM", new List<Expression>{ new IdentifierExpression("val") }), "total")
            };
            var colNames = new List<string> { "cat", "sub", "total" };

            var result = await engine.ApplyAggregationExternal(rows, groupBy, finalColumns, colNames, null, groupingSet).ToListAsync();

            // Expected groups for ROLLUP(cat, sub):
            // (A, 1), (A, 2), (B, 1), (B, 2)  -- 4 detail groups
            // (A, null), (B, null)            -- 2 cat-level subtotals
            // (null, null)                    -- 1 grand total
            Assert.Equal(7, result.Count);

            // Verify Grand Total
            var grandTotal = result.FirstOrDefault(r => r["cat"] == null && r["sub"] == null);
            Assert.NotNull(grandTotal);
            Assert.Equal(12m, Convert.ToDecimal(grandTotal["total"]));

            // Verify a Subtotal
            var subtotalA = result.FirstOrDefault(r => r["cat"]?.ToString() == "A" && r["sub"] == null);
            Assert.NotNull(subtotalA);
            Assert.Equal(6m, Convert.ToDecimal(subtotalA["total"])); // 6 rows for A

            // Verify Expansion Ratio
            Assert.Equal(3.0, eval.Telemetry.AggregateExpansionRatio, 1);
        }

        [Fact]
        public async Task CUBE_Expansion_ProducesFullPowerSet()
        {
            var (eval, logger) = BuildContext();
            var engine = new ExternalAggregateEngine(eval, logger);

            // 4 rows: cat A, sub 1
            var rows = MakeRows(4, new[] { "A" }, new[] { 1 });
            var groupBy = new List<Expression> { new IdentifierExpression("cat"), new IdentifierExpression("sub") };

            // CUBE(cat, sub)
            var groupingSet = new GroupingSetClause(GroupingSetType.Cube, new List<List<Expression>>
            {
                new List<Expression> { new IdentifierExpression("cat"), new IdentifierExpression("sub") }
            });

            var finalColumns = new List<SelectColumn>
            {
                new SelectColumn(new IdentifierExpression("cat"), "cat"),
                new SelectColumn(new IdentifierExpression("sub"), "sub"),
                new SelectColumn(new FunctionCallExpression("COUNT", new List<Expression>{ new IdentifierExpression("val") }), "cnt")
            };
            var colNames = new List<string> { "cat", "sub", "cnt" };

            var result = await engine.ApplyAggregationExternal(rows, groupBy, finalColumns, colNames, null, groupingSet).ToListAsync();

            // Expected groups for CUBE(cat, sub) with input (A, 1):
            // (A, 1), (A, null), (null, 1), (null, null)
            Assert.Equal(4, result.Count);

            Assert.Contains(result, r => r["cat"]?.ToString() == "A" && r["sub"] != null);
            Assert.Contains(result, r => r["cat"]?.ToString() == "A" && r["sub"] == null);
            Assert.Contains(result, r => r["cat"] == null && r["sub"]?.ToString() == "1");
            Assert.Contains(result, r => r["cat"] == null && r["sub"] == null);

            // Expansion Ratio for CUBE(2) is 2^2 = 4.0
            Assert.Equal(4.0, eval.Telemetry.AggregateExpansionRatio, 1);
        }

        [Fact]
        public async Task GROUPING_SETS_Explicit_ProducesOnlyRequestedSets()
        {
            var (eval, logger) = BuildContext();
            var engine = new ExternalAggregateEngine(eval, logger);

            var rows = MakeRows(10, new[] { "A", "B" }, new[] { 1, 2 });
            var groupBy = new List<Expression> { new IdentifierExpression("cat") };

            // GROUPING SETS ((cat), ())
            var groupingSet = new GroupingSetClause(GroupingSetType.GroupingSets, new List<List<Expression>>
            {
                new List<Expression> { new IdentifierExpression("cat") },
                new List<Expression> { } // Grand total
            });

            var finalColumns = new List<SelectColumn>
            {
                new SelectColumn(new IdentifierExpression("cat"), "cat"),
                new SelectColumn(new FunctionCallExpression("COUNT", new List<Expression>{ new IdentifierExpression("val") }), "cnt")
            };
            var colNames = new List<string> { "cat", "cnt" };

            var result = await engine.ApplyAggregationExternal(rows, groupBy, finalColumns, colNames, null, groupingSet).ToListAsync();

            // Expected: (A), (B), (null)
            Assert.Equal(3, result.Count);
            Assert.Contains(result, r => r["cat"]?.ToString() == "A" && r["cnt"] != null);
            Assert.Contains(result, r => r["cat"]?.ToString() == "B" && r["cnt"] != null);
            Assert.Contains(result, r => r["cat"] == null);
        }

        [Fact]
        public async Task HighScale_GroupingSets_SpillsToDiskCorrectly()
        {
            var (eval, logger) = BuildContext();
            var engine = new ExternalAggregateEngine(eval, logger);

            // Small amount of rows but many sets to force intermediate data volume
            var rows = MakeRows(100, new[] { "A", "B", "C" }, new[] { 1, 2, 3 });

            // CUBE of 3 columns = 8 sets per row
            var groupingSet = new GroupingSetClause(GroupingSetType.Cube, new List<List<Expression>>
            {
                new List<Expression> { new IdentifierExpression("cat"), new IdentifierExpression("sub"), new IdentifierExpression("val") }
            });

            var finalColumns = new List<SelectColumn>
            {
                new SelectColumn(new IdentifierExpression("cat"), "cat"),
                new SelectColumn(new FunctionCallExpression("COUNT", new List<Expression>{ new IdentifierExpression("*") }), "cnt")
            };
            var colNames = new List<string> { "cat", "cnt" };

            long spillBefore = eval.Telemetry.TotalSpilledBytes;

            await engine.ApplyAggregationExternal(rows, null, finalColumns, colNames, null, groupingSet).ToListAsync();

            // Should have spilled 100 * 8 = 800 intermediate rows to dynamic partitions
            Assert.True(eval.Telemetry.TotalSpilledBytes > spillBefore);
            Assert.Equal(8.0, eval.Telemetry.AggregateExpansionRatio, 1);
        }
    }
}
