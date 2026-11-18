using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Engines;
using ETL_SQL.Orchestrator.Execution;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public class PipelineCursorAdmissionTests
{
    private static ServiceProvider CreateSingleSlotServiceProvider(int maxCursors = 1, int timeoutSec = 2)
    {
        var dbPath = Path.Combine(
            Path.GetTempPath(),
            "ETL-SQL.Tests",
            "sp14-cursor",
            $"sp14-{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var overrides = new Dictionary<string, string?>
        {
            ["Orchestration:ResourceManagement:MaxStreamingCursors"] = maxCursors.ToString(),
            ["Orchestration:ResourceManagement:ResourceWaitTimeoutSeconds"] = timeoutSec.ToString(),
            ["Orchestrator:DatabasePath"] = dbPath
        };

        return (ServiceProvider)DependencyInjectionSetup.BuildServiceProvider(overrides);
    }

    private static Evaluator CreateEvaluator(
        IServiceProvider sp,
        int sortChunkSize = 2,
        int windowSpillThreshold = 2)
    {
        var eval = sp.GetRequiredService<Evaluator>();
        eval.ExternalSortChunkSize = sortChunkSize;
        eval.WindowSpillThreshold = windowSpillThreshold;
        eval.OperatorMemoryGrantMB = 1;
        return eval;
    }

    private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();

    private static BufferManager GetBufferManager(IServiceProvider sp)
        => Assert.IsType<BufferManager>(sp.GetRequiredService<IBufferManager>());

    private static void AssertAllPermitsRestored(BufferManager bm, int expectedSlots = 1)
    {
        Assert.Equal(0, bm.ActiveCursors);
        Assert.Equal(0, bm.QueuedCursors);
        Assert.Equal(expectedSlots, bm.AvailableCursorSlots);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds >= timeoutMs)
                throw new TimeoutException($"Condition did not become true within {timeoutMs} ms.");
            await Task.Delay(10);
        }
    }

    private static async IAsyncEnumerable<Row> GateStreamAsync(
        List<Row> rows,
        TaskCompletionSource enteredGate,
        TaskCompletionSource releaseGate,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        enteredGate.TrySetResult();
        await releaseGate.Task.WaitAsync(cancellationToken);
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return row;
        }
    }

    private static async IAsyncEnumerable<Row> FailingStreamAsync(
        List<Row> prefix,
        Exception error,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var row in prefix)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return row;
            await Task.Yield();
        }
        throw error;
    }

    [Fact]
    public async Task NestedSorts_DirectAndSql_WithOneCursorSlot_SucceedAndRestorePermits()
    {
        await using var sp = CreateSingleSlotServiceProvider(maxCursors: 1, timeoutSec: 2);
        var bm = GetBufferManager(sp);
        await using var eval = CreateEvaluator(sp, sortChunkSize: 2, windowSpillThreshold: 2);

        // 1. Direct operator composition: outer ExternalSort -> inner ExternalSort on 1 slot.
        var rows = new List<Row>
        {
            new() { ["id"] = 3m, ["grp"] = 2m, ["k"] = 30m },
            new() { ["id"] = 1m, ["grp"] = 2m, ["k"] = 10m },
            new() { ["id"] = 2m, ["grp"] = 1m, ["k"] = 20m }
        };

        var innerSort = new ExternalSortEngine(eval, NullLogger.Instance);
        var outerSort = new ExternalSortEngine(eval, NullLogger.Instance);

        var innerOrder = new List<OrderByClause> { new(new IdentifierExpression("k"), true) };
        var outerOrder = new List<OrderByClause>
        {
            new(new IdentifierExpression("grp"), false),
            new(new IdentifierExpression("id"), false)
        };

        var directResult = new List<Row>();
        await foreach (var r in outerSort.SortStreamAsync(innerSort.SortStreamAsync(rows.ToAsyncEnumerable(), innerOrder), outerOrder))
            directResult.Add(r);

        Assert.Equal([2m, 1m, 3m], directResult.Select(r => Convert.ToDecimal(r["id"])).ToArray());
        AssertAllPermitsRestored(bm, expectedSlots: 1);

        // 2. End-to-end SQL nested sort (subquery ORDER BY -> outer ORDER BY) on 3 rows with 1 slot.
        eval.Connections["#src"] = new TestStreamingDataSource(rows);
        const string sql = """
            SELECT id, grp, k
            FROM (
                SELECT id, grp, k
                FROM #src
                ORDER BY k DESC
            ) s
            ORDER BY grp ASC, id ASC
            """;

        await eval.Evaluate(Parse(sql));
        var sqlRows = eval.LastResult?.Rows ?? [];
        Assert.Equal([2m, 1m, 3m], sqlRows.Select(r => Convert.ToDecimal(r["id"])).ToArray());
        AssertAllPermitsRestored(bm, expectedSlots: 1);
    }

    [Fact]
    public async Task WindowToSort_DirectAndSql_WithOneCursorSlot_SucceedsAndRestoresPermits()
    {
        await using var sp = CreateSingleSlotServiceProvider(maxCursors: 1, timeoutSec: 2);
        var bm = GetBufferManager(sp);
        await using var eval = CreateEvaluator(sp, sortChunkSize: 2, windowSpillThreshold: 2);

        var rows = Enumerable.Range(1, 6).Select(i => new Row
        {
            ["id"] = (decimal)i,
            ["grp"] = i % 2 == 0 ? "even" : "odd",
            ["score"] = (decimal)(100 - i)
        }).ToList();

        // 1. Direct: ExternalSort -> ExternalWindow -> internal _sortEngine on 1 cursor slot.
        const string windowSql = """
            SELECT
                id,
                grp,
                score,
                ROW_NUMBER() OVER (PARTITION BY grp ORDER BY score ASC) AS rn
            FROM #src
            ORDER BY id ASC
            """;
        var selectStmt = Assert.IsType<SelectStatement>(Parse(windowSql).Statements.Single());
        var aggEngine = new AggregateEngine(eval, NullLogger.Instance);
        var winEngine = new WindowEngine(eval, aggEngine, NullLogger.Instance);
        var extWin = new ExternalWindowEngine(eval, winEngine, NullLogger.Instance);
        var extSort = new ExternalSortEngine(eval, NullLogger.Instance);

        var windowed = extWin.ApplyWindowFunctionsExternal(rows.ToAsyncEnumerable(), selectStmt);
        var directResult = new List<Row>();
        await foreach (var r in extSort.SortStreamAsync(windowed, selectStmt.OrderBy!))
            directResult.Add(r);

        Assert.Equal(6, directResult.Count);
        Assert.Equal([1m, 2m, 3m, 4m, 5m, 6m], directResult.Select(r => Convert.ToDecimal(r["id"])).ToArray());
        // Within "odd" (ids 1,3,5 -> scores 99,97,95), ASC score assigns rn 1->id5, 2->id3, 3->id1.
        var rnKey = $"WINDOW_{selectStmt.Columns[3].Expression.ToSql().ToUpperInvariant()}";
        Assert.Equal(3m, Convert.ToDecimal(directResult[0][rnKey]));
        Assert.Equal(1m, Convert.ToDecimal(directResult[4][rnKey]));
        AssertAllPermitsRestored(bm, expectedSlots: 1);

        // 2. Full SQL SelectStatement execution with window -> sort spilling on 1 slot.
        eval.Connections["#src"] = new TestStreamingDataSource(rows);
        await eval.Evaluate(Parse(windowSql));
        var sqlRows = eval.LastResult?.Rows ?? [];
        Assert.Equal(6, sqlRows.Count);
        Assert.Equal([1m, 2m, 3m, 4m, 5m, 6m], sqlRows.Select(r => Convert.ToDecimal(r["id"])).ToArray());
        Assert.Equal(3m, Convert.ToDecimal(sqlRows[0]["rn"]));
        Assert.Equal(1m, Convert.ToDecimal(sqlRows[4]["rn"]));
        AssertAllPermitsRestored(bm, expectedSlots: 1);
    }

    [Fact]
    public async Task CompetingPipelines_AtCapacity_QueueAndDrainCleanly()
    {
        await using var sp = CreateSingleSlotServiceProvider(maxCursors: 1, timeoutSec: 5);
        var bm = GetBufferManager(sp);
        await using var evalA = CreateEvaluator(sp, sortChunkSize: 2, windowSpillThreshold: 2);
        await using var evalB = CreateEvaluator(sp, sortChunkSize: 2, windowSpillThreshold: 2);

        var enteredA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var rowsA = new List<Row>
        {
            new() { ["id"] = 2m },
            new() { ["id"] = 1m },
            new() { ["id"] = 3m }
        };
        var rowsB = new List<Row>
        {
            new() { ["id"] = 20m },
            new() { ["id"] = 10m },
            new() { ["id"] = 30m }
        };

        var orderBy = new List<OrderByClause> { new(new IdentifierExpression("id"), false) };
        var sortA = new ExternalSortEngine(evalA, NullLogger.Instance);
        var sortB = new ExternalSortEngine(evalB, NullLogger.Instance);

        var taskA = Task.Run(async () =>
        {
            var outA = new List<Row>();
            await foreach (var r in sortA.SortStreamAsync(GateStreamAsync(rowsA, enteredA, releaseA), orderBy))
                outA.Add(r);
            return outA;
        });

        await enteredA.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, bm.ActiveCursors);
        Assert.Equal(0, bm.AvailableCursorSlots);

        var taskB = Task.Run(async () =>
        {
            var outB = new List<Row>();
            await foreach (var r in sortB.SortStreamAsync(rowsB.ToAsyncEnumerable(), orderBy))
                outB.Add(r);
            return outB;
        });

        await WaitUntilAsync(() => bm.QueuedCursors == 1);
        Assert.False(taskB.IsCompleted);

        // Releasing A lets B acquire the freed slot and complete cleanly.
        releaseA.SetResult();
        var results = await Task.WhenAll(taskA, taskB);

        Assert.Equal([1m, 2m, 3m], results[0].Select(r => Convert.ToDecimal(r["id"])).ToArray());
        Assert.Equal([10m, 20m, 30m], results[1].Select(r => Convert.ToDecimal(r["id"])).ToArray());
        AssertAllPermitsRestored(bm, expectedSlots: 1);
    }

    [Fact]
    public async Task QueuedOperator_CancelledWhileWaiting_UnblocksPromptlyAndPreservesPermits()
    {
        await using var sp = CreateSingleSlotServiceProvider(maxCursors: 1, timeoutSec: 10);
        var bm = GetBufferManager(sp);
        await using var evalA = CreateEvaluator(sp, sortChunkSize: 2, windowSpillThreshold: 2);
        await using var evalB = CreateEvaluator(sp, sortChunkSize: 2, windowSpillThreshold: 2);
        await using var evalC = CreateEvaluator(sp, sortChunkSize: 2, windowSpillThreshold: 2);

        var enteredA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var orderBy = new List<OrderByClause> { new(new IdentifierExpression("id"), false) };
        var sortA = new ExternalSortEngine(evalA, NullLogger.Instance);
        var sortB = new ExternalSortEngine(evalB, NullLogger.Instance);
        var sortC = new ExternalSortEngine(evalC, NullLogger.Instance);

        var taskA = Task.Run(async () =>
        {
            var outA = new List<Row>();
            await foreach (var r in sortA.SortStreamAsync(
                GateStreamAsync([new() { ["id"] = 2m }, new() { ["id"] = 1m }], enteredA, releaseA),
                orderBy))
                outA.Add(r);
            return outA;
        });

        await enteredA.Task.WaitAsync(TimeSpan.FromSeconds(3));

        using var ctsB = new CancellationTokenSource();
        var taskB = Task.Run(async () =>
        {
            await foreach (var _ in sortB.SortStreamAsync(
                new List<Row> { new() { ["id"] = 99m } }.ToAsyncEnumerable(),
                orderBy,
                cancellationToken: ctsB.Token))
            {
            }
        });

        await WaitUntilAsync(() => bm.QueuedCursors == 1);

        var sw = Stopwatch.StartNew();
        ctsB.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await taskB);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), $"Cancellation took too long ({sw.Elapsed})."); // flaky-time-bound-ok: hang guard; a cancellation that is ignored blocks indefinitely, not for seconds
        await WaitUntilAsync(() => bm.QueuedCursors == 0);
        Assert.Equal(1, bm.ActiveCursors);

        // Also verify direct IBufferManager.AcquireCursorAsync cancellation via interface contract.
        IBufferManager ibm = bm;
        using var ctsDirect = new CancellationTokenSource();
        var directWaitTask = ibm.AcquireCursorAsync("direct-waiter", ctsDirect.Token);
        await WaitUntilAsync(() => bm.QueuedCursors == 1);
        ctsDirect.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await directWaitTask);
        await WaitUntilAsync(() => bm.QueuedCursors == 0);

        // Release A and confirm all capacity is usable by C.
        releaseA.SetResult();
        await taskA;
        AssertAllPermitsRestored(bm, expectedSlots: 1);

        var resultC = new List<Row>();
        await foreach (var r in sortC.SortStreamAsync(
            new List<Row> { new() { ["id"] = 5m }, new() { ["id"] = 4m }, new() { ["id"] = 6m } }.ToAsyncEnumerable(),
            orderBy))
            resultC.Add(r);

        Assert.Equal([4m, 5m, 6m], resultC.Select(r => Convert.ToDecimal(r["id"])).ToArray());
        AssertAllPermitsRestored(bm, expectedSlots: 1);
    }

    [Fact]
    public async Task NestedPipelineFailure_RestoresAllPermitsAndCleansUpSpills()
    {
        await using var sp = CreateSingleSlotServiceProvider(maxCursors: 1, timeoutSec: 2);
        var bm = GetBufferManager(sp);
        await using var eval = CreateEvaluator(sp, sortChunkSize: 2, windowSpillThreshold: 2);

        var windowSql = """
            SELECT
                id,
                ROW_NUMBER() OVER (ORDER BY id ASC) AS rn
            FROM #src
            ORDER BY id DESC
            """;
        var selectStmt = Assert.IsType<SelectStatement>(Parse(windowSql).Statements.Single());
        var aggEngine = new AggregateEngine(eval, NullLogger.Instance);
        var winEngine = new WindowEngine(eval, aggEngine, NullLogger.Instance);
        var extWin = new ExternalWindowEngine(eval, winEngine, NullLogger.Instance);
        var extSort = new ExternalSortEngine(eval, NullLogger.Instance);

        var failingInput = FailingStreamAsync(
            [
                new Row { ["id"] = 1m },
                new Row { ["id"] = 2m },
                new Row { ["id"] = 3m }
            ],
            new InvalidOperationException("upstream pipeline failure"));

        var windowed = extWin.ApplyWindowFunctionsExternal(failingInput, selectStmt);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in extSort.SortStreamAsync(windowed, selectStmt.OrderBy!))
            {
            }
        });
        Assert.Equal("upstream pipeline failure", ex.Message);

        AssertAllPermitsRestored(bm, expectedSlots: 1);
        if (Directory.Exists(eval.SpillStore.RootPath))
            Assert.Empty(Directory.GetFiles(eval.SpillStore.RootPath, "*", SearchOption.AllDirectories));

        // Verify the single slot is immediately reusable for another nested pipeline.
        var okRows = new List<Row>
        {
            new() { ["id"] = 2m },
            new() { ["id"] = 1m },
            new() { ["id"] = 3m }
        };
        var recovered = new List<Row>();
        await foreach (var r in extSort.SortStreamAsync(
            extWin.ApplyWindowFunctionsExternal(okRows.ToAsyncEnumerable(), selectStmt),
            selectStmt.OrderBy!))
            recovered.Add(r);

        Assert.Equal([3m, 2m, 1m], recovered.Select(r => Convert.ToDecimal(r["id"])).ToArray());
        AssertAllPermitsRestored(bm, expectedSlots: 1);
    }

    [Fact]
    public async Task NestedJoinAndAggregateToSort_WithOneCursorSlot_SucceedsAndRestoresPermits()
    {
        await using var sp = CreateSingleSlotServiceProvider(maxCursors: 1, timeoutSec: 2);
        var bm = GetBufferManager(sp);
        await using var eval = CreateEvaluator(sp, sortChunkSize: 2, windowSpillThreshold: 2);
        eval.JoinSpillThreshold = 2;

        var leftRows = new List<Row>
        {
            new() { ["id"] = 1m, ["grp"] = "B", ["amt"] = 10m },
            new() { ["id"] = 2m, ["grp"] = "A", ["amt"] = 20m },
            new() { ["id"] = 3m, ["grp"] = "A", ["amt"] = 30m }
        };
        var rightRows = new List<Row>
        {
            new() { ["rid"] = 1m, ["tag"] = "r1" },
            new() { ["rid"] = 2m, ["tag"] = "r2" },
            new() { ["rid"] = 3m, ["tag"] = "r3" }
        };

        eval.Connections["#left_src"] = new TestStreamingDataSource(leftRows);
        eval.Connections["#right_src"] = new TestStreamingDataSource(rightRows);

        const string sql = """
            SELECT l.grp, SUM(l.amt) AS total_amt
            FROM #left_src l
            INNER JOIN #right_src r ON l.id = r.rid
            GROUP BY l.grp
            ORDER BY total_amt DESC
            """;

        await eval.Evaluate(Parse(sql));
        var resultRows = eval.LastResult?.Rows ?? [];
        Assert.Equal(2, resultRows.Count);
        Assert.Equal("A", resultRows[0]["grp"]?.ToString());
        Assert.Equal(50m, Convert.ToDecimal(resultRows[0]["total_amt"]));
        Assert.Equal("B", resultRows[1]["grp"]?.ToString());
        Assert.Equal(10m, Convert.ToDecimal(resultRows[1]["total_amt"]));
        AssertAllPermitsRestored(bm, expectedSlots: 1);
    }

    private sealed class TestStreamingDataSource(IEnumerable<Row> rows) : IDataSource
    {
        private readonly List<Row> _rows = rows.ToList();

        public string ConnectorType => "TEST";
        public string Path => "#stream";
        public Dictionary<string, string>? Options => null;

        public Task<IEnumerable<string>> GetColumnsAsync() =>
            Task.FromResult<IEnumerable<string>>(_rows.Count == 0
                ? Array.Empty<string>()
                : _rows[0].GetColumnNames().ToList());

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
