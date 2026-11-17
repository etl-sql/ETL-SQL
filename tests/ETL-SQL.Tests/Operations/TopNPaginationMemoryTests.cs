using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Operations;

[Trait("CompatBreak", "0.23")]
public class TopNPaginationMemoryTests
{
    private static async Task<Evaluator> CreateEvaluatorAsync(MemoryGrantArbiter? arbiter = null, int operatorGrantMb = 1)
    {
        var sp = DependencyInjectionSetup.BuildServiceProvider();
        var evaluator = sp.GetRequiredService<Evaluator>();
        evaluator.OperatorMemoryGrantMB = operatorGrantMb;
        if (arbiter is not null)
            evaluator.MemoryArbiter = arbiter;
        return await Task.FromResult(evaluator);
    }

    private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();

    [Fact]
    public async Task SpillTransition_EvaluatesEachSortKeyOnlyOnce()
    {
        var arbiter = new MemoryGrantArbiter(16 * 1024);
        await using var evaluator = await CreateEvaluatorAsync(arbiter);
        var calls = new Dictionary<decimal, int>();
        evaluator.FunctionRegistry.Register("REVIEW_KEY", (args, _) =>
        {
            var id = Convert.ToDecimal(args[0]);
            calls[id] = calls.GetValueOrDefault(id) + 1;
            // A deterministic stand-in for a volatile expression: replaying a row changes its key.
            return (object?)(calls[id] == 1 ? id : -id);
        });
        evaluator.Connections["#src"] = new StreamingRowsDataSource(
            Enumerable.Range(0, 100).Reverse().Select(i => new Row
            {
                ["id"] = (decimal)i,
                ["payload"] = new string('x', 512)
            }));

        await evaluator.Evaluate(Parse("""
            SELECT id, payload FROM #src
            ORDER BY REVIEW_KEY(id) OFFSET 50 ROWS FETCH NEXT 10 ROWS ONLY;
            """));

        Assert.True(evaluator.Telemetry.SortSpillCount > 0);
        Assert.Equal(Enumerable.Range(50, 10).Select(i => (decimal)i),
            evaluator.LastResult!.Rows.Select(row => Convert.ToDecimal(row["id"])));
        Assert.Equal(100, calls.Count);
        Assert.All(calls.Values, count => Assert.Equal(1, count));
        Assert.Equal(0, arbiter.ReservedBytes);
    }

    [Fact]
    public async Task LargeOffsetUnderTinyGrant_TransitionsToExternalSortAndReturnsExactSlice()
    {
        var arbiter = new MemoryGrantArbiter(32 * 1024);
        await using var evaluator = await CreateEvaluatorAsync(arbiter);

        // 1,000 rows in descending id order; ORDER BY id ASC with OFFSET 900 FETCH NEXT 25.
        evaluator.Connections["#src"] = new StreamingRowsDataSource(
            Enumerable.Range(0, 1000).Reverse().Select(i => new Row { ["id"] = (decimal)i, ["pad"] = new string('x', 80) }));

        await evaluator.Evaluate(Parse("""
            SELECT id, pad
            FROM #src
            ORDER BY id ASC
            OFFSET 900 ROWS FETCH NEXT 25 ROWS ONLY;
            """));

        var rows = evaluator.LastResult?.Rows ?? [];
        Assert.Equal(25, rows.Count);
        Assert.Equal(Enumerable.Range(900, 25).Select(i => (decimal)i), rows.Select(r => Convert.ToDecimal(r["id"])));
        Assert.True(evaluator.Telemetry.SortSpillCount > 0, "Large offset under tiny grant must spill to ExternalSortEngine.");
        Assert.Equal(0, arbiter.ReservedBytes);
    }

    [Fact]
    public async Task WideRowsBelowRowThreshold_SpillsAndPreservesRetainedPrefixAndUnreadInput()
    {
        var arbiter = new MemoryGrantArbiter(64 * 1024);
        await using var evaluator = await CreateEvaluatorAsync(arbiter);

        // 120 rows x ~8 KB (~960 KB total), well below default row thresholds, ordered backwards.
        // Rows 119..105 fit initially; rows 104..0 are unread when spill triggers and contain the top slice.
        evaluator.Connections["#wide"] = new StreamingRowsDataSource(
            Enumerable.Range(0, 120).Reverse().Select(i => new Row
            {
                ["id"] = (decimal)i,
                ["payload"] = new string((char)('A' + (i % 26)), 4096)
            }));

        await evaluator.Evaluate(Parse("""
            SELECT id, payload
            FROM #wide
            ORDER BY id ASC
            OFFSET 95 ROWS FETCH NEXT 10 ROWS ONLY;
            """));

        var rows = evaluator.LastResult?.Rows ?? [];
        Assert.Equal(10, rows.Count);
        Assert.Equal(Enumerable.Range(95, 10).Select(i => (decimal)i), rows.Select(r => Convert.ToDecimal(r["id"])));
        Assert.True(evaluator.Telemetry.SortSpillCount > 0);
        Assert.Equal(0, arbiter.ReservedBytes);
    }

    [Fact]
    public async Task OperatorGrantSmallerThanProcessGrant_TriggersSpillAndReleasesReservations()
    {
        var arbiter = new MemoryGrantArbiter(64L * 1024 * 1024);
        await using var evaluator = await CreateEvaluatorAsync(arbiter, operatorGrantMb: 1);

        // 250 rows x ~8 KB (~2 MB) fits under the 64 MB process grant, but exceeds the 1 MB operator grant.
        evaluator.Connections["#wide"] = new StreamingRowsDataSource(
            Enumerable.Range(0, 250).Reverse().Select(i => new Row
            {
                ["id"] = (decimal)i,
                ["payload"] = new string('w', 4096)
            }));

        await evaluator.Evaluate(Parse("""
            SELECT id, payload
            FROM #wide
            ORDER BY id ASC
            OFFSET 200 ROWS FETCH NEXT 15 ROWS ONLY;
            """));

        var rows = evaluator.LastResult?.Rows ?? [];
        Assert.Equal(15, rows.Count);
        Assert.Equal(Enumerable.Range(200, 15).Select(i => (decimal)i), rows.Select(r => Convert.ToDecimal(r["id"])));
        Assert.True(evaluator.Telemetry.SortSpillCount > 0);
        Assert.Equal(0, arbiter.ReservedBytes);
    }

    [Fact]
    public async Task SharedGrantPressure_TriggersSpillAndPositionalAliasOrderingMatches()
    {
        var arbiter = new MemoryGrantArbiter(128 * 1024);
        using var competingLease = arbiter.AcquireLease();
        Assert.False(competingLease.RegisterAndCheckSpill(112 * 1024)); // only 16 KB headroom left

        await using var evaluator = await CreateEvaluatorAsync(arbiter);
        evaluator.Connections["#src"] = new StreamingRowsDataSource(
            Enumerable.Range(0, 200).Select(i => new Row
            {
                ["id"] = (decimal)i,
                ["note"] = new string('n', 256)
            }));

        await evaluator.Evaluate(Parse("""
            SELECT id * 2 AS sort_key, id, note
            FROM #src
            ORDER BY 1 DESC
            OFFSET 10 ROWS FETCH NEXT 5 ROWS ONLY;
            """));

        var rows = evaluator.LastResult?.Rows ?? [];
        Assert.Equal(5, rows.Count);
        Assert.Equal([378m, 376m, 374m, 372m, 370m], rows.Select(r => Convert.ToDecimal(r["sort_key"])));
        Assert.True(evaluator.Telemetry.SortSpillCount > 0);
        Assert.Equal(112 * 1024, arbiter.ReservedBytes);

        competingLease.Dispose();
        Assert.Equal(0, arbiter.ReservedBytes);
    }

    [Fact]
    public async Task InMemoryTopN_ReservesAgainstSharedArbiterDuringExecutionAndReleasesOnCompletion()
    {
        var arbiter = new MemoryGrantArbiter(4L * 1024 * 1024);
        await using var evaluator = await CreateEvaluatorAsync(arbiter);

        long maxObservedReserved = 0;
        evaluator.Connections["#src"] = new StreamingRowsDataSource(
            Enumerable.Range(0, 50).Reverse().Select(i => new Row
            {
                ["id"] = (decimal)i,
                ["payload"] = new string('a', 512)
            }),
            onBatchYielded: () =>
            {
                maxObservedReserved = Math.Max(maxObservedReserved, arbiter.ReservedBytes);
            });

        await evaluator.Evaluate(Parse("""
            SELECT id, payload
            FROM #src
            ORDER BY id ASC
            OFFSET 5 ROWS FETCH NEXT 10 ROWS ONLY;
            """));

        var rows = evaluator.LastResult?.Rows ?? [];
        Assert.Equal(10, rows.Count);
        Assert.Equal(Enumerable.Range(5, 10).Select(i => (decimal)i), rows.Select(r => Convert.ToDecimal(r["id"])));
        Assert.True(maxObservedReserved > 0, "TopNFromStream must reserve retained heap bytes against MemoryArbiter.");
        Assert.Equal(0, evaluator.Telemetry.SortSpillCount);
        Assert.Equal(0, arbiter.ReservedBytes);
    }

    [Fact]
    public async Task WiderReplacementRow_UnderTightGrant_SpillsWithoutDroppingAnyRow()
    {
        var arbiter = new MemoryGrantArbiter(16 * 1024);
        await using var evaluator = await CreateEvaluatorAsync(arbiter);

        // First 20 rows are narrow (id = 100..119) and fit in 16 KB.
        // Subsequent rows have better keys (id = 0..19) and 2 KB payloads so replacing into the heap
        // exceeds 16 KB and must spill without losing either the narrow retained rows or unread rows.
        var input = Enumerable.Range(100, 20)
            .Select(i => new Row { ["id"] = (decimal)i, ["payload"] = "small" })
            .Concat(Enumerable.Range(0, 20).Reverse()
                .Select(i => new Row { ["id"] = (decimal)i, ["payload"] = new string('Z', 1024) }));

        evaluator.Connections["#mix"] = new StreamingRowsDataSource(input);

        await evaluator.Evaluate(Parse("""
            SELECT id, payload
            FROM #mix
            ORDER BY id ASC
            OFFSET 15 ROWS FETCH NEXT 10 ROWS ONLY;
            """));

        var rows = evaluator.LastResult?.Rows ?? [];
        Assert.Equal(10, rows.Count);
        Assert.Equal(
            [15m, 16m, 17m, 18m, 19m, 100m, 101m, 102m, 103m, 104m],
            rows.Select(r => Convert.ToDecimal(r["id"])));
        Assert.True(evaluator.Telemetry.SortSpillCount > 0);
        Assert.Equal(0, arbiter.ReservedBytes);
    }

    [Fact]
    public async Task CancellationUnderTinyGrant_ReleasesAllReservationsAndCleansUp()
    {
        var arbiter = new MemoryGrantArbiter(24 * 1024);
        await using var evaluator = await CreateEvaluatorAsync(arbiter);
        using var cts = new CancellationTokenSource();

        int yieldedBatches = 0;
        bool sourceDisposed = false;
        evaluator.Connections["#src"] = new StreamingRowsDataSource(
            Enumerable.Range(0, 500).Reverse().Select(i => new Row
            {
                ["id"] = (decimal)i,
                ["payload"] = new string('c', 512)
            }),
            batchSize: 25,
            onBatchYielded: () =>
            {
                if (++yieldedBatches >= 4)
                    cts.Cancel();
            },
            onDisposed: () => sourceDisposed = true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            evaluator.Evaluate(Parse("""
                SELECT id, payload
                FROM #src
                ORDER BY id ASC
                OFFSET 200 ROWS FETCH NEXT 10 ROWS ONLY;
                """), cts.Token));

        Assert.Equal(0, arbiter.ReservedBytes);
        Assert.True(sourceDisposed);
    }

    [Fact]
    public async Task NegativeOffset_ThrowsExecutionExceptionAndReleasesReservation()
    {
        var arbiter = new MemoryGrantArbiter(64 * 1024);
        await using var evaluator = await CreateEvaluatorAsync(arbiter);
        evaluator.Connections["#src"] = new StreamingRowsDataSource([new Row { ["id"] = 1m }]);

        var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
            evaluator.Evaluate(Parse("SELECT id FROM #src ORDER BY id OFFSET -1 ROWS FETCH NEXT 5 ROWS ONLY;")));

        Assert.Contains("OFFSET", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, arbiter.ReservedBytes);
    }

    private sealed class StreamingRowsDataSource(
        IEnumerable<Row> rows,
        int batchSize = 10,
        Action? onBatchYielded = null,
        Action? onDisposed = null) : IDataSource
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
            try
            {
                int size = Math.Min(batchSize, Math.Max(1, requestedBatchSize));
                for (int i = 0; i < _rows.Count; i += size)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var dt = new DataTable();
                    dt.SetColumns(_rows[0].GetColumnNames());
                    int end = Math.Min(_rows.Count, i + size);
                    for (int j = i; j < end; j++)
                        await dt.AddRowAsync(_rows[j].Clone());
                    yield return dt;
                    onBatchYielded?.Invoke();
                    await Task.Yield();
                }
            }
            finally
            {
                onDisposed?.Invoke();
            }
        }

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => throw new NotSupportedException();
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName) => this;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
