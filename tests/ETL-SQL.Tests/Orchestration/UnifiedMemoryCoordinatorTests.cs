using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Engines;
using ETL_SQL.Engine.Services;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public class UnifiedMemoryCoordinatorTests
{
    private static ServiceProvider CreateCoordinatorProvider(Dictionary<string, string?>? extraOverrides = null)
    {
        var dbPath = Path.Combine(
            Path.GetTempPath(),
            "ETL-SQL.Tests",
            "sp16-coord",
            $"sp16-{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var overrides = new Dictionary<string, string?>
        {
            ["Orchestrator:DatabasePath"] = dbPath,
            ["Orchestration:ResourceManagement:MaxStreamingCursors"] = "8",
            ["Orchestration:ResourceManagement:ResourceWaitTimeoutSeconds"] = "5"
        };
        if (extraOverrides != null)
        {
            foreach (var kvp in extraOverrides)
                overrides[kvp.Key] = kvp.Value;
        }

        return (ServiceProvider)DependencyInjectionSetup.BuildServiceProvider(overrides);
    }

    private static BufferManager CreateControlledBufferManager(
        ISystemResources sysResources,
        int maxGlobalMB = 10,
        int hysteresisMB = 2,
        int systemFloorMB = 512,
        int timeoutSec = 2,
        IConfiguration? config = null)
    {
        var options = Options.Create(new BufferManagerOptions
        {
            MaxGlobalMemoryMB = maxGlobalMB,
            HysteresisMemoryMB = hysteresisMB,
            SystemMemoryFloorMB = systemFloorMB,
            MaxStreamingCursors = 8,
            ResourceWaitTimeoutSeconds = timeoutSec
        });
        return new BufferManager(options, NullLogger<BufferManager>.Instance, sysResources, config);
    }

    private static Evaluator CreateStandaloneEvaluator(BufferManager bm, IConfiguration? config = null)
    {
        var cfg = config ?? new ConfigurationBuilder().Build();
        var sec = new SecurityService(ETL_SQL.Common.NullLogger.Instance);
        var services = new ServiceCollection();
        services.AddSingleton<IBufferManager>(bm);
        services.AddSingleton<IMemoryGrantArbiter>(bm);
        services.AddSingleton<IConfiguration>(cfg);
        var sp = services.BuildServiceProvider();

        return new Evaluator(
            Array.Empty<IStatementHandler>(),
            sp,
            new ETL_SQL.Engine.Functions.FunctionRegistry(),
            new Mock<ILineageTracker>().Object,
            new Mock<IDockerManager>().Object,
            new Mock<IConnectorRegistry>().Object,
            new SessionStateManager(ETL_SQL.Common.NullLogger.Instance, sec, cfg, new SqliteSessionMetadataStoreFactory()),
            sec,
            ETL_SQL.Common.NullLogger.Instance,
            new ETL_SQL.Core.Metadata.LanguageHelpRegistry(),
            new EvaluatorComponentRegistry());
    }

    private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();

    [Fact]
    public void DefaultSystemResources_ReportsLiveHeadroom_AndParsesLinuxContainerLimits()
    {
        var resources = new DefaultSystemResources();
        long available = resources.GetAvailableMemoryBytes();
        long total = resources.GetTotalMemoryBytes();

        Assert.True(available > 0, $"Expected positive live memory headroom, got {available}.");
        if (total > 0)
            Assert.True(available <= total, $"Headroom {available} should not exceed total capacity {total}.");

        // Linux /proc/meminfo MemAvailable + MemFree/Buffers/Cached fallback.
        const string memInfoWithAvailable = """
            MemTotal:       16384000 kB
            MemFree:         1024000 kB
            MemAvailable:    6144000 kB
            Buffers:          256000 kB
            Cached:          2048000 kB
            """;
        Assert.Equal(6144000L * 1024L, DefaultSystemResources.ParseProcMemInfoAvailableBytes(memInfoWithAvailable));

        const string memInfoFallback = """
            MemFree:          512000 kB
            Buffers:          128000 kB
            Cached:           360000 kB
            """;
        Assert.Equal(1000000L * 1024L, DefaultSystemResources.ParseProcMemInfoAvailableBytes(memInfoFallback));

        // cgroup v2 with reclaimable inactive_file page cache & "max" unlimited handling.
        long v2Max = 1024L * 1024 * 1024;
        long v2Cur = 400L * 1024 * 1024;
        long v2Inactive = 100L * 1024 * 1024;
        long? v2Headroom = DefaultSystemResources.ComputeCgroupV2Headroom(
            v2Max.ToString(),
            v2Cur.ToString(),
            $"anon 300000000\ninactive_file {v2Inactive}\n");
        Assert.Equal(v2Max - (v2Cur - v2Inactive), v2Headroom);
        Assert.Null(DefaultSystemResources.ComputeCgroupV2Headroom("max\n", v2Cur.ToString(), null));

        // cgroup v1 bounded vs unlimited sentinel.
        long v1Limit = 512L * 1024 * 1024;
        long v1Usage = 200L * 1024 * 1024;
        long v1Inactive = 50L * 1024 * 1024;
        Assert.Equal(
            v1Limit - (v1Usage - v1Inactive),
            DefaultSystemResources.ComputeCgroupV1Headroom(v1Limit.ToString(), v1Usage.ToString(), $"total_inactive_file {v1Inactive}"));
        Assert.Null(DefaultSystemResources.ComputeCgroupV1Headroom((1L << 60).ToString(), v1Usage.ToString(), null));
    }

    [Fact]
    public void SettingsPrecedence_ExplicitEngineGrantWins_AutoCapsByMaxGlobalMemory()
    {
        var total20Gb = 20L * 1024 * 1024 * 1024;

        // 1. Explicit Engine:TotalMemoryGrantMB > 0 wins over MaxGlobalMemoryMB (both lower and higher).
        Assert.Equal(12, MemoryCoordinatorPolicy.ResolveEffectiveGlobalMemoryMB(12, 2048, total20Gb));
        Assert.Equal(4096, MemoryCoordinatorPolicy.ResolveEffectiveGlobalMemoryMB(4096, 2048, total20Gb));

        // 2. Explicit Engine:TotalMemoryGrantMB = 0 disables the global cap.
        Assert.Equal(0, MemoryCoordinatorPolicy.ResolveEffectiveGlobalMemoryMB(0, 2048, total20Gb));

        // 3. Auto (-1 / null) uses min(auto, MaxGlobalMemoryMB) when MaxGlobalMemoryMB > 0.
        Assert.Equal(64, MemoryCoordinatorPolicy.ResolveEffectiveGlobalMemoryMB(-1, 64, total20Gb));
        Assert.Equal(64, MemoryCoordinatorPolicy.ResolveEffectiveGlobalMemoryMB(null, 64, total20Gb));

        // 4. Inside a small container (e.g. 1000 MB total -> auto = 800 MB), auto respects container over default 2048.
        var container1Gb = 1000L * 1024 * 1024;
        Assert.Equal(800, MemoryCoordinatorPolicy.ResolveEffectiveGlobalMemoryMB(-1, 2048, container1Gb));

        // 5. DefaultThresholds helper agrees cleanly with IConfiguration.
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Engine:TotalMemoryGrantMB"] = "32",
                ["Orchestration:ResourceManagement:MaxGlobalMemoryMB"] = "2048"
            })
            .Build();
        Assert.Equal(32, DefaultThresholds.ResolveGlobalMemoryGrantMB(cfg));
    }

    [Fact]
    public async Task DiHost_ConnectsBufferManagerAndArbiterIntoSingleCoordinator()
    {
        await using var sp = CreateCoordinatorProvider(new Dictionary<string, string?>
        {
            ["Orchestration:ResourceManagement:MaxGlobalMemoryMB"] = "8",
            ["Engine:TotalMemoryGrantMB"] = "-1"
        });

        var bm = Assert.IsType<BufferManager>(sp.GetRequiredService<IBufferManager>());
        var arbiter = sp.GetRequiredService<IMemoryGrantArbiter>();
        Assert.Same(bm, arbiter);

        await using var eval = sp.GetRequiredService<Evaluator>();
        Assert.Equal(8L * 1024 * 1024, bm.TotalBudgetBytes);
        Assert.Equal(bm.TotalBudgetBytes, eval.MemoryArbiter.TotalBudgetBytes);

        // An operator lease from Evaluator and an async reservation from BufferManager share the exact same byte pool.
        using (var asyncHold = await bm.ReserveMemoryAsync("other_job", 6L * 1024 * 1024))
        using (var opLease = eval.MemoryArbiter.AcquireLease())
        {
            Assert.False(opLease.RegisterAndCheckSpill(1L * 1024 * 1024));
            Assert.Equal(7L * 1024 * 1024, bm.ReservedBytes);
            Assert.Equal(bm.ReservedBytes, eval.MemoryArbiter.ReservedBytes);

            // Exceeding 8 MB combined budget rejects without double-counting or blocking.
            Assert.True(opLease.RegisterAndCheckSpill(3L * 1024 * 1024));
            Assert.Equal(7L * 1024 * 1024, bm.ReservedBytes);
        }

        Assert.Equal(0, bm.ReservedBytes);
    }

    [Fact]
    public async Task HysteresisAndSystemFloorRecovery_ResumeBlockedAdmissions()
    {
        long availableHeadroom = 2L * 1024 * 1024 * 1024;
        var mockSys = new Mock<ISystemResources>();
        mockSys.Setup(r => r.GetAvailableMemoryBytes()).Returns(() => Volatile.Read(ref availableHeadroom));

        using var bm = CreateControlledBufferManager(
            mockSys.Object,
            maxGlobalMB: 10,
            hysteresisMB: 4, // safe level = 6 MB
            systemFloorMB: 512,
            timeoutSec: 3);

        // Part A: Mixed async reservation + operator lease hits max (10 MB), pauses until hysteresis safe level (6 MB).
        var res1 = await bm.ReserveMemoryAsync("holder_async", 6L * 1024 * 1024);
        var opLease = bm.AcquireMemoryLease("holder_op");
        Assert.False(opLease.RegisterAndCheckSpill(3L * 1024 * 1024)); // 9 MB total reserved

        // Next 2 MB waiter pushes 9 + 2 = 11 > 10 MB -> queues & enters exhaustion.
        var waiterTask = bm.ReserveMemoryAsync("waiter", 2L * 1024 * 1024);
        Assert.True(bm.IsMemoryExhausted);

        // Dropping 3 MB (9 -> 6 MB) still exceeds safeLevel for 2 MB waiter (6 + 2 = 8 > 6 MB safeLevel).
        opLease.Dispose();
        await Task.Delay(120);
        Assert.False(waiterTask.IsCompleted);
        Assert.True(bm.IsMemoryExhausted);

        // Releasing res1 drops to 0 -> clears hysteresis & resumes waiterTask.
        res1.Dispose();
        var grantedWaiter = await waiterTask;
        Assert.False(bm.IsMemoryExhausted);
        grantedWaiter.Dispose();
        Assert.Equal(0, bm.ReservedBytes);

        // Part B: System memory drops below 512 MB floor -> blocks reservation & forces operator lease spill.
        bm.SystemCheckIntervalOverride = TimeSpan.Zero;
        Volatile.Write(ref availableHeadroom, 128L * 1024 * 1024);
        using var pressureLease = bm.AcquireMemoryLease("op_floor");
        Assert.True(pressureLease.RegisterAndCheckSpill(512L * 1024)); // Immediately tells operator to spill without waiting.
        Assert.True(bm.IsSystemMemoryExhausted);

        var blockedByFloor = bm.ReserveMemoryAsync("floor_waiter", 1024L * 1024);
        await Task.Delay(150);
        Assert.False(blockedByFloor.IsCompleted);

        // Raise system headroom back above floor -> recovery resumes admission automatically without any release.
        Volatile.Write(ref availableHeadroom, 2L * 1024 * 1024 * 1024);
        using var recovered = await blockedByFloor;
        Assert.NotNull(recovered);
        Assert.False(bm.IsSystemMemoryExhausted);
    }

    [Fact]
    public async Task ExternalOperators_RespectCoordinatorBudget_WithoutDoubleAccounting_AndReleaseOnCancelAndError()
    {
        var mockSys = new Mock<ISystemResources>();
        mockSys.Setup(r => r.GetAvailableMemoryBytes()).Returns(8L * 1024 * 1024 * 1024);
        mockSys.Setup(r => r.GetTotalMemoryBytes()).Returns(16L * 1024 * 1024 * 1024);

        // Configure 2 MB global budget via Engine:TotalMemoryGrantMB overriding MaxGlobalMemoryMB=2048.
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Engine:TotalMemoryGrantMB"] = "2",
                ["Engine:OperatorMemoryGrantMB"] = "256",
                ["Orchestration:ResourceManagement:MaxGlobalMemoryMB"] = "2048"
            })
            .Build();

        using var bm = CreateControlledBufferManager(mockSys.Object, maxGlobalMB: 2048, hysteresisMB: 0, systemFloorMB: 256, timeoutSec: 2, config: cfg);
        Assert.Equal(2L * 1024 * 1024, bm.TotalBudgetBytes);

        // Consume 1.9 MB in an unrelated concurrent reservation -> leaves ~100 KB for operators.
        using (var bg = await bm.ReserveMemoryAsync("concurrent_bg", (long)(1.9 * 1024 * 1024)))
        {
            await using var eval = CreateStandaloneEvaluator(bm, cfg);
            eval.ExternalSortChunkSize = 10000;
            eval.JoinSpillThreshold = 2;

            var widePayload = new string('x', 8000);
            var rows = Enumerable.Range(1, 40)
                .Select(i => new Row { ["id"] = (decimal)(41 - i), ["grp"] = (decimal)(i % 5), ["pad"] = widePayload })
                .ToList();

            // 1. ExternalSort spills chunks frequently under the 100 KB remaining coordinator headroom.
            var extSort = new ExternalSortEngine(eval, ETL_SQL.Common.NullLogger.Instance);
            var sorted = new List<Row>();
            await foreach (var r in extSort.SortStreamAsync(rows.ToAsyncEnumerable(), [new OrderByClause(new IdentifierExpression("id"), true)]))
                sorted.Add(r);

            Assert.Equal(40, sorted.Count);
            Assert.True(eval.Telemetry.SortSpillCount > 1, $"Expected multiple sort spills under tight coordinator pressure, got {eval.Telemetry.SortSpillCount}.");
            Assert.Equal((long)(1.9 * 1024 * 1024), bm.ReservedBytes);

            // 2. ExternalDistinct respects shared coordinator pressure via repartitioning and releases its lease.
            var extDistinct = new ExternalDistinctEngine(eval);
            var dupRows = rows.Concat(rows).ToList();
            var distinct = new List<Row>();
            await foreach (var r in extDistinct.ApplyAsync(dupRows.ToAsyncEnumerable()))
                distinct.Add(r);
            Assert.Equal(40, distinct.Count);
            Assert.Equal((long)(1.9 * 1024 * 1024), bm.ReservedBytes);
        }

        Assert.Equal(0, bm.ReservedBytes);

        // 3. Cancellation & failure inside ExternalJoin release all coordinator reservations cleanly.
        await using (var cancelEval = CreateStandaloneEvaluator(bm, cfg))
        {
            cancelEval.JoinSpillThreshold = 2;
            var left = Enumerable.Range(1, 20).Select(i => new Row { ["id"] = (decimal)i }).ToList();
            var right = Enumerable.Range(1, 20).Select(i => new Row { ["rid"] = (decimal)i, ["val"] = new string('y', 1000) }).ToList();

            using var cts = new CancellationTokenSource();
            var extJoin = new ExternalJoinEngine(cancelEval, ETL_SQL.Common.NullLogger.Instance);
            var joinClause = new JoinClause("INNER", new TableReference("#r"), new LiteralExpression(true, TokenType.NUMBER));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                int seen = 0;
                await foreach (var _ in extJoin.ApplyHashJoinExternal(
                    left.ToAsyncEnumerable(),
                    right.ToAsyncEnumerable(),
                    joinClause,
                    ["id"],
                    ["rid"],
                    cancellationToken: cts.Token))
                {
                    if (++seen == 1) cts.Cancel();
                }
            });
        }

        Assert.Equal(0, bm.ReservedBytes);

        // 4. Tight floor failure under SpillOrFail releases all coordinator reservations and leaves full capacity usable.
        using (var tightReserve = await bm.ReserveMemoryAsync("saturate", 2L * 1024 * 1024))
        {
            await using var failEval = CreateStandaloneEvaluator(bm, cfg);
            failEval.JoinSpillThreshold = 2;
            var rightSingleKey = Enumerable.Range(1, 10).Select(i => new Row { ["rid"] = 1m, ["pad"] = new string('z', 2000) }).ToList();
            var leftSingleKey = Enumerable.Range(1, 5).Select(i => new Row { ["id"] = 1m }).ToList();
            var extJoin = new ExternalJoinEngine(failEval, ETL_SQL.Common.NullLogger.Instance);
            var joinClause = new JoinClause("INNER", new TableReference("#r"), new LiteralExpression(true, TokenType.NUMBER));

            await Assert.ThrowsAsync<ExecutionException>(async () =>
            {
                await foreach (var _ in extJoin.ApplyHashJoinExternal(
                    leftSingleKey.ToAsyncEnumerable(),
                    rightSingleKey.ToAsyncEnumerable(),
                    joinClause,
                    ["id"],
                    ["rid"]))
                {
                }
            });
            Assert.Equal(2L * 1024 * 1024, bm.ReservedBytes);
        }

        Assert.Equal(0, bm.ReservedBytes);
        Assert.False(bm.IsMemoryExhausted);

        // Full 2 MB reservation succeeds after error + cancellation recovery.
        using var fullRecovery = await bm.ReserveMemoryAsync("after_recovery", 2L * 1024 * 1024);
        Assert.NotNull(fullRecovery);
    }

    [Fact]
    public async Task ConcurrentJobs_EndToEndSqlThroughDiCoordinator_CompleteAndReleaseAllReservations()
    {
        await using var sp = CreateCoordinatorProvider(new Dictionary<string, string?>
        {
            ["Orchestration:ResourceManagement:MaxGlobalMemoryMB"] = "4",
            ["Orchestration:ResourceManagement:HysteresisMemoryMB"] = "1",
            ["Engine:OperatorMemoryGrantMB"] = "2",
            ["Engine:TotalMemoryGrantMB"] = "-1"
        });

        var bm = Assert.IsType<BufferManager>(sp.GetRequiredService<IBufferManager>());
        Assert.Equal(4L * 1024 * 1024, bm.TotalBudgetBytes);

        async Task RunJobAsync(int jobId, bool abortMidRun)
        {
            await using var eval = sp.GetRequiredService<Evaluator>();
            eval.ExternalSortChunkSize = 5;
            eval.JoinSpillThreshold = 5;
            var rows = Enumerable.Range(1, 60)
                .Select(i => new Row { ["grp"] = (decimal)(i % 6), ["v"] = (decimal)(i * (jobId + 1)), ["txt"] = new string((char)('a' + (i % 26)), 512) })
                .ToList();

            eval.Connections["#src"] = new InMemoryRowsSource(rows);

            const string sql = """
                SELECT DISTINCT grp, SUM(v) AS total_v
                FROM #src
                GROUP BY grp
                ORDER BY total_v DESC;
                """;

            if (abortMidRun)
            {
                // Intentionally hold an un-disposed session reservation and verify DisposeAsync cleans up.
                _ = await bm.ReserveMemoryAsync(eval.SessionId!, 512 * 1024);
                await eval.Evaluate(Parse(sql));
            }
            else
            {
                await eval.Evaluate(Parse(sql));
                Assert.Equal(6, eval.LastResult?.Rows.Count);
            }
        }

        await Task.WhenAll(
            RunJobAsync(1, abortMidRun: false),
            RunJobAsync(2, abortMidRun: true),
            RunJobAsync(3, abortMidRun: false));

        Assert.Equal(0, bm.ReservedBytes);
        Assert.False(bm.IsMemoryExhausted);

        using var postCheck = await bm.ReserveMemoryAsync("post_concurrent", 4L * 1024 * 1024);
        Assert.NotNull(postCheck);
    }

    private sealed class InMemoryRowsSource(IEnumerable<Row> rows) : IDataSource
    {
        private readonly List<Row> _rows = rows.ToList();
        public string ConnectorType => "TEST";
        public string Path => "#src";
        public Dictionary<string, string>? Options => null;

        public Task<IEnumerable<string>> GetColumnsAsync() =>
            Task.FromResult<IEnumerable<string>>(_rows.Count == 0 ? [] : _rows[0].GetColumnNames().ToList());

        public IAsyncEnumerable<DataTable> ReadBatches(int requestedBatchSize = 10000) =>
            ReadBatches(requestedBatchSize, CancellationToken.None);

        public async IAsyncEnumerable<DataTable> ReadBatches(
            int requestedBatchSize,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (_rows.Count == 0) yield break;
            var dt = new DataTable();
            dt.SetColumns(_rows[0].GetColumnNames());
            foreach (var r in _rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await dt.AddRowAsync(r.Clone());
            }
            yield return dt;
        }

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => throw new NotSupportedException();
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName) => this;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
