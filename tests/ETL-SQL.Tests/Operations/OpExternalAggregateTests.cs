using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Engines;
using ETL_SQL.Engine.Planning;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Operations.Operations
{
    /// <summary>
    /// CQ-T6: Tests for ExternalAggregateEngine — the disk-spill aggregation path
    /// activated when the SELECT engine exceeds 100k buffered rows.
    ///
    /// These tests call ApplyAggregationExternal() directly to exercise the code path
    /// without needing 100k+ rows in memory.
    /// </summary>
    public class ExternalAggregateEngineTests
    {
        private static (Evaluator eval, ILogger logger) BuildContext()
        {
            var sp = DependencyInjectionSetup.BuildServiceProvider();
            var eval = sp.GetRequiredService<Evaluator>();
            var logger = sp.GetRequiredService<ETL_SQL.Common.ILogger>();
            return (eval, logger);
        }

        /// <summary>
        /// Creates a batch of rows, each with a single "category" column cycling
        /// through the given category values.
        /// </summary>
        private static IAsyncEnumerable<Row> MakeRows(int count, string[] categories)
        {
            return CreateRows(count, categories).ToAsyncEnumerable();

            static IEnumerable<Row> CreateRows(int n, string[] cats)
            {
                for (int i = 0; i < n; i++)
                {
                    var row = new Row();
                    row["category"] = cats[i % cats.Length];
                    row["value"] = (decimal)(i + 1);
                    yield return row;
                }
            }
        }

        [Fact]
        public async Task ApplyAggregationExternal_GroupBy_ProducesCorrectGroups()
        {
            var (eval, logger) = BuildContext();
            var engine = new ExternalAggregateEngine(eval, logger);

            var categories = new[] { "A", "B", "C" };
            var rows = MakeRows(90, categories); // 30 rows each category

            var groupByExpr = new IdentifierExpression("category");
            var countExpr = new FunctionCallExpression("COUNT", new List<Expression>
            {
                new IdentifierExpression("value")
            });

            var finalColumns = new List<SelectColumn>
            {
                new SelectColumn(new IdentifierExpression("category"), "category"),
                new SelectColumn(countExpr, "cnt")
            };
            var colNames = new List<string> { "category", "cnt" };

            var result = await engine.ApplyAggregationExternal(rows, new List<Expression> { groupByExpr },
                finalColumns, colNames).ToListAsync();

            // Three groups: A, B, C
            Assert.Equal(3, result.Count);

            var grouped = result.ToDictionary(r => r["category"]?.ToString() ?? "");
            Assert.True(grouped.ContainsKey("A"), "Group A should exist");
            Assert.True(grouped.ContainsKey("B"), "Group B should exist");
            Assert.True(grouped.ContainsKey("C"), "Group C should exist");
        }

        [Fact]
        public async Task ApplyAggregationExternal_AlwaysWritesToDisk_IncrementsTotalSpilledBytes()
        {
            var (eval, logger) = BuildContext();
            var engine = new ExternalAggregateEngine(eval, logger);

            long spillBefore = eval.Telemetry.TotalSpilledBytes;

            // ApplyAggregationExternal always spills to disk unconditionally for batch processing,
            // regardless of the 100k row memory threshold used by the higher-level SelectStatementHandler.
            var rows = MakeRows(60, new[] { "X", "Y" });
            var groupByExpr = new IdentifierExpression("category");
            var countExpr = new FunctionCallExpression("COUNT", new List<Expression>
            {
                new IdentifierExpression("value")
            });
            var finalColumns = new List<SelectColumn>
            {
                new SelectColumn(new IdentifierExpression("category"), "category"),
                new SelectColumn(countExpr, "cnt")
            };

            await engine.ApplyAggregationExternal(rows, new List<Expression> { groupByExpr },
                finalColumns, new List<string> { "category", "cnt" }).ToListAsync();

            // Spilling 60 rows should have written bytes to temp
            Assert.True(eval.Telemetry.TotalSpilledBytes > spillBefore,
                "TotalSpilledBytes should increase after external aggregation");
        }

        [Fact]
        public async Task NumericGroupedAggregateConsumesNativeSpillBatches()
        {
            var (eval, logger) = BuildContext();
            var savedBudget = MemoryGrantArbiter.Shared.TotalBudgetBytes;
            MemoryGrantArbiter.Shared.TotalBudgetBytes = 0;
            try
            {
                var rows = Enumerable.Range(0, 100)
                    .Select(id => new Row { ["group_id"] = id % 10, ["value"] = 1m })
                    .ToAsyncEnumerable();
                var groupBy = new List<Expression> { new IdentifierExpression("group_id") };
                var columns = new List<SelectColumn>
                {
                    new(new IdentifierExpression("group_id"), "group_id"),
                    new(new FunctionCallExpression("COUNT", new List<Expression>
                    {
                        new IdentifierExpression("value")
                    }), "cnt"),
                    new(new FunctionCallExpression("SUM", new List<Expression>
                    {
                        new IdentifierExpression("value")
                    }), "total")
                };
                var engine = new ExternalAggregateEngine(eval, logger);

                var result = await engine.ApplyAggregationExternal(
                    rows, groupBy, columns, new List<string> { "group_id", "cnt", "total" }).ToListAsync();

                Assert.Equal(10, result.Count);
                Assert.All(result, row => Assert.Equal(10m, Convert.ToDecimal(row["cnt"])));
                Assert.All(result, row => Assert.Equal(10m, Convert.ToDecimal(row["total"])));
                Assert.Equal(100, engine.ColumnarAggregateRows);
            }
            finally
            {
                MemoryGrantArbiter.Shared.TotalBudgetBytes = savedBudget;
            }
        }

        [Fact]
        public async Task GroupSampleCanIncreaseFanOutWithoutLosingRows()
        {
            var (eval, logger) = BuildContext();
            eval.ExternalHashPartitions = 2;
            eval.OperatorMemoryGrantMB = 1;
            var savedBudget = MemoryGrantArbiter.Shared.TotalBudgetBytes;
            MemoryGrantArbiter.Shared.TotalBudgetBytes = 0;
            try
            {
                var payload = new string('x', 1024);
                var rows = Enumerable.Range(0, 4096)
                    .Select(id => new Row
                    {
                        ["category"] = "g" + id,
                        ["value"] = 1m,
                        ["payload"] = payload + id
                    })
                    .ToAsyncEnumerable();
                var (groupBy, columns, names) = CountSumByCategory();
                var engine = new ExternalAggregateEngine(eval, logger);

                var result = await engine.ApplyAggregationExternal(rows, groupBy, columns, names).ToListAsync();

                Assert.Equal(4096, result.Count);
                Assert.Equal(4096m, result.Sum(row => Convert.ToDecimal(row["cnt"])));
                Assert.True(engine.PartitionCount > 2);
            }
            finally
            {
                MemoryGrantArbiter.Shared.TotalBudgetBytes = savedBudget;
            }
        }

        [Fact]
        public async Task ExactInputEstimateCanReduceOversizedConfiguredBaseline()
        {
            var (eval, logger) = BuildContext();
            eval.ExternalHashPartitions = 64;
            eval.OperatorMemoryGrantMB = 1;
            var rows = Enumerable.Range(0, 16)
                .Select(id => new Row { ["category"] = "g" + id, ["value"] = 1m })
                .ToList();
            var (groupBy, columns, names) = CountSumByCategory();
            var engine = new ExternalAggregateEngine(eval, logger);

            var result = await engine.ApplyAggregationExternal(
                rows.ToAsyncEnumerable(), groupBy, columns, names,
                knownRowCount: rows.Count,
                knownInputBytes: RowWidthEstimator.EstimateTotalBytes(rows)).ToListAsync();

            Assert.Equal(16, result.Count);
            Assert.True(engine.PartitionCount < 64);
        }

        [Fact]
        public async Task ApplyAggregationExternal_EmptyInput_ReturnsEmptyResult()
        {
            var (eval, logger) = BuildContext();
            var engine = new ExternalAggregateEngine(eval, logger);

            var groupByExpr = new IdentifierExpression("category");
            var countExpr = new FunctionCallExpression("COUNT", new List<Expression>
            {
                new IdentifierExpression("value")
            });
            var finalColumns = new List<SelectColumn>
            {
                new SelectColumn(new IdentifierExpression("category"), "category"),
                new SelectColumn(countExpr, "cnt")
            };

            var result = await engine.ApplyAggregationExternal(
                Array.Empty<Row>().ToAsyncEnumerable(),
                new List<Expression> { groupByExpr },
                finalColumns,
                new List<string> { "category", "cnt" }).ToListAsync();

            // Empty input with GROUP BY → empty result
            Assert.Empty(result);
        }

        [Fact]
        public async Task ApplyAggregationExternal_AllAggregates_ProducesCorrectResults()
        {
            var (eval, logger) = BuildContext();
            var engine = new ExternalAggregateEngine(eval, logger);

            // Setup rows with mixed values
            var rows = new List<Row>
            {
                new Row { ["cat"] = "A", ["sub"] = 1, ["val"] = 10m },
                new Row { ["cat"] = "A", ["sub"] = 1, ["val"] = 20m },
                new Row { ["cat"] = "B", ["sub"] = 2, ["val"] = 30m },
                new Row { ["cat"] = "B", ["sub"] = 2, ["val"] = 40m },
            }.ToAsyncEnumerable();

            var groupBy = new List<Expression> { new IdentifierExpression("cat"), new IdentifierExpression("sub") };
            var columns = new List<SelectColumn>
            {
                new SelectColumn(new IdentifierExpression("cat"), "cat"),
                new SelectColumn(new IdentifierExpression("sub"), "sub"),
                new SelectColumn(new FunctionCallExpression("SUM", new List<Expression>{ new IdentifierExpression("val") }), "s"),
                new SelectColumn(new FunctionCallExpression("MIN", new List<Expression>{ new IdentifierExpression("val") }), "mi"),
                new SelectColumn(new FunctionCallExpression("MAX", new List<Expression>{ new IdentifierExpression("val") }), "ma"),
                new SelectColumn(new FunctionCallExpression("AVG", new List<Expression>{ new IdentifierExpression("val") }), "av")
            };
            var names = new List<string> { "cat", "sub", "s", "mi", "ma", "av" };

            var result = await engine.ApplyAggregationExternal(rows, groupBy, columns, names).ToListAsync();

            Assert.Equal(2, result.Count);
            var a = result.First(r => r["cat"]?.ToString() == "A");
            Assert.Equal(30m, Convert.ToDecimal(a["s"]));
            Assert.Equal(10m, Convert.ToDecimal(a["mi"]));
            Assert.Equal(20m, Convert.ToDecimal(a["ma"]));
            Assert.Equal(15m, Convert.ToDecimal(a["av"]));
        }

        [Fact]
        public async Task ApplyAggregationExternal_NoGroupBy_GlobalCountReturnsOneRow()
        {
            var (eval, logger) = BuildContext();
            var engine = new ExternalAggregateEngine(eval, logger);

            var rows = MakeRows(50, new[] { "A", "B" });
            var countExpr = new FunctionCallExpression("COUNT", new List<Expression>
            {
                new IdentifierExpression("value")
            });
            var finalColumns = new List<SelectColumn>
            {
                new SelectColumn(countExpr, "cnt")
            };

            var result = await engine.ApplyAggregationExternal(
                rows, null, finalColumns, new List<string> { "cnt" }).ToListAsync();

            // Global aggregate (no group by) should return a single row with count = 50
            Assert.Single(result);
            Assert.Equal(50m, Convert.ToDecimal(result[0]["cnt"]));
        }

        // ── RAM governor ──────────────────────────────────────────────────────
        // These force a small memory ceiling so the in-memory group build trips the governor.
        // The governor uses precise byte accounting (bytes added per new group), so the ceiling must
        // be feasible — large enough that a sufficiently-split partition fits, but smaller than the
        // full group set — for SpillOrFail to complete by recursive repartitioning. ExternalHashPartitions=2
        // makes each repartition step roughly halve a partition's group count.

        private static IAsyncEnumerable<Row> MakeGroupedRows(int count, int distinctGroups)
        {
            return Create(count, distinctGroups).ToAsyncEnumerable();
            static IEnumerable<Row> Create(int n, int groups)
            {
                for (int i = 0; i < n; i++)
                    yield return new Row { ["category"] = "g" + (i % groups), ["value"] = (decimal)(i + 1) };
            }
        }

        private static (List<Expression> groupBy, List<SelectColumn> cols, List<string> names) CountSumByCategory()
        {
            var groupBy = new List<Expression> { new IdentifierExpression("category") };
            var cols = new List<SelectColumn>
            {
                new SelectColumn(new IdentifierExpression("category"), "category"),
                new SelectColumn(new FunctionCallExpression("COUNT", new List<Expression>{ new IdentifierExpression("value") }), "cnt"),
                new SelectColumn(new FunctionCallExpression("SUM", new List<Expression>{ new IdentifierExpression("value") }), "s"),
            };
            return (groupBy, cols, new List<string> { "category", "cnt", "s" });
        }

        [Fact]
        public async Task Governor_SpillOrFail_HighCardinality_CompletesViaRepartition()
        {
            var (eval, logger) = BuildContext();
            eval.ExternalHashPartitions = 2;
            eval.MemoryGovernorPolicy = MemoryGovernorPolicy.SpillOrFail;
            long savedBudget = MemoryGrantArbiter.Shared.TotalBudgetBytes;
            // 64 KB holds only a few hundred groups, so the 3000-group input trips the governor at
            // depth 0 and must recursively repartition (halving group count per level) until each
            // sub-partition fits — the path this test exercises.
            MemoryGrantArbiter.Shared.TotalBudgetBytes = 64 * 1024;
            try
            {
                const int rows = 30000, groups = 3000;
                var (groupBy, cols, names) = CountSumByCategory();
                var engine = new ExternalAggregateEngine(eval, logger);

                var result = await engine.ApplyAggregationExternal(
                    MakeGroupedRows(rows, groups), groupBy, cols, names).ToListAsync();

                // High cardinality CAN be split, so SpillOrFail completes via recursive repartition.
                Assert.Equal(groups, result.Count);
                Assert.All(result, r => Assert.Equal(10m, Convert.ToDecimal(r["cnt"]))); // each group appears rows/groups times
                decimal totalSum = result.Sum(r => Convert.ToDecimal(r["s"]));
                Assert.Equal((decimal)rows * (rows + 1) / 2, totalSum); // no rows lost or duplicated
            }
            finally { MemoryGrantArbiter.Shared.TotalBudgetBytes = savedBudget; }
        }

        // A single-group holistic GROUP_CONCAT buffers every row (GenericState), so its live heap
        // growth is large and unambiguous — making the governor trigger deterministic regardless of
        // GC timing (unlike an O(1) COUNT whose live state is a handful of bytes).
        private static (List<Expression> groupBy, List<SelectColumn> cols, List<string> names) ConcatByCategory()
        {
            var groupBy = new List<Expression> { new IdentifierExpression("category") };
            var cols = new List<SelectColumn>
            {
                new SelectColumn(new IdentifierExpression("category"), "category"),
                new SelectColumn(new FunctionCallExpression("GROUP_CONCAT", new List<Expression>{ new IdentifierExpression("value") }), "g"),
            };
            return (groupBy, cols, new List<string> { "category", "g" });
        }

        // Note: no "SpillOrFail throws" test. That path requires the heap-growth guard to trip at a
        // specific moment, which is non-deterministic across a shared-process test run (a GC freeing
        // prior tests' garbage can offset the build's growth). The governor's bounded-memory behavior
        // is verified deterministically by the high-cardinality / churn tests; EnforcePolicy is a
        // trivial policy switch.

        [Fact]
        public async Task Governor_SpillOnly_Churns_ProducesCorrectResult()
        {
            var (eval, logger) = BuildContext();
            eval.ExternalHashPartitions = 2;
            eval.MemoryGovernorPolicy = MemoryGovernorPolicy.SpillOnly;
            long savedBudget = MemoryGrantArbiter.Shared.TotalBudgetBytes;
            MemoryGrantArbiter.Shared.TotalBudgetBytes = 1;
            try
            {
                // Same unsplittable single group + holistic buffering, but churn mode completes.
                var (groupBy, cols, names) = ConcatByCategory();
                var engine = new ExternalAggregateEngine(eval, logger);

                var result = await engine.ApplyAggregationExternal(
                    MakeGroupedRows(20000, distinctGroups: 1), groupBy, cols, names).ToListAsync();

                Assert.Single(result);
                Assert.False(string.IsNullOrEmpty(result[0]["g"]?.ToString()));
            }
            finally { MemoryGrantArbiter.Shared.TotalBudgetBytes = savedBudget; }
        }

        [Fact]
        public async Task ApplyAggregationExternal_TempFilesAreCleanedUp()
        {
            var (eval, logger) = BuildContext();

            // Track temp files created during execution
            var tempRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ETL-SQL", "AggSpill");
            var dirsBefore = System.IO.Directory.Exists(tempRoot)
                ? System.IO.Directory.GetDirectories(tempRoot).ToHashSet()
                : new System.Collections.Generic.HashSet<string>();

            var engine = new ExternalAggregateEngine(eval, logger);
            var rows = MakeRows(30, new[] { "A", "B" });
            var countExpr = new FunctionCallExpression("COUNT", new List<Expression> { new IdentifierExpression("value") });

            await engine.ApplyAggregationExternal(
                rows,
                new List<Expression> { new IdentifierExpression("category") },
                new List<SelectColumn>
                {
                    new SelectColumn(new IdentifierExpression("category"), "category"),
                    new SelectColumn(countExpr, "cnt")
                },
                new List<string> { "category", "cnt" }).ToListAsync();

            // No new temp dirs should remain after completion (the engine's finally block deletes them)
            if (System.IO.Directory.Exists(tempRoot))
            {
                var dirsAfter = System.IO.Directory.GetDirectories(tempRoot).ToHashSet();
                var newDirs = dirsAfter.Except(dirsBefore).ToList();
                Assert.Empty(newDirs);
            }
        }
    }
}
