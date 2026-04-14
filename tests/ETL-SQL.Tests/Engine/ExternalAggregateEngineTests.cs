using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Engines;

namespace ETL_SQL.Tests.Engine
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
                finalColumns, colNames);

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

            long spillBefore = eval.TotalSpilledBytes;

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
                finalColumns, new List<string> { "category", "cnt" });

            // Spilling 60 rows should have written bytes to temp
            Assert.True(eval.TotalSpilledBytes > spillBefore,
                "TotalSpilledBytes should increase after external aggregation");
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
                new List<string> { "category", "cnt" });

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

            var result = await engine.ApplyAggregationExternal(rows, groupBy, columns, names);

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
                rows, null, finalColumns, new List<string> { "cnt" });

            // Global aggregate (no group by) should return a single row with count = 50
            Assert.Single(result);
            Assert.Equal(50m, Convert.ToDecimal(result[0]["cnt"]));
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
                new List<string> { "category", "cnt" });

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
