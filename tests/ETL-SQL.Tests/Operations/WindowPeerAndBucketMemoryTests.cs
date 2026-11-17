using System;
using System.Collections.Generic;
using System.IO;
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

public class WindowPeerAndBucketMemoryTests
{
    private static Evaluator CreateEvaluator(
        MemoryGrantArbiter? arbiter = null,
        int windowSpillThreshold = 1000,
        int operatorGrantMb = 1,
        MemoryGovernorPolicy policy = MemoryGovernorPolicy.SpillOrFail)
    {
        var sp = DependencyInjectionSetup.BuildServiceProvider();
        var evaluator = sp.GetRequiredService<Evaluator>();
        evaluator.WindowSpillThreshold = windowSpillThreshold;
        evaluator.OperatorMemoryGrantMB = operatorGrantMb;
        evaluator.MemoryGovernorPolicy = policy;
        if (arbiter is not null)
            evaluator.MemoryArbiter = arbiter;
        return evaluator;
    }

    private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();

    private static List<Row> BuildWideRows(int count, int payloadLength, Func<int, decimal> orderKeySelector) =>
        Enumerable.Range(0, count).Select(i => new Row
        {
            ["id"] = (decimal)i,
            ["grp"] = i % 2 == 0 ? "A" : "B",
            ["k"] = orderKeySelector(i),
            ["val"] = (decimal)(i + 1) * 10m,
            ["tag"] = $"row-{i:D4}",
            ["payload"] = new string((char)('A' + (i % 26)), payloadLength)
        }).ToList();

    private static async Task<Dictionary<decimal, Row>> RunInMemoryOracleAsync(List<Row> input, string sql)
    {
        await using var oracle = CreateEvaluator(
            arbiter: new MemoryGrantArbiter(0),
            windowSpillThreshold: 100_000,
            operatorGrantMb: 256);
        oracle.Connections["#src"] = new StreamingRowsDataSource(input);
        await oracle.Evaluate(Parse(sql));
        return (oracle.LastResult?.Rows ?? []).ToDictionary(r => Convert.ToDecimal(r["id"]));
    }

    private static void AssertSpillRootEmpty(Evaluator evaluator)
    {
        if (Directory.Exists(evaluator.SpillStore.RootPath))
            Assert.Empty(Directory.GetFiles(evaluator.SpillStore.RootPath, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task FirstValueOnly_AllEqualOrderingKeys_StreamsWithoutPeerBufferingUnderTinyGrant()
    {
        var input = BuildWideRows(80, payloadLength: 2048, _ => 1m); // ~320 KB total, well under WindowSpillThreshold=1000
        const string sql = """
            SELECT id,
                   grp,
                   FIRST_VALUE(tag) OVER (PARTITION BY grp ORDER BY k) AS fv,
                   SUM(val) OVER (PARTITION BY grp ORDER BY k) AS total_val
            FROM #src;
            """;
        var oracle = await RunInMemoryOracleAsync(input, sql);

        var arbiter = new MemoryGrantArbiter(24 * 1024);
        await using var evaluator = CreateEvaluator(arbiter, windowSpillThreshold: 1000);
        evaluator.Connections["#src"] = new StreamingRowsDataSource(input);

        await evaluator.Evaluate(Parse(sql));

        var rows = evaluator.LastResult?.Rows ?? [];
        Assert.Equal(input.Count, rows.Count);
        var tagsByGroup = input.GroupBy(r => r["grp"]?.ToString()!)
            .ToDictionary(g => g.Key, g => g.Select(r => r["tag"]?.ToString()!).ToHashSet());
        foreach (var grpGroup in rows.GroupBy(r => r["grp"]?.ToString()!))
        {
            var distinctFv = grpGroup.Select(r => r["fv"]?.ToString()).Distinct().ToList();
            Assert.Single(distinctFv);
            Assert.Contains(distinctFv[0], tagsByGroup[grpGroup.Key]);
        }
        foreach (var row in rows)
            Assert.Equal(Convert.ToDecimal(oracle[Convert.ToDecimal(row["id"])]["total_val"]), Convert.ToDecimal(row["total_val"]));
        Assert.Equal(0, arbiter.ReservedBytes);
        AssertSpillRootEmpty(evaluator);
    }

    [Fact]
    public async Task LastValue_AllEqualOrderingKeys_WideRowsBelowThreshold_SpillsPeersAndMatchesOracle()
    {
        // All-equal ordering keys put an entire partition into one peer group while staying below WindowSpillThreshold.
        var input = BuildWideRows(80, payloadLength: 2048, _ => 42m);
        const string sql = """
            SELECT id,
                   grp,
                   FIRST_VALUE(tag) OVER (PARTITION BY grp ORDER BY k) AS fv,
                   LAST_VALUE(tag) OVER (PARTITION BY grp ORDER BY k) AS lv,
                   SUM(val) OVER (PARTITION BY grp ORDER BY k) AS running_sum
            FROM #src;
            """;
        var oracle = await RunInMemoryOracleAsync(input, sql);

        var arbiter = new MemoryGrantArbiter(24 * 1024);
        await using var evaluator = CreateEvaluator(arbiter, windowSpillThreshold: 1000);
        evaluator.Connections["#src"] = new StreamingRowsDataSource(input);

        await evaluator.Evaluate(Parse(sql));

        var rows = evaluator.LastResult?.Rows ?? [];
        Assert.Equal(input.Count, rows.Count);
        var tagsByGroup = input.GroupBy(r => r["grp"]?.ToString()!)
            .ToDictionary(g => g.Key, g => g.Select(r => r["tag"]?.ToString()!).ToHashSet());
        foreach (var grpGroup in rows.GroupBy(r => r["grp"]?.ToString()!))
        {
            var distinctFv = grpGroup.Select(r => r["fv"]?.ToString()).Distinct().ToList();
            var distinctLv = grpGroup.Select(r => r["lv"]?.ToString()).Distinct().ToList();
            Assert.Single(distinctFv);
            Assert.Single(distinctLv);
            Assert.Contains(distinctFv[0], tagsByGroup[grpGroup.Key]);
            Assert.Contains(distinctLv[0], tagsByGroup[grpGroup.Key]);
        }
        foreach (var row in rows)
        {
            var expected = oracle[Convert.ToDecimal(row["id"])];
            Assert.Equal(Convert.ToDecimal(expected["running_sum"]), Convert.ToDecimal(row["running_sum"]));
        }
        Assert.True(evaluator.Telemetry.SortSpillCount > 0 || evaluator.Telemetry.PartitionsCount > 0);
        Assert.Equal(0, arbiter.ReservedBytes);
        AssertSpillRootEmpty(evaluator);
    }

    [Fact]
    public async Task MixedWindowFunctions_WithTiesAndWideRows_UnderTinyGrant_MatchInMemoryOracle()
    {
        var input = BuildWideRows(70, payloadLength: 1500, i => (decimal)(i / 5));
        const string sql = """
            SELECT id,
                   grp,
                   k,
                   RANK() OVER (PARTITION BY grp ORDER BY k) AS rnk,
                   DENSE_RANK() OVER (PARTITION BY grp ORDER BY k) AS drnk,
                   SUM(val) OVER (PARTITION BY grp ORDER BY k) AS running_sum,
                   PERCENT_RANK() OVER (PARTITION BY grp ORDER BY k) AS pct_rnk,
                   CUME_DIST() OVER (PARTITION BY grp ORDER BY k) AS cume
            FROM #src;
            """;
        var oracle = await RunInMemoryOracleAsync(input, sql);

        var arbiter = new MemoryGrantArbiter(32 * 1024);
        await using var evaluator = CreateEvaluator(arbiter, windowSpillThreshold: 1000);
        evaluator.Connections["#src"] = new StreamingRowsDataSource(input);

        await evaluator.Evaluate(Parse(sql));

        var rows = evaluator.LastResult?.Rows ?? [];
        Assert.Equal(input.Count, rows.Count);
        foreach (var row in rows)
        {
            var expected = oracle[Convert.ToDecimal(row["id"])];
            Assert.Equal(Convert.ToDecimal(expected["rnk"]), Convert.ToDecimal(row["rnk"]));
            Assert.Equal(Convert.ToDecimal(expected["drnk"]), Convert.ToDecimal(row["drnk"]));
            Assert.Equal(Convert.ToDecimal(expected["running_sum"]), Convert.ToDecimal(row["running_sum"]));
            Assert.Equal(Convert.ToDecimal(expected["pct_rnk"]), Convert.ToDecimal(row["pct_rnk"]));
            Assert.Equal(Convert.ToDecimal(expected["cume"]), Convert.ToDecimal(row["cume"]));
        }
        Assert.Equal(0, arbiter.ReservedBytes);
        AssertSpillRootEmpty(evaluator);
    }

    [Fact]
    public async Task MixedWindowFunctions_DeterministicOrderAndWideRows_UnderTinyGrant_MatchInMemoryOracle()
    {
        // Mixes ranking, running SUM, FIRST_VALUE, LAST_VALUE, LAG, LEAD, and PERCENT_RANK
        // on the same PARTITION/ORDER signature with wide payloads under a tiny grant.
        var input = BuildWideRows(70, payloadLength: 1500, i => (decimal)(i / 5));
        const string sql = """
            SELECT id,
                   grp,
                   k,
                   ROW_NUMBER() OVER (PARTITION BY grp ORDER BY k, id) AS rn,
                   RANK() OVER (PARTITION BY grp ORDER BY k, id) AS rnk,
                   SUM(val) OVER (PARTITION BY grp ORDER BY k, id) AS running_sum,
                   FIRST_VALUE(tag) OVER (PARTITION BY grp ORDER BY k, id) AS fv,
                   LAST_VALUE(tag) OVER (PARTITION BY grp ORDER BY k, id) AS lv,
                   LAG(val, 1, -1) OVER (PARTITION BY grp ORDER BY k, id) AS prev_val,
                   LEAD(val, 1, -1) OVER (PARTITION BY grp ORDER BY k, id) AS next_val,
                   PERCENT_RANK() OVER (PARTITION BY grp ORDER BY k, id) AS pct_rnk
            FROM #src;
            """;
        var oracle = await RunInMemoryOracleAsync(input, sql);

        var arbiter = new MemoryGrantArbiter(32 * 1024);
        await using var evaluator = CreateEvaluator(arbiter, windowSpillThreshold: 1000);
        evaluator.Connections["#src"] = new StreamingRowsDataSource(input);

        await evaluator.Evaluate(Parse(sql));

        var rows = evaluator.LastResult?.Rows ?? [];
        Assert.Equal(input.Count, rows.Count);
        foreach (var row in rows)
        {
            var id = Convert.ToDecimal(row["id"]);
            var expected = oracle[id];
            Assert.Equal(Convert.ToDecimal(expected["rn"]), Convert.ToDecimal(row["rn"]));
            Assert.Equal(Convert.ToDecimal(expected["rnk"]), Convert.ToDecimal(row["rnk"]));
            Assert.Equal(Convert.ToDecimal(expected["running_sum"]), Convert.ToDecimal(row["running_sum"]));
            Assert.Equal(expected["fv"], row["fv"]);
            Assert.Equal(expected["lv"], row["lv"]);
            Assert.Equal(Convert.ToDecimal(expected["prev_val"]), Convert.ToDecimal(row["prev_val"]));
            Assert.Equal(Convert.ToDecimal(expected["next_val"]), Convert.ToDecimal(row["next_val"]));
            Assert.Equal(Convert.ToDecimal(expected["pct_rnk"]), Convert.ToDecimal(row["pct_rnk"]));
        }
        Assert.Equal(0, arbiter.ReservedBytes);
        AssertSpillRootEmpty(evaluator);
    }

    [Fact]
    public async Task UnsupportedFallbackBucket_WideRowsBelowThreshold_UnderTinyGrant_FailsClosedAndCleansUp()
    {
        var input = BuildWideRows(60, payloadLength: 2048, i => (decimal)i); // ~240 KB, below WindowSpillThreshold=1000
        const string sql = """
            SELECT id,
                   SUM(val) OVER (PARTITION BY grp ORDER BY k ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING) AS centered_sum
            FROM #src;
            """;

        var arbiter = new MemoryGrantArbiter(24 * 1024);
        await using var evaluator = CreateEvaluator(arbiter, windowSpillThreshold: 1000);
        evaluator.Connections["#src"] = new StreamingRowsDataSource(input);

        var ex = await Assert.ThrowsAsync<ExecutionException>(() => evaluator.Evaluate(Parse(sql)));
        Assert.Contains("memory grant", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, arbiter.ReservedBytes);
        AssertSpillRootEmpty(evaluator);
    }

    [Fact]
    public async Task CancellationDuringPeerSpill_ReleasesReservationsAndDeletesSpillFiles()
    {
        var input = BuildWideRows(100, payloadLength: 2048, _ => 1m);
        const string sql = """
            SELECT id,
                   SUM(val) OVER (PARTITION BY grp ORDER BY k) AS running_sum,
                   LAST_VALUE(tag) OVER (PARTITION BY grp ORDER BY k) AS lv
            FROM #src;
            """;

        var arbiter = new MemoryGrantArbiter(24 * 1024);
        await using var evaluator = CreateEvaluator(arbiter, windowSpillThreshold: 1000);
        using var cts = new CancellationTokenSource();
        int batches = 0;
        evaluator.Connections["#src"] = new StreamingRowsDataSource(
            input,
            batchSize: 10,
            onBatchYielded: () =>
            {
                if (++batches >= 4)
                    cts.Cancel();
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => evaluator.Evaluate(Parse(sql), cts.Token));
        Assert.Equal(0, arbiter.ReservedBytes);
        AssertSpillRootEmpty(evaluator);
    }

    private sealed class StreamingRowsDataSource(
        IEnumerable<Row> rows,
        int batchSize = 10,
        Action? onBatchYielded = null) : IDataSource
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

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => throw new NotSupportedException();
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName) => this;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
