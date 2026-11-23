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
using ETL_SQL.Engine.Spill;
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

        [Fact]
        public void PackedBuildTable_UsesBudgetCappedPooledSlabsFlatBucketChainsAndZeroesOnDispose()
        {
            const long budgetBytes = 16 * 1024;
            var table = new ExternalJoinEngine.PackedBuildTable(budgetBytes);
            Assert.Equal(ExternalJoinEngine.PackedBuildTable.MinSlabBytes, table.TargetSlabBytes);
            Assert.Equal(4096, table.TargetSlabBytes);

            var columns = new[] { "Key", "SecretPayload" };
            table.Columns.AddRange(columns);
            var guard = new MemoryBudgetGuard(budgetBytes);
            using var packer = new RowPacker();

            var dupKey = CompoundKey.CreateJoinKey(caseSensitive: false, new object?[] { "dup" });
            var soloKey = CompoundKey.CreateJoinKey(caseSensitive: false, new object?[] { "solo" });

            var expectedDupSecrets = new List<string>();
            for (int i = 0; i < 40; i++)
            {
                string secret = $"SECRET_SLAB_ROW_{i:D2}_PAYLOAD";
                var key = i % 10 == 0 ? soloKey : dupKey;
                if (i % 10 != 0) expectedDupSecrets.Add(secret);
                var row = new Row { ["Key"] = i % 10 == 0 ? "solo" : "dup", ["SecretPayload"] = secret };
                var (rowIdx, byteLen) = packer.PackInto(row, columns, table);
                table.IndexRow(key, rowIdx, byteLen, guard);
            }

            Assert.Equal(40, table.RowCount);
            Assert.True(table.SlabCount >= 1 && table.SlabCount < table.RowCount,
                $"Expected multiple rows packed per slab, saw {table.SlabCount} slabs for {table.RowCount} rows.");

            Assert.True(table.TryGetChain(dupKey, out var chain));
            var observedDupSecrets = new List<string?>();
            for (int idx = chain.Head; idx >= 0; idx = table.GetNextInBucket(idx))
                observedDupSecrets.Add(table.UnpackRow(idx)["SecretPayload"]?.ToString());

            Assert.Equal(expectedDupSecrets, observedDupSecrets);

            var slabs = table.GetSlabBuffersForTests();
            Assert.Contains(slabs, slab => slab.Used > 0 && slab.Buffer.AsSpan(0, slab.Used).IndexOf((byte)'S') >= 0);

            table.Dispose();
            Assert.Equal(0, table.RowCount);
            Assert.Equal(0, table.SlabCount);
            foreach (var slab in slabs)
            {
                for (int i = 0; i < slab.Used; i++)
                    Assert.Equal((byte)0, slab.Buffer[i]);
            }
        }

        [Fact]
        public void JoinKey_PreservesCompoundKeyNormalizationAndEqualityRules()
        {
            Assert.True(JoinKey.FromValue(null).IsNull);
            Assert.True(JoinKey.FromValue(DBNull.Value).IsNull);

            var dec1 = JoinKey.FromValue(1m);
            var dec10 = JoinKey.FromValue(1.0m);
            var dec100 = JoinKey.FromValue(1.00m);
            var str1 = JoinKey.FromValue("1");
            var str100 = JoinKey.FromValue(" 1.00 ");
            var int1 = JoinKey.FromValue(1);
            var short1 = JoinKey.FromValue((short)1);

            Assert.True(JoinKey.Equals(dec1, dec10, caseSensitive: false));
            Assert.True(JoinKey.Equals(dec1, dec100, caseSensitive: false));
            Assert.True(JoinKey.Equals(dec1, str1, caseSensitive: false));
            Assert.True(JoinKey.Equals(dec1, str100, caseSensitive: false));
            Assert.True(JoinKey.Equals(dec1, int1, caseSensitive: false));
            Assert.True(JoinKey.Equals(dec1, short1, caseSensitive: false));

            int hDec = JoinKey.ComputeHashCode(0, dec1, ReadOnlySpan<JoinKey>.Empty, caseSensitive: false);
            Assert.Equal(hDec, JoinKey.ComputeHashCode(0, dec10, ReadOnlySpan<JoinKey>.Empty, caseSensitive: false));
            Assert.Equal(hDec, JoinKey.ComputeHashCode(0, str1, ReadOnlySpan<JoinKey>.Empty, caseSensitive: false));
            Assert.Equal(hDec, JoinKey.ComputeHashCode(0, short1, ReadOnlySpan<JoinKey>.Empty, caseSensitive: false));

            // Leading-zero codes stay strings and do not collapse into numbers.
            var code001 = JoinKey.FromValue("001");
            Assert.False(JoinKey.Equals(code001, dec1, caseSensitive: false));
            Assert.True(JoinKey.Equals(code001, JoinKey.FromValue("001"), caseSensitive: false));

            var upper = JoinKey.FromValue("Alpha");
            var lower = JoinKey.FromValue("alpha");
            Assert.True(JoinKey.Equals(upper, lower, caseSensitive: false));
            Assert.Equal(
                JoinKey.ComputeHashCode(0, upper, ReadOnlySpan<JoinKey>.Empty, caseSensitive: false),
                JoinKey.ComputeHashCode(0, lower, ReadOnlySpan<JoinKey>.Empty, caseSensitive: false));
            Assert.False(JoinKey.Equals(upper, lower, caseSensitive: true));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(4)]
        public async Task ApplyHashJoinExternal_DecimalScale_NumericStrings_AndDuplicateFanOut(int partitions)
        {
            var (eval, logger) = BuildContext(partitions: partitions);
            var engine = new ExternalJoinEngine(eval, logger);

            var leftRows = new List<Row>
            {
                new() { ["id"] = "003", ["lval"] = "L003" },
                new() { ["id"] = 1m, ["lval"] = "L1" },
                new() { ["id"] = "2", ["lval"] = "L2" },
                new() { ["id"] = 4m, ["lval"] = "L4a" },
                new() { ["id"] = 4.0m, ["lval"] = "L4b" },
                new() { ["id"] = 5m, ["lval"] = "L5" },
            };

            var rightRows = new List<Row>
            {
                new() { ["id"] = "009", ["rval"] = "R009" },
                new() { ["id"] = 1.0m, ["rval"] = "R1" },
                new() { ["id"] = 2m, ["rval"] = "R2a" },
                new() { ["id"] = 2.00m, ["rval"] = "R2b" },
                new() { ["id"] = 3m, ["rval"] = "R3" },
                new() { ["id"] = 4.00m, ["rval"] = "R4a" },
                new() { ["id"] = 4m, ["rval"] = "R4b" },
                new() { ["id"] = (short)5, ["rval"] = "R5" },
            };

            var results = await engine.ApplyHashJoinExternal(
                leftRows.ToAsyncEnumerable(),
                rightRows.ToAsyncEnumerable(),
                InnerOnId,
                new List<string> { "id" },
                new List<string> { "id" }).ToListAsync();

            var pairs = results.Select(r => $"{r["lval"]}:{r["rval"]}").OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.Equal(
                new[] { "L1:R1", "L2:R2a", "L2:R2b", "L4a:R4a", "L4a:R4b", "L4b:R4a", "L4b:R4b", "L5:R5" },
                pairs);
        }

        [Fact]
        public async Task ApplyHashJoinExternal_NullKeysAndOuterPartitions_PreserveUnmatchedAndExcludeNullMatches()
        {
            var leftRows = new List<Row>
            {
                new() { ["id"] = null, ["lval"] = "L_null" },
                new() { ["id"] = 1, ["lval"] = "L1" },
                new() { ["id"] = 2, ["lval"] = "L2_only" },
                new() { ["id"] = 3, ["lval"] = "L3_only" },
            };

            var rightRows = new List<Row>
            {
                new() { ["id"] = null, ["rval"] = "R_null" },
                new() { ["id"] = 1, ["rval"] = "R1" },
                new() { ["id"] = 4, ["rval"] = "R4_only" },
                new() { ["id"] = 5, ["rval"] = "R5_only" },
            };

            // INNER: NULL keys never match.
            {
                var (eval, logger) = BuildContext(partitions: 4);
                var engine = new ExternalJoinEngine(eval, logger);
                var inner = await engine.ApplyHashJoinExternal(
                    leftRows.ToAsyncEnumerable(),
                    rightRows.ToAsyncEnumerable(),
                    InnerOnId,
                    new List<string> { "id" },
                    new List<string> { "id" }).ToListAsync();

                Assert.Single(inner);
                Assert.Equal("L1", inner[0]["lval"]);
                Assert.Equal("R1", inner[0]["rval"]);
            }

            // FULL OUTER: preserves unmatched rows (including NULL keys) across spill partitions.
            {
                var (eval, logger) = BuildContext(partitions: 4);
                var engine = new ExternalJoinEngine(eval, logger);
                var fullJoin = new JoinClause(
                    "FULL OUTER",
                    new TableReference("right"),
                    new BinaryExpression(new IdentifierExpression("id"), TokenType.EQUALS, new IdentifierExpression("id")));

                var full = await engine.ApplyHashJoinExternal(
                    leftRows.ToAsyncEnumerable(),
                    rightRows.ToAsyncEnumerable(),
                    fullJoin,
                    new List<string> { "id" },
                    new List<string> { "id" }).ToListAsync();

                Assert.Equal(7, full.Count);
                Assert.Contains(full, r => r["lval"]?.ToString() == "L_null" && r["rval"] == null);
                Assert.Contains(full, r => r["rval"]?.ToString() == "R_null" && r["lval"] == null);
                Assert.Contains(full, r => r["lval"]?.ToString() == "L2_only" && r["rval"] == null);
                Assert.Contains(full, r => r["lval"]?.ToString() == "L3_only" && r["rval"] == null);
                Assert.Contains(full, r => r["rval"]?.ToString() == "R4_only" && r["lval"] == null);
                Assert.Contains(full, r => r["rval"]?.ToString() == "R5_only" && r["lval"] == null);
                Assert.Contains(full, r => r["lval"]?.ToString() == "L1" && r["rval"]?.ToString() == "R1");
            }

            // LEFT SEMI and LEFT ANTI: NULL keys never match.
            {
                var (eval, logger) = BuildContext(partitions: 4);
                var engine = new ExternalJoinEngine(eval, logger);
                var semiJoin = new JoinClause(
                    "LEFT SEMI",
                    new TableReference("right"),
                    new BinaryExpression(new IdentifierExpression("id"), TokenType.EQUALS, new IdentifierExpression("id")));

                var semi = await engine.ApplyHashJoinExternal(
                    leftRows.ToAsyncEnumerable(),
                    rightRows.ToAsyncEnumerable(),
                    semiJoin,
                    new List<string> { "id" },
                    new List<string> { "id" }).ToListAsync();

                Assert.Single(semi);
                Assert.Equal("L1", semi[0]["lval"]);
            }

            {
                var (eval, logger) = BuildContext(partitions: 4);
                var engine = new ExternalJoinEngine(eval, logger);
                var antiJoin = new JoinClause(
                    "LEFT ANTI",
                    new TableReference("right"),
                    new BinaryExpression(new IdentifierExpression("id"), TokenType.EQUALS, new IdentifierExpression("id")));

                var anti = await engine.ApplyHashJoinExternal(
                    leftRows.ToAsyncEnumerable(),
                    rightRows.ToAsyncEnumerable(),
                    antiJoin,
                    new List<string> { "id" },
                    new List<string> { "id" }).ToListAsync();

                var antiVals = anti.Select(r => r["lval"]?.ToString()).OrderBy(x => x, StringComparer.Ordinal).ToList();
                Assert.Equal(new[] { "L2_only", "L3_only", "L_null" }, antiVals);
            }
        }

        [Fact]
        public async Task ApplyHashJoinExternal_CompositeKeys_HandleExtraKeysAndPartialNulls()
        {
            var (eval, logger) = BuildContext(partitions: 4);
            var engine = new ExternalJoinEngine(eval, logger);

            var leftRows = new List<Row>
            {
                new() { ["k1"] = 1m, ["k2"] = "1.0", ["lval"] = "L_match" },
                new() { ["k1"] = 1m, ["k2"] = null, ["lval"] = "L_null2" },
                new() { ["k1"] = 2m, ["k2"] = "alpha", ["lval"] = "L_case" },
            };

            var rightRows = new List<Row>
            {
                new() { ["k1"] = 1.0m, ["k2"] = "1", ["rval"] = "R_match" }, // one runtime type per column: spill rejects mixed columns
                new() { ["k1"] = 1m, ["k2"] = null, ["rval"] = "R_null2" },
                new() { ["k1"] = 2m, ["k2"] = "ALPHA", ["rval"] = "R_case" },
            };

            var cond = new BinaryExpression(
                new BinaryExpression(new IdentifierExpression("k1"), TokenType.EQUALS, new IdentifierExpression("k1")),
                TokenType.AND,
                new BinaryExpression(new IdentifierExpression("k2"), TokenType.EQUALS, new IdentifierExpression("k2")));
            var join = new JoinClause("INNER", new TableReference("right"), cond);

            var results = await engine.ApplyHashJoinExternal(
                leftRows.ToAsyncEnumerable(),
                rightRows.ToAsyncEnumerable(),
                join,
                new List<string> { "k1", "k2" },
                new List<string> { "k1", "k2" }).ToListAsync();

            var pairs = results.Select(r => $"{r["lval"]}:{r["rval"]}").OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.Equal(new[] { "L_case:R_case", "L_match:R_match" }, pairs);
        }

        [Fact]
        public async Task FullOuterJoin_WithCollidingIdColumnAndSpilledOrderBy_PreservesNullRightIdInEmptyRightPartition()
        {
            // Regression for W9 (SLT join.test:175 on the spill path): when an unmatched left row
            // (Charlie, u.id = 3) hashes into a partition whose right side is empty, it must still
            // use the combined layout so downstream Arrow spill sorting maps o.id to NULL rather
            // than reading left's bare `id` (3).
            await using var provider = (ServiceProvider)DependencyInjectionSetup.BuildServiceProvider(new Dictionary<string, string?>
            {
                ["Engine:BatchSize"] = "3",
                ["Engine:JoinSpillThreshold"] = "1",
                ["Engine:ExternalSortChunkSize"] = "2",
                ["Engine:TempTableSpillThresholdRows"] = "2",
                ["Engine:MaxInMemoryBatches"] = "1",
                ["Orchestration:ResourceManagement:SystemMemoryFloorMB"] = "0"
            });
            await using var eval = provider.GetRequiredService<Evaluator>();
            eval.ExternalHashPartitions = 8;

            const string sql = """
                CREATE TABLE #users (id INT, name STRING);
                INSERT INTO #users VALUES (1, 'Alice'), (2, 'Bob'), (3, 'Charlie');
                CREATE TABLE #orders (id INT, user_id INT, amount INT);
                INSERT INTO #orders VALUES (10, 1, 100), (11, 1, 50), (12, 2, 200), (13, 4, 300);

                SELECT u.name, o.id, o.amount
                FROM #users u
                FULL OUTER JOIN #orders o ON u.id = o.user_id
                ORDER BY u.name, o.id;
                """;

            var parser = new Parser(new Lexer(sql).Tokenize(), sql);
            await eval.Evaluate(parser.Parse());

            var rows = eval.LastResult?.Rows;
            Assert.NotNull(rows);
            Assert.Equal(5, rows!.Count);

            var charlie = Assert.Single(rows, r => Equals(r["name"], "Charlie"));
            Assert.Null(charlie["id"]);
            Assert.Null(charlie["amount"]);

            var unmatchedOrder = Assert.Single(rows, r => r["name"] == null);
            Assert.Equal(13m, unmatchedOrder["id"]);
            Assert.Equal(300m, unmatchedOrder["amount"]);
        }
    }
}
