using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using Xunit;
using Xunit.Abstractions;

namespace ETL_SQL.Tests.Scale;

public sealed class ColumnarOperatorGateTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "ScaleAssessment")]
    public void NativeFilterProjectionAndGroupMatchRowsAndReportThroughput()
    {
        var rowCount = ReadPositiveInt("COLUMNAR_GATE_ROWS", 100_000);
        var batchSize = Math.Min(rowCount, ReadPositiveInt("COLUMNAR_GATE_BATCH_ROWS", 100_000));
        var minimumSpeedup = ReadPositiveDouble("COLUMNAR_GATE_MIN_SPEEDUP");

        // Warm both paths so one-time JIT cost is outside the measurement.
        _ = RunRows(2_000);
        using (RunNative(2_000, 2_000)) { }
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();

        var rowResult = RunRows(rowCount);
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        using var nativeResult = RunNative(rowCount, batchSize);

        Assert.Equal(rowResult.SelectedCount, nativeResult.SelectedCount);
        Assert.Equal(rowResult.ProjectionChecksum, nativeResult.ProjectionChecksum);
        Assert.Equal(rowResult.GroupSums.OrderBy(pair => pair.Key), nativeResult.GroupSums.OrderBy(pair => pair.Key));

        var rowRate = rowCount / Math.Max(0.001, rowResult.Elapsed.TotalSeconds);
        var nativeRate = rowCount / Math.Max(0.001, nativeResult.Elapsed.TotalSeconds);
        var speedup = nativeRate / rowRate;
        var metric = new
        {
            rowCount,
            batchSize,
            selectedRows = nativeResult.SelectedCount,
            rowElapsedMs = Math.Round(rowResult.Elapsed.TotalMilliseconds, 3),
            nativeElapsedMs = Math.Round(nativeResult.Elapsed.TotalMilliseconds, 3),
            rowRowsPerSecond = Math.Round(rowRate, 2),
            nativeRowsPerSecond = Math.Round(nativeRate, 2),
            speedup = Math.Round(speedup, 3),
            minimumSpeedup,
            checksum = nativeResult.ProjectionChecksum,
            groups = nativeResult.GroupSums.Count
        };
        var json = JsonSerializer.Serialize(metric);
        output.WriteLine("COLUMNAR_OPERATOR_GATE " + json);
        var outputPath = Environment.GetEnvironmentVariable("COLUMNAR_GATE_OUTPUT");
        if (!string.IsNullOrWhiteSpace(outputPath)) File.WriteAllText(outputPath, json);

        if (minimumSpeedup.HasValue)
            Assert.True(speedup >= minimumSpeedup.Value,
                $"Native operator speedup {speedup:F3}x was below required {minimumSpeedup.Value:F3}x.");
    }

    private static GateResult RunRows(int rowCount)
    {
        var groups = new Dictionary<int, decimal>();
        long selected = 0;
        long checksum = 0;
        var threshold = rowCount / 2;
        var stopwatch = Stopwatch.StartNew();
        for (var id = 1; id <= rowCount; id++)
        {
            var row = new Row
            {
                ["Id"] = id,
                ["GroupId"] = id % 100,
                ["Amount"] = (long)(id % 1000)
            };
            if (Convert.ToInt32(row["Id"]) <= threshold) continue;
            selected++;
            var group = Convert.ToInt32(row["GroupId"]);
            var amount = Convert.ToInt64(row["Amount"]);
            checksum = checked(checksum + Convert.ToInt64(row["Id"]) + amount * 2);
            groups[group] = groups.TryGetValue(group, out var sum) ? sum + amount : amount;
        }
        stopwatch.Stop();
        return new GateResult(selected, checksum, groups, stopwatch.Elapsed);
    }

    private static NativeGateResult RunNative(int rowCount, int batchSize)
    {
        NativeGroupAggregateResult<int, long>? groups = null;
        long selected = 0;
        long checksum = 0;
        var threshold = rowCount / 2;
        var stopwatch = Stopwatch.StartNew();
        for (var start = 1; start <= rowCount; start += batchSize)
        {
            var count = Math.Min(batchSize, rowCount - start + 1);
            var ids = ColumnBuffer<int>.Rent(count);
            var groupIds = ColumnBuffer<int>.Rent(count);
            var amounts = ColumnBuffer<long>.Rent(count);
            for (var offset = 0; offset < count; offset++)
            {
                var id = start + offset;
                ids.Values.Span[offset] = id;
                groupIds.Values.Span[offset] = id % 100;
                amounts.Values.Span[offset] = id % 1000;
            }
            using var batch = new ColumnBatch(
                new ColumnBatchSchema(new[]
                {
                    new ColumnBatchField("Id", typeof(int), "INT", false),
                    new ColumnBatchField("GroupId", typeof(int), "INT", false),
                    new ColumnBatchField("Amount", typeof(long), "BIGINT", false)
                }),
                new IColumnBuffer[] { ids, groupIds, amounts }, count);
            using var selection = ColumnBatchKernels.SelectComparison(
                batch, "Id", ColumnComparison.GreaterThan, threshold);
            selected += selection.Count;
            foreach (var row in selection.Indices.Span)
                checksum = checked(checksum + ids.Values.Span[row] + amounts.Values.Span[row] * 2);
            if (groups == null)
                groups = ColumnBatchGroupKernels.GroupAggregate<int, long>(
                    batch, "GroupId", "Amount", selection: selection);
            else
                groups.Accumulate(batch, "GroupId", "Amount", selection);
        }
        stopwatch.Stop();
        var sums = groups?.Groups.ToDictionary(pair => pair.Key.Value, pair => pair.Value.Sum)
            ?? new Dictionary<int, decimal>();
        return new NativeGateResult(selected, checksum, sums, stopwatch.Elapsed, groups);
    }

    private static int ReadPositiveInt(string name, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var value) && value > 0 ? value : fallback;

    private static double? ReadPositiveDouble(string name)
        => double.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;

    private sealed record GateResult(long SelectedCount, long ProjectionChecksum,
        IReadOnlyDictionary<int, decimal> GroupSums, TimeSpan Elapsed);

    private sealed class NativeGateResult(long selectedCount, long projectionChecksum,
        IReadOnlyDictionary<int, decimal> groupSums, TimeSpan elapsed,
        NativeGroupAggregateResult<int, long>? state) : IDisposable
    {
        public long SelectedCount { get; } = selectedCount;
        public long ProjectionChecksum { get; } = projectionChecksum;
        public IReadOnlyDictionary<int, decimal> GroupSums { get; } = groupSums;
        public TimeSpan Elapsed { get; } = elapsed;
        public void Dispose() => state?.Dispose();
    }
}
