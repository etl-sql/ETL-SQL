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
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Planning;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Operations;

[Trait("CompatBreak", "0.23")]
public class AggregateMemoryContractTests
{
    private static Evaluator CreateEvaluator(
        IMemoryGrantArbiter arbiter,
        int operatorGrantMb = 1,
        int externalPartitions = 4,
        int joinSpillThreshold = 1000,
        MemoryGovernorPolicy policy = MemoryGovernorPolicy.SpillOrFail)
    {
        var sp = DependencyInjectionSetup.BuildServiceProvider();
        var evaluator = sp.GetRequiredService<Evaluator>();
        evaluator.MemoryArbiter = arbiter;
        evaluator.OperatorMemoryGrantMB = operatorGrantMb;
        evaluator.ExternalHashPartitions = externalPartitions;
        evaluator.JoinSpillThreshold = joinSpillThreshold;
        evaluator.MemoryGovernorPolicy = policy;
        return evaluator;
    }

    private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();

    private static SelectStatement ParseSelect(string sql)
        => Assert.IsType<SelectStatement>(Parse(sql).Statements.Single());

    private static void AssertSpillRootEmpty(Evaluator evaluator)
    {
        if (Directory.Exists(evaluator.SpillStore.RootPath))
            Assert.Empty(Directory.GetFiles(evaluator.SpillStore.RootPath, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task HighCardinality_OperatorGrantSmallerThanProcessGrant_RepartitionsAndReleasesReservations()
    {
        var arbiter = new MemoryGrantArbiter(64L * 1024 * 1024);
        await using var evaluator = CreateEvaluator(
            arbiter,
            operatorGrantMb: 1,
            externalPartitions: 4,
            joinSpillThreshold: 100,
            policy: MemoryGovernorPolicy.SpillOrFail);

        // 5,000 distinct 300-char keys (~2.8 MB of key/state) with a 1 MiB operator grant inside a 64 MiB process budget.
        var suffix = new string('k', 280);
        var rows = Enumerable.Range(0, 5000).Select(i => new Row
        {
            ["grp_key"] = $"grp-{i:D5}-{suffix}",
            ["amt"] = (decimal)(i % 17 + 1)
        }).ToList();

        evaluator.Connections["#src"] = new TestStreamingRowsDataSource(rows);

        const string sql = """
            SELECT grp_key, COUNT(*) AS cnt, SUM(amt) AS total_amt
            FROM #src
            GROUP BY grp_key
            """;

        await evaluator.Evaluate(Parse(sql));
        var result = evaluator.LastResult?.Rows ?? [];

        Assert.Equal(5000, result.Count);
        var expectedSum = Enumerable.Range(0, 5000).Sum(i => (decimal)(i % 17 + 1));
        Assert.Equal(expectedSum, result.Sum(r => Convert.ToDecimal(r["total_amt"])));
        Assert.True(result.All(r => Convert.ToDecimal(r["cnt"]) == 1m));
        Assert.Equal(0, arbiter.ReservedBytes);
        AssertSpillRootEmpty(evaluator);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(4000)]
    public async Task NativeSingleAndCompositeGroupBy_ExceedOperatorGrant_FallBackToExactRowResults(int rowsPerBatch)
    {
        var arbiter = new MemoryGrantArbiter(64L * 1024 * 1024);
        await using var evaluator = CreateEvaluator(
            arbiter,
            operatorGrantMb: 1,
            externalPartitions: 8,
            joinSpillThreshold: 100,
            policy: MemoryGovernorPolicy.SpillOrFail);

        // Cross the 1 MiB operator ceiling either in the first batch or mid-stream,
        // while remaining below the 64 MiB process grant.
        var pad = new string('p', 320);
        var batchCount = 4000 / rowsPerBatch;
        var batches = new List<ColumnBatch>(batchCount);
        var schema = new ColumnBatchSchema(new[]
        {
            new ColumnBatchField("Region", typeof(string), "VARCHAR(16)"),
            new ColumnBatchField("GroupKey", typeof(string), "VARCHAR(512)"),
            new ColumnBatchField("Amount", typeof(int), "INT")
        });

        for (var b = 0; b < batchCount; b++)
        {
            var regions = new string[rowsPerBatch];
            var groupKeys = new string[rowsPerBatch];
            var amounts = new int[rowsPerBatch];
            for (var i = 0; i < rowsPerBatch; i++)
            {
                var id = b * rowsPerBatch + i;
                regions[i] = (id % 2 == 0) ? "EAST" : "WEST";
                groupKeys[i] = $"k-{id:D5}-{pad}";
                amounts[i] = (id % 10) + 1;
            }
            batches.Add(new ColumnBatch(
                schema,
                new IColumnBuffer[]
                {
                    Utf8ColumnBuffer.FromStrings(regions),
                    Utf8ColumnBuffer.FromStrings(groupKeys),
                    new ColumnBuffer<int>(amounts, rowsPerBatch)
                },
                rowsPerBatch));
        }

        await using var nativeSource = new TestReplayableColumnarDataSource(batches, throwOnRowRead: true);
        evaluator.Connections["native_src"] = nativeSource;

        var handler = new SelectStatementHandler(NullLogger.Instance);

        // 1. Single-key native GROUP BY exceeding 1 MiB operator grant -> fallback mid-stream to row pipeline.
        var singleStmt = ParseSelect(
            "SELECT GroupKey, COUNT(*) AS cnt, SUM(Amount) AS total_amt FROM native_src GROUP BY GroupKey;");
        var singleRows = (await handler.EvaluateQuery(singleStmt, evaluator).ToListAsync())
            .SelectMany(t => t.Rows)
            .ToList();

        var expectedTotal = Enumerable.Range(0, batchCount * rowsPerBatch).Sum(id => (decimal)((id % 10) + 1));
        Assert.Equal(batchCount * rowsPerBatch, singleRows.Count);
        Assert.Equal(expectedTotal, singleRows.Sum(r => Convert.ToDecimal(r["total_amt"])));
        Assert.Equal(0, nativeSource.RowReadAttempts);
        Assert.Equal(0, arbiter.ReservedBytes);
        Assert.Contains(evaluator.Telemetry.PlanDecisions,
            d => d.CandidatePath == "ColumnarGroupedAggregate"
                && d.Outcome == PlanDecisionOutcome.Fallback
                && d.ReasonCode == PlanDecisionReasonCodes.MemoryAdmissionRejected);
        AssertSpillRootEmpty(evaluator);

        // 2. Composite-key native GROUP BY exceeding 1 MiB operator grant -> fallback mid-stream to row pipeline.
        evaluator.Telemetry.Clear();
        var compositeStmt = ParseSelect(
            "SELECT Region, GroupKey, SUM(Amount) AS total_amt FROM native_src GROUP BY Region, GroupKey;");
        var compositeRows = (await handler.EvaluateQuery(compositeStmt, evaluator).ToListAsync())
            .SelectMany(t => t.Rows)
            .ToList();

        Assert.Equal(batchCount * rowsPerBatch, compositeRows.Count);
        Assert.Equal(expectedTotal, compositeRows.Sum(r => Convert.ToDecimal(r["total_amt"])));
        Assert.Equal(0, nativeSource.RowReadAttempts);
        Assert.Equal(0, arbiter.ReservedBytes);
        Assert.Contains(evaluator.Telemetry.PlanDecisions,
            d => d.CandidatePath == "ColumnarGroupedAggregate"
                && d.Outcome == PlanDecisionOutcome.Fallback
                && d.ReasonCode == PlanDecisionReasonCodes.MemoryAdmissionRejected);
        AssertSpillRootEmpty(evaluator);
    }

    [Fact]
    public async Task GroupingSets_HighCardinality_UnderOperatorGrant_RepartitionsAndPreservesExactResults()
    {
        var arbiter = new MemoryGrantArbiter(64L * 1024 * 1024);
        await using var evaluator = CreateEvaluator(
            arbiter,
            operatorGrantMb: 1,
            externalPartitions: 4,
            joinSpillThreshold: 100,
            policy: MemoryGovernorPolicy.SpillOrFail);

        // Each input row expands into 3 grouping sets ((cat, sub), (cat), ()) and has long keys
        // so partitioning + set aggregation must stay bounded under the 1 MiB operator ceiling.
        var padCat = new string('c', 220);
        var padSub = new string('s', 220);
        const int rowCount = 3000;
        var rows = Enumerable.Range(0, rowCount).Select(i => new Row
        {
            ["cat"] = $"cat-{i:D4}-{padCat}",
            ["sub"] = $"sub-{i:D4}-{padSub}",
            ["amt"] = (decimal)((i % 5) + 1)
        }).ToList();

        evaluator.Connections["#gs_src"] = new TestStreamingRowsDataSource(rows);

        const string sql = """
            SELECT cat, sub, SUM(amt) AS total_amt
            FROM #gs_src
            GROUP BY GROUPING SETS ((cat, sub), (cat), ())
            """;

        await evaluator.Evaluate(Parse(sql));
        var result = evaluator.LastResult?.Rows ?? [];

        // 3000 detail rows + 3000 cat rows + 1 grand total row = 6001 rows.
        Assert.Equal(6001, result.Count);
        var baseSum = rows.Sum(r => Convert.ToDecimal(r["amt"]));
        var grandTotal = Assert.Single(result, r => r["cat"] == null && r["sub"] == null);
        Assert.Equal(baseSum, Convert.ToDecimal(grandTotal["total_amt"]));
        Assert.Equal(baseSum * 3m, result.Sum(r => Convert.ToDecimal(r["total_amt"])));
        Assert.Equal(0, arbiter.ReservedBytes);
        AssertSpillRootEmpty(evaluator);
    }

    [Fact]
    public async Task UnsplittableGroupingSetsAndSharedPressure_EnforceSpillOrFailAndReleaseReservations()
    {
        // 1. Unsplittable holistic GROUPING SETS ((cat), ()) exceeding 1 MiB operator ceiling under SpillOrFail
        //    fails closed and releases its reservation.
        var arbiter = new MemoryGrantArbiter(64L * 1024 * 1024);
        await using (var evaluator = CreateEvaluator(
            arbiter,
            operatorGrantMb: 1,
            externalPartitions: 4,
            joinSpillThreshold: 100,
            policy: MemoryGovernorPolicy.SpillOrFail))
        {
            var payload = new string('z', 1500);
            var rows = Enumerable.Range(0, 1200).Select(i => new Row
            {
                ["cat"] = "SINGLE_HOT_GROUP",
                ["payload"] = $"{i:D4}-{payload}"
            }).ToList();
            evaluator.Connections["#unsplittable"] = new TestStreamingRowsDataSource(rows);

            const string sql = """
                SELECT cat, STRING_AGG(payload, ',') AS joined
                FROM #unsplittable
                GROUP BY GROUPING SETS ((cat), ())
                """;

            await Assert.ThrowsAsync<ExecutionException>(() => evaluator.Evaluate(Parse(sql)));
            Assert.Equal(0, arbiter.ReservedBytes);
            AssertSpillRootEmpty(evaluator);
        }

        // 2. Shared reservation pressure reduces effective headroom on ordinary aggregation:
        //    even though OperatorMemoryGrantMB is 16 MB and row count < JoinSpillThreshold,
        //    the 62 KB remaining shared headroom forces spill + dynamic partitioning to complete
        //    with exact results and releases all reservations back to the competing holder.
        var sharedArbiter = new MemoryGrantArbiter(512L * 1024);
        using var externalHolder = sharedArbiter.AcquireLease();
        Assert.False(externalHolder.RegisterAndCheckSpill(450L * 1024)); // Only 62 KB free in shared pool.

        await using (var sharedEval = CreateEvaluator(
            sharedArbiter,
            operatorGrantMb: 16, // Operator grant larger than remaining shared headroom!
            externalPartitions: 4,
            joinSpillThreshold: 10_000,
            policy: MemoryGovernorPolicy.SpillOrFail))
        {
            var pad = new string('m', 120);
            var rows = Enumerable.Range(0, 1000).Select(i => new Row
            {
                ["grp"] = $"g-{i:D4}-{pad}",
                ["v"] = 1m
            }).ToList();
            sharedEval.Connections["#shared_src"] = new TestStreamingRowsDataSource(rows);

            const string sql = "SELECT grp, COUNT(*) AS cnt FROM #shared_src GROUP BY grp";
            await sharedEval.Evaluate(Parse(sql));
            Assert.Equal(1000, (sharedEval.LastResult?.Rows ?? []).Count);
            Assert.True(sharedEval.Telemetry.TotalSpilledBytes > 0);
            Assert.Equal(450L * 1024, sharedArbiter.ReservedBytes);
            AssertSpillRootEmpty(sharedEval);
        }

        // 3. When remaining shared headroom is too tight even after partitioning (< 10 KB),
        //    unsplittable holistic state fails closed under SpillOrFail and releases its lease.
        Assert.False(externalHolder.RegisterAndCheckSpill(508L * 1024)); // Only 4 KB free.
        await using (var tightEval = CreateEvaluator(
            sharedArbiter,
            operatorGrantMb: 16,
            externalPartitions: 4,
            joinSpillThreshold: 10_000,
            policy: MemoryGovernorPolicy.SpillOrFail))
        {
            var rows = Enumerable.Range(0, 100).Select(i => new Row
            {
                ["grp"] = "HOT",
                ["payload"] = $"row-{i:D3}-" + new string('q', 100)
            }).ToList();
            tightEval.Connections["#tight_src"] = new TestStreamingRowsDataSource(rows);

            const string sql = "SELECT grp, STRING_AGG(payload, ',') AS joined FROM #tight_src GROUP BY grp";
            await Assert.ThrowsAsync<ExecutionException>(() => tightEval.Evaluate(Parse(sql)));
            Assert.Equal(508L * 1024, sharedArbiter.ReservedBytes);
            AssertSpillRootEmpty(tightEval);
        }

        externalHolder.Dispose();
        Assert.Equal(0, sharedArbiter.ReservedBytes);
    }

    private sealed class TestStreamingRowsDataSource(IEnumerable<Row> rows) : IDataSource
    {
        private readonly List<Row> _rows = rows.ToList();
        public string ConnectorType => "TEST";
        public string Path => "#mem_test";
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
            var size = Math.Max(1, Math.Min(250, requestedBatchSize));
            for (var i = 0; i < _rows.Count; i += size)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dt = new DataTable();
                dt.SetColumns(_rows[0].GetColumnNames());
                var end = Math.Min(_rows.Count, i + size);
                for (var j = i; j < end; j++)
                    await dt.AddRowAsync(_rows[j].Clone());
                yield return dt;
            }
        }

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => throw new NotSupportedException();
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName) => this;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestReplayableColumnarDataSource(
        IEnumerable<ColumnBatch> batches,
        bool throwOnRowRead) : IDataSource, IReplayableColumnarDataSource, IEstimatedCardinalityDataSource
    {
        private List<ColumnBatch>? _batches = batches.ToList();
        public int RowReadAttempts { get; private set; }
        public string Path => string.Empty;
        public Dictionary<string, string>? Options => null;
        public string ConnectorType => "TEST_COLUMNAR";
        public long EstimatedRowCount => (_batches ?? throw new ObjectDisposedException(nameof(TestReplayableColumnarDataSource)))
            .Sum(b => (long)b.RowCount);

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10_000)
        {
            RowReadAttempts++;
            await Task.Yield();
            if (throwOnRowRead)
                throw new InvalidOperationException("Native columnar fallback must replay column batches instead of calling ReadBatches.");
            foreach (var batch in _batches ?? throw new ObjectDisposedException(nameof(TestReplayableColumnarDataSource)))
            {
                using var retained = batch.Retain();
                yield return ColumnBatchAdapter.ToDataTable(retained);
            }
        }

        public async IAsyncEnumerable<ColumnBatch> ReadColumnBatches(
            int batchSize = 10_000,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            foreach (var batch in _batches ?? throw new ObjectDisposedException(nameof(TestReplayableColumnarDataSource)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return batch.Retain();
            }
        }

        public Task<IEnumerable<string>> GetColumnsAsync() =>
            Task.FromResult<IEnumerable<string>>(
                (_batches ?? throw new ObjectDisposedException(nameof(TestReplayableColumnarDataSource)))[0]
                .Schema.Fields.Select(f => f.Name).ToArray());

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => throw new NotSupportedException();
        public object? Snapshot() => throw new NotSupportedException();
        public void Restore(object? snapshot) => throw new NotSupportedException();
        public IDataSource WithTable(string tableName) => this;

        public ValueTask DisposeAsync()
        {
            var batches = Interlocked.Exchange(ref _batches, null);
            if (batches != null)
                foreach (var batch in batches) batch.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
