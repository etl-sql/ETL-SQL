using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Engines;
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

            var results = await engine.ApplyHashJoinExternal(
                leftRows.ToAsyncEnumerable(),
                rightRows.ToAsyncEnumerable(),
                join,
                new List<string> { "id" },
                new List<string> { "id" }).ToListAsync();

            Assert.Equal(10, results.Count);
            Assert.All(results, r => Assert.Equal(r["lval"]?.ToString().Replace("l-", ""), r["rval"]?.ToString().Replace("r-", "")));

            Assert.True(eval.Telemetry.TotalSpilledBytes > startSpill, "Should have reported spilled bytes");
            Assert.True(eval.Telemetry.PartitionsCount > startParts, "Should have reported used partition count");
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

        // ── RAM governor ──────────────────────────────────────────────────────
        // JoinSpillThreshold is set huge so the row-count repartition never fires — the memory
        // guard is the only thing that can trip, isolating the governor path. The build (right)
        // side always buffers its rows, so heap growth is reliable (ceiling = 1 byte trips it).

        private static readonly JoinClause InnerOnId = new JoinClause(
            "INNER", new TableReference("right"),
            new BinaryExpression(new IdentifierExpression("id"), TokenType.EQUALS, new IdentifierExpression("id")));

        private static IAsyncEnumerable<Row> JoinRows(int count, int distinctKeys, string valPrefix) =>
            Build(count, distinctKeys, valPrefix).ToAsyncEnumerable();

        private static IEnumerable<Row> Build(int count, int distinctKeys, string valPrefix)
        {
            for (int i = 0; i < count; i++)
                yield return new Row { ["id"] = i % distinctKeys, [valPrefix] = $"{valPrefix}-{i}" };
        }

        [Fact]
        public async Task Governor_SpillOrFail_HighCardinalityBuild_CompletesViaRepartition()
        {
            var (eval, logger) = BuildContext(partitions: 2);
            eval.JoinSpillThreshold = 10_000_000;   // disable row-count repartition
            eval.MemoryGovernorPolicy = MemoryGovernorPolicy.SpillOrFail;
            long saved = MemoryGrantArbiter.Shared.TotalBudgetBytes;
            MemoryGrantArbiter.Shared.TotalBudgetBytes = 1;
            try
            {
                // left: 4000 distinct keys once each (probe). right: same 4000 keys ×5 (build, 20000 rows).
                var left = JoinRows(4000, 4000, "lval");
                var right = JoinRows(20000, 4000, "rval");
                var engine = new ExternalJoinEngine(eval, logger);

                var results = await engine.ApplyHashJoinExternal(
                    left, right, InnerOnId, new List<string> { "id" }, new List<string> { "id" }).ToListAsync();

                Assert.Equal(20000, results.Count); // each of 4000 left rows matches 5 right rows
            }
            finally { MemoryGrantArbiter.Shared.TotalBudgetBytes = saved; }
        }

        [Fact]
        public async Task Governor_SpillOrFail_UnsplittableBuild_Throws()
        {
            var (eval, logger) = BuildContext(partitions: 2);
            eval.JoinSpillThreshold = 10_000_000;
            eval.MemoryGovernorPolicy = MemoryGovernorPolicy.SpillOrFail;
            long saved = MemoryGrantArbiter.Shared.TotalBudgetBytes;
            MemoryGrantArbiter.Shared.TotalBudgetBytes = 1;
            try
            {
                // Single build-side key → all rows in one partition that can't be split → abort.
                var left = JoinRows(1, 1, "lval");
                var right = JoinRows(20000, 1, "rval");
                var engine = new ExternalJoinEngine(eval, logger);

                await Assert.ThrowsAsync<ExecutionException>(async () =>
                    await engine.ApplyHashJoinExternal(
                        left, right, InnerOnId, new List<string> { "id" }, new List<string> { "id" }).ToListAsync());
            }
            finally { MemoryGrantArbiter.Shared.TotalBudgetBytes = saved; }
        }

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
    }
}
