using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.App;
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
    public class ExternalJoinTests
    {
        private (Evaluator eval, ETL_SQL.Common.ILogger logger) BuildContext(int partitions = 4)
        {
            var sp = DependencyInjectionSetup.BuildServiceProvider();
            var eval = sp.GetRequiredService<Evaluator>();
            eval.ExternalHashPartitions = partitions;
            var logger = sp.GetRequiredService<ETL_SQL.Common.ILogger>();
            return (eval, logger);
        }

        [Fact]
        public async Task ApplyHashJoinExternal_CorrectlyPartitionsAndJoins()
        {
            var (eval, logger) = BuildContext(partitions: 4);
            var engine = new ExternalJoinEngine(eval, logger);

            var leftRows = new List<Row>();
            for (int i = 0; i < 20; i++) leftRows.Add(new Row { ["id"] = i, ["lval"] = $"l-{i}" });

            var rightRows = new List<Row>();
            for (int i = 0; i < 20; i += 2) rightRows.Add(new Row { ["id"] = i, ["rval"] = $"r-{i}" });

            var join = new JoinClause(
                "INNER",
                new TableReference("right"),
                new BinaryExpression(new IdentifierExpression("id"), TokenType.EQUALS, new IdentifierExpression("id"))
            );

            var startSpill = eval.Telemetry.TotalSpilledBytes;
            var startParts = eval.Telemetry.PartitionsCount;
            var startPasses = eval.Telemetry.PartitionPassCount;

            var results = await engine.ApplyHashJoinExternal(
                leftRows.ToAsyncEnumerable(),
                rightRows.ToAsyncEnumerable(),
                join,
                new List<string> { "id" },
                new List<string> { "id" }).ToListAsync();

            Assert.Equal(10, results.Count);
            Assert.Equal(10, engine.ColumnarBuildRows);
            Assert.Equal(20, engine.ColumnarProbeRows);
            Assert.All(results, r => Assert.Equal(r["lval"]?.ToString().Replace("l-", ""), r["rval"]?.ToString().Replace("r-", "")));

            Assert.True(eval.Telemetry.TotalSpilledBytes > startSpill, "Should have reported spilled bytes");
            Assert.True(eval.Telemetry.PartitionsCount > startParts, "Should have reported used partition count");
            Assert.Equal(startPasses + 2, eval.Telemetry.PartitionPassCount); // left and right partition sweeps
        }

        [Fact]
        public async Task ApplyHashJoinExternal_LeftJoin_PreservesUnmatchedLeftRows()
        {
            var (eval, logger) = BuildContext(partitions: 2);
            var engine = new ExternalJoinEngine(eval, logger);

            var leftRows = new List<Row> { new Row { ["id"] = 1 }, new Row { ["id"] = 2 } };
            var rightRows = new List<Row> { new Row { ["id"] = 1 } };

            var join = new JoinClause(
                "LEFT",
                new TableReference("right"),
                new BinaryExpression(new IdentifierExpression("id"), TokenType.EQUALS, new IdentifierExpression("id"))
            );

            var results = await engine.ApplyHashJoinExternal(
                leftRows.ToAsyncEnumerable(),
                rightRows.ToAsyncEnumerable(),
                join,
                new List<string> { "id" },
                new List<string> { "id" }).ToListAsync();

            Assert.Equal(2, results.Count);
            Assert.Contains(results, r => Convert.ToInt32(r["id"]) == 2);
        }

        [Fact]
        public async Task BuildSampleCanIncreaseFanOutAboveConfiguredBaseline()
        {
            var (eval, logger) = BuildContext(partitions: 2);
            eval.OperatorMemoryGrantMB = 1;
            var saved = MemoryGrantArbiter.Shared.TotalBudgetBytes;
            MemoryGrantArbiter.Shared.TotalBudgetBytes = 0;
            try
            {
                var payload = new string('x', 1024);
                var right = Enumerable.Range(0, 4096)
                    .Select(id => new Row { ["id"] = id, ["payload"] = payload + id })
                    .ToAsyncEnumerable();
                var left = new[] { new Row { ["id"] = 7 } }.ToAsyncEnumerable();
                var engine = new ExternalJoinEngine(eval, logger);

                var results = await engine.ApplyHashJoinExternal(
                    left, right, InnerOnId,
                    new List<string> { "id" }, new List<string> { "id" }).ToListAsync();

                Assert.Single(results);
                Assert.True(engine.PartitionCount > 2);
            }
            finally
            {
                MemoryGrantArbiter.Shared.TotalBudgetBytes = saved;
            }
        }

        [Fact]
        public async Task ExactBuildEstimateCanReduceOversizedConfiguredBaseline()
        {
            var (eval, logger) = BuildContext(partitions: 64);
            eval.OperatorMemoryGrantMB = 1;
            var leftRows = Enumerable.Range(0, 16)
                .Select(id => new Row { ["id"] = id })
                .ToList();
            var rightRows = Enumerable.Range(0, 16)
                .Select(id => new Row { ["id"] = id, ["value"] = "r" + id })
                .ToList();
            var engine = new ExternalJoinEngine(eval, logger);

            var results = await engine.ApplyHashJoinExternal(
                leftRows.ToAsyncEnumerable(), rightRows.ToAsyncEnumerable(), InnerOnId,
                new List<string> { "id" }, new List<string> { "id" },
                knownBuildRowCount: rightRows.Count,
                knownBuildBytes: RowWidthEstimator.EstimateTotalBytes(rightRows)).ToListAsync();

            Assert.Equal(16, results.Count);
            Assert.True(engine.PartitionCount < 64);
        }

        // ── RAM governor ──────────────────────────────────────────────────────
        // JoinSpillThreshold is set huge so the row-count repartition never fires — the memory
        // guard is the only thing that can trip, isolating the governor path. The build (right)
        // side always buffers its rows, so heap growth is reliable (ceiling = 1 byte trips it).

        private static readonly JoinClause InnerOnId = new JoinClause(
            "INNER", new TableReference("right"),
            new BinaryExpression(new IdentifierExpression("id"), TokenType.EQUALS, new IdentifierExpression("id")));

        private static IAsyncEnumerable<Row> JoinRows(int count, int distinctKeys, string valPrefix, int padChars = 0) =>
            Build(count, distinctKeys, valPrefix, padChars).ToAsyncEnumerable();

        private static IEnumerable<Row> Build(int count, int distinctKeys, string valPrefix, int padChars = 0)
        {
            var pad = padChars > 0 ? new string('x', padChars) : null;
            for (int i = 0; i < count; i++)
            {
                var row = new Row { ["id"] = i % distinctKeys, [valPrefix] = $"{valPrefix}-{i}" };
                if (pad != null) row["pad"] = pad;
                yield return row;
            }
        }

        [Fact]
        public async Task Governor_SpillOrFail_HighCardinalityBuild_CompletesViaRepartition()
        {
            var (eval, logger) = BuildContext(partitions: 2);
            eval.JoinSpillThreshold = 10_000_000;   // disable row-count repartition
            eval.MemoryGovernorPolicy = MemoryGovernorPolicy.SpillOrFail;
            // Precise byte accounting: 256 KB is well below the ~3 MB build side, so the depth-0 build
            // trips the governor and must recursively repartition (4000 distinct keys split cleanly)
            // until each sub-partition's build fits — the path this test exercises. Private arbiter:
            // leftover shared reservations from earlier tests would shrink the budget unpredictably.
            eval.MemoryArbiter = new MemoryGrantArbiter(256 * 1024);

            // left: 4000 distinct keys once each (probe). right: same 4000 keys ×5 (build, 20000 rows).
            var left = JoinRows(4000, 4000, "lval");
            var right = JoinRows(20000, 4000, "rval");
            var engine = new ExternalJoinEngine(eval, logger);

            var results = await engine.ApplyHashJoinExternal(
                left, right, InnerOnId, new List<string> { "id" }, new List<string> { "id" }).ToListAsync();

            Assert.Equal(20000, results.Count); // each of 4000 left rows matches 5 right rows
        }


        [Fact]
        public async Task OversizedPartitionsAreRewrittenAsNativeBatches()
        {
            var (eval, logger) = BuildContext(partitions: 2);
            eval.JoinSpillThreshold = 10_000_000;
            eval.MemoryGovernorPolicy = MemoryGovernorPolicy.SpillOnly;
            eval.MemoryArbiter = new MemoryGrantArbiter(64 * 1024);

            // The engine sizes fan-out from the 20 sampled keys (32 partitions), and which keys share a
            // partition depends on CompoundKey's per-process hash seed. Each key's build alone must
            // therefore exceed the ceiling: 250 rows of ~540 packed bytes is about 2x 64 KiB. With
            // 128-character padding a key fit (~46 KB), and the test failed whenever no two keys
            // happened to share a partition (about 1 run in 2,500).
            var left = JoinRows(500, 20, "lval");
            var right = JoinRows(5000, 20, "rval", padChars: 512);
            var engine = new ExternalJoinEngine(eval, logger);

            var results = await engine.ApplyHashJoinExternal(
                left, right, InnerOnId,
                new List<string> { "id" }, new List<string> { "id" }).ToListAsync();

            Assert.Equal(125000, results.Count);
            // Every partition was oversized, so every probe and build row was rewritten natively.
            Assert.True(engine.ColumnarRepartitionRows >= 5500, $"Rewrote {engine.ColumnarRepartitionRows} rows.");
        }

        // Note: no "SpillOrFail throws" test here. That path requires the heap-growth guard to trip
        // at a specific moment, which is non-deterministic across a shared-process test run (a GC
        // freeing prior tests' garbage can offset the build's growth). The governor's bounded-memory
        // behavior is verified deterministically by the high-cardinality / churn tests below and by
        // the scale-cert repro; EnforcePolicy itself is a trivial policy switch.

        [Fact]
        public async Task Governor_SpillOnly_UnsplittableBuild_Churns()
        {
            var (eval, logger) = BuildContext(partitions: 2);
            eval.JoinSpillThreshold = 10_000_000;
            eval.MemoryGovernorPolicy = MemoryGovernorPolicy.SpillOnly;
            long saved = MemoryGrantArbiter.Shared.TotalBudgetBytes;
            MemoryGrantArbiter.Shared.TotalBudgetBytes = 1;
            try
            {
                var left = JoinRows(1, 1, "lval");        // single left key 0
                var right = JoinRows(20000, 1, "rval");   // 20000 right rows, key 0
                var engine = new ExternalJoinEngine(eval, logger);

                var results = await engine.ApplyHashJoinExternal(
                    left, right, InnerOnId, new List<string> { "id" }, new List<string> { "id" }).ToListAsync();

                Assert.Equal(20000, results.Count); // churn completes: 1 left × 20000 right matches
            }
            finally { MemoryGrantArbiter.Shared.TotalBudgetBytes = saved; }
        }

        [Fact]
        public async Task EagerPartitionDeletion_ShrinksLiveSpillFilesWhileStreaming()
        {
            var (eval, logger) = BuildContext(partitions: 4);
            eval.JoinSpillThreshold = 10_000_000;
            eval.MemoryArbiter = new MemoryGrantArbiter(16 * 1024 * 1024);

            var left = JoinRows(200, 64, "lval", padChars: 128);
            var right = JoinRows(200, 64, "rval", padChars: 128);
            var engine = new ExternalJoinEngine(eval, logger);

            var observedFileCounts = new List<int>();
            var observedLiveBytes = new List<long>();
            int matchedCount = 0;

            await foreach (var row in engine.ApplyHashJoinExternal(
                left, right, InnerOnId, new List<string> { "id" }, new List<string> { "id" }))
            {
                matchedCount++;
                if (matchedCount == 1 || matchedCount % 25 == 0)
                {
                    var files = Directory.GetFiles(eval.SpillStore.RootPath, "*.tmp");
                    observedFileCounts.Add(files.Length);
                    observedLiveBytes.Add(files.Sum(f => new FileInfo(f).Length));
                }
            }

            Assert.True(matchedCount > 0);
            Assert.True(observedFileCounts.Count >= 2);
            // Total initial partitions across left + right is 2 * 4 = 8; eager deletion shrinks live chunks during streaming.
            Assert.True(observedFileCounts[0] < 8, $"Expected build chunk deleted before first yield, observed {observedFileCounts[0]} files.");
            Assert.True(observedFileCounts[^1] < observedFileCounts[0],
                $"Expected live spill files to shrink across partitions ({observedFileCounts[0]} -> {observedFileCounts[^1]}).");
            Assert.True(observedLiveBytes[^1] < observedLiveBytes[0],
                $"Expected live spill bytes to shrink across partitions ({observedLiveBytes[0]} -> {observedLiveBytes[^1]}).");
            Assert.Empty(Directory.GetFiles(eval.SpillStore.RootPath, "*.tmp"));
        }

        [Fact]
        public async Task EagerPartitionDeletion_RecursiveRepartition_DeletesSupersededParentsBeforeSubPartitionProbe()
        {
            var (eval, logger) = BuildContext(partitions: 2);
            eval.JoinSpillThreshold = 10_000_000;
            eval.MemoryGovernorPolicy = MemoryGovernorPolicy.SpillOnly;
            eval.MemoryArbiter = new MemoryGrantArbiter(64 * 1024);

            var left = JoinRows(500, 20, "lval");
            var right = JoinRows(5000, 20, "rval", padChars: 512);
            var engine = new ExternalJoinEngine(eval, logger);

            int matched = 0;
            bool sawDepth1FileWithoutParentPair = false;
            await foreach (var _ in engine.ApplyHashJoinExternal(
                left, right, InnerOnId, new List<string> { "id" }, new List<string> { "id" }))
            {
                matched++;
                if (matched % 5000 == 1)
                {
                    var files = Directory.GetFiles(eval.SpillStore.RootPath, "*.tmp")
                        .Select(Path.GetFileName)
                        .ToArray();
                    // Whenever depth>=1 partitions exist, at least the superseded depth-0 parent pair must already be gone.
                    if (files.Any(f => f!.Contains("_d", StringComparison.Ordinal)))
                    {
                        var depth0Count = files.Count(f => !f!.Contains("_d", StringComparison.Ordinal));
                        Assert.True(depth0Count <= 2 * (engine.PartitionCount - 1),
                            $"Expected superseded depth-0 parent chunks deleted before sub-partition probe (depth0Count={depth0Count}, partitions={engine.PartitionCount}).");
                        sawDepth1FileWithoutParentPair = true;
                    }
                }
            }

            Assert.Equal(125000, matched);
            Assert.True(engine.ColumnarRepartitionRows >= 5500, $"Expected recursive repartitioning, rewrote {engine.ColumnarRepartitionRows} rows.");
            Assert.True(sawDepth1FileWithoutParentPair, "Superseded depth-0 parent chunks should be observed deleted before sub-partition probing.");
            Assert.Empty(Directory.GetFiles(eval.SpillStore.RootPath, "*.tmp"));
        }

        [Fact]
        public async Task EagerPartitionDeletion_UnsplittableFallback_DeletesUnusedChildPartitionsBeforeChurn()
        {
            var (eval, logger) = BuildContext(partitions: 2);
            eval.JoinSpillThreshold = 10_000_000;
            eval.MemoryGovernorPolicy = MemoryGovernorPolicy.SpillOnly;
            eval.MemoryArbiter = new MemoryGrantArbiter(1);

            var left = JoinRows(1, 1, "lval");
            var right = JoinRows(2000, 1, "rval", padChars: 64);
            var engine = new ExternalJoinEngine(eval, logger);

            int count = 0;
            bool anyChildFilesLeftDuringProbe = false;
            await foreach (var _ in engine.ApplyHashJoinExternal(
                left, right, InnerOnId, new List<string> { "id" }, new List<string> { "id" }))
            {
                count++;
                if (count == 1)
                {
                    anyChildFilesLeftDuringProbe = Directory.GetFiles(eval.SpillStore.RootPath, "*_d1_*.tmp").Length > 0;
                }
            }

            Assert.Equal(2000, count);
            Assert.False(anyChildFilesLeftDuringProbe, "Unused failed-split child partitions should be deleted before SpillOnly churn.");
            Assert.Empty(Directory.GetFiles(eval.SpillStore.RootPath, "*.tmp"));
        }

        [Fact]
        public async Task EagerPartitionDeletion_CancellationAndFailure_CleansAllPartitions()
        {
            var (eval, logger) = BuildContext(partitions: 4);
            eval.JoinSpillThreshold = 10_000_000;
            eval.MemoryArbiter = new MemoryGrantArbiter(16 * 1024 * 1024);

            // 1. Mid-stream cancellation cleans every remaining unconsumed partition.
            using var cts = new CancellationTokenSource();
            var engine = new ExternalJoinEngine(eval, logger);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                int seen = 0;
                await foreach (var _ in engine.ApplyHashJoinExternal(
                    JoinRows(40, 20, "lval"),
                    JoinRows(40, 20, "rval"),
                    InnerOnId,
                    new List<string> { "id" },
                    new List<string> { "id" },
                    cancellationToken: cts.Token))
                {
                    if (++seen == 2) cts.Cancel();
                }
            });
            Assert.Empty(Directory.GetFiles(eval.SpillStore.RootPath, "*.tmp"));

            // 2. Failure mid-probe (expression evaluation error) cleans every remaining partition.
            var failingCondition = new BinaryExpression(
                new IdentifierExpression("id"),
                TokenType.SLASH,
                new LiteralExpression(0m, TokenType.NUMBER));
            var failingJoin = new JoinClause("INNER", new TableReference("right"), failingCondition);
            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await engine.ApplyHashJoinExternal(
                    JoinRows(20, 10, "lval"),
                    JoinRows(20, 10, "rval"),
                    failingJoin,
                    new List<string> { "id" },
                    new List<string> { "id" }).ToListAsync();
            });
            Assert.Empty(Directory.GetFiles(eval.SpillStore.RootPath, "*.tmp"));
        }
    }
}
