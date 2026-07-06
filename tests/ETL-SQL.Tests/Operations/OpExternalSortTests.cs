using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Engines;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Operations.Operations
{
    public class ExternalSortTests
    {
        private (Evaluator eval, ETL_SQL.Common.ILogger logger) BuildContext(int chunkSize = 100000)
        {
            var sp = DependencyInjectionSetup.BuildServiceProvider();
            var eval = sp.GetRequiredService<Evaluator>();
            eval.ExternalSortChunkSize = chunkSize;
            var logger = sp.GetRequiredService<ETL_SQL.Common.ILogger>();
            return (eval, logger);
        }

        [Fact]
        public async Task SortExternal_AcrossMultipleChunks_ProducesCorrectOrder()
        {
            // Forces 4 chunks: [10, 9], [8, 7], [6, 5], [4, 3, 2, 1] -> wait no, 2 per chunk
            var (eval, logger) = BuildContext(chunkSize: 2);
            var engine = new ExternalSortEngine(eval, logger);

            var rows = new List<Row>();
            for (int i = 10; i >= 1; i--)
            {
                rows.Add(new Row { ["id"] = i, ["val"] = $"val-{i}" });
            }

            var orderBy = new List<OrderByClause>
            {
                new OrderByClause(new IdentifierExpression("id"), false) // ASC
            };

            var startSpill = eval.Telemetry.TotalSpilledBytes;
            var sorted = await engine.SortExternal(rows, orderBy);

            Assert.Equal(10, sorted.Count);
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal((decimal)(i + 1), Convert.ToDecimal(sorted[i]["id"]));
            }

            Assert.True(eval.Telemetry.TotalSpilledBytes > startSpill, "Should have reported spilled bytes to context");
        }

        [Fact]
        public async Task SortExternal_DescendingOrder_ProducesCorrectOrder()
        {
            var (eval, logger) = BuildContext(chunkSize: 5);
            var engine = new ExternalSortEngine(eval, logger);

            var rows = new List<Row>();
            for (int i = 1; i <= 10; i++)
            {
                rows.Add(new Row { ["id"] = i });
            }

            var orderBy = new List<OrderByClause>
            {
                new OrderByClause(new IdentifierExpression("id"), true) // DESC
            };

            var sorted = await engine.SortExternal(rows, orderBy);

            Assert.Equal(10, sorted.Count);
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal((decimal)(10 - i), Convert.ToDecimal(sorted[i]["id"]));
            }
        }

        [Fact]
        public async Task SortExternal_ExceedsMergeFanIn_MultiPassMergeProducesCorrectOrder()
        {
            // chunkSize 2 over 300 rows -> 150 spill chunks, which exceeds the 64-way
            // MaxMergeFanIn cap and forces a multi-pass (reduction) merge. The output must
            // still be fully and correctly ordered with no rows lost or duplicated.
            var (eval, logger) = BuildContext(chunkSize: 2);
            var engine = new ExternalSortEngine(eval, logger);

            const int n = 300;
            // Deterministic shuffle so the test is reproducible but not pre-sorted.
            var ids = Enumerable.Range(1, n).ToList();
            for (int i = 0; i < n; i++)
            {
                int j = (i * 137 + 53) % n;
                (ids[i], ids[j]) = (ids[j], ids[i]);
            }

            var rows = ids.Select(i => new Row { ["id"] = i, ["val"] = $"val-{i}" }).ToList();

            var orderBy = new List<OrderByClause>
            {
                new OrderByClause(new IdentifierExpression("id"), false) // ASC
            };

            var startExtents = eval.Telemetry.SpillExtentCount;
            var sorted = await engine.SortExternal(rows, orderBy);

            Assert.Equal(n, sorted.Count);
            for (int i = 0; i < n; i++)
            {
                Assert.Equal((decimal)(i + 1), Convert.ToDecimal(sorted[i]["id"]));
                Assert.Equal($"val-{i + 1}", sorted[i]["val"]); // row payload travels with its key
            }
            Assert.InRange(engine.MaxConcurrentMergeReaders, 1, 64);
            Assert.Equal(2, engine.MergePassCount);
            Assert.Equal(3, engine.IntermediateMergeRunCount);
            Assert.Equal(153, eval.Telemetry.SpillExtentCount - startExtents);
        }

        // Note: no dedicated "early-flush under a 1-byte ceiling" test — asserting the guard tripped
        // depends on real-heap timing (non-deterministic in a shared-process run). The memory-guarded
        // early flush is exercised by the scale-cert repro (sort stays ~500MB at 10M); chunked
        // multi-pass merge correctness is covered by the multi-pass test above.

        [Fact]
        public async Task SortExternal_MultiColumnSort_ProducesCorrectOrder()
        {
            var (eval, logger) = BuildContext(chunkSize: 2);
            var engine = new ExternalSortEngine(eval, logger);

            var rows = new List<Row>
            {
                new Row { ["cat"] = "B", ["val"] = 1m },
                new Row { ["cat"] = "A", ["val"] = 2m },
                new Row { ["cat"] = "A", ["val"] = 1m },
                new Row { ["cat"] = "B", ["val"] = 2m },
            };

            var orderBy = new List<OrderByClause>
            {
                new OrderByClause(new IdentifierExpression("cat"), false), // cat ASC
                new OrderByClause(new IdentifierExpression("val"), true)   // val DESC
            };

            var sorted = await engine.SortExternal(rows, orderBy);

            Assert.Equal(4, sorted.Count);

            // Expected: A/2, A/1, B/2, B/1
            Assert.Equal("A", sorted[0]["cat"]); Assert.Equal(2m, Convert.ToDecimal(sorted[0]["val"]));
            Assert.Equal("A", sorted[1]["cat"]); Assert.Equal(1m, Convert.ToDecimal(sorted[1]["val"]));
            Assert.Equal("B", sorted[2]["cat"]); Assert.Equal(2m, Convert.ToDecimal(sorted[2]["val"]));
            Assert.Equal("B", sorted[3]["cat"]); Assert.Equal(1m, Convert.ToDecimal(sorted[3]["val"]));
        }
    }
}
