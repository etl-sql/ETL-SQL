using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ETL_SQL.Core.Data;
using Xunit;
using Xunit.Abstractions;

namespace ETL_SQL.Tests.Scale;

public sealed class BillionRowCertificationTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "BillionRowCertification")]
    public void NativeScanFilterProjectionAndLowCardinalityAggregateStayBounded()
    {
        var rowCount = ReadPositiveLong("GATE_F_ROWS", 1_000_000_000);
        var batchSize = ReadPositiveInt("GATE_F_BATCH_ROWS", 100_000);
        var memoryBoundMb = ReadPositiveDouble("GATE_F_MEMORY_BOUND_MB") ?? 16_384;
        var minimumRowsPerSecond = ReadPositiveDouble("GATE_F_MIN_ROWS_PER_SECOND");

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        using var sampler = new ScenarioResourceSampler();
        using var result = RunNative(rowCount, batchSize);
        var resources = sampler.SnapshotAndReset();

        var threshold = checked((int)(rowCount / 2));
        var expectedSelected = rowCount - threshold;
        var expectedIdSum = SumRange(threshold + 1, rowCount);
        var expectedAmountSum = SumModuloRange(threshold + 1, rowCount, 1_000);
        var expectedChecksum = checked(expectedIdSum + expectedAmountSum * 2);
        var rowsPerSecond = rowCount / Math.Max(0.001, result.Elapsed.TotalSeconds);
        var peakWorkingSetMb = resources.PeakWorkingSetBytes / 1024d / 1024d;

        Assert.Equal(expectedSelected, result.SelectedCount);
        Assert.Equal(expectedChecksum, result.ProjectionChecksum);
        Assert.Equal(100, result.GroupSums.Count);
        Assert.Equal((decimal)expectedAmountSum, result.GroupSums.Values.Sum());
        Assert.True(peakWorkingSetMb < memoryBoundMb,
            $"Peak process working set {peakWorkingSetMb:N1} MB exceeded {memoryBoundMb:N1} MB.");
        if (minimumRowsPerSecond.HasValue)
            Assert.True(rowsPerSecond >= minimumRowsPerSecond.Value,
                $"Throughput {rowsPerSecond:N0} rows/s was below {minimumRowsPerSecond:N0} rows/s.");

        var metric = new
        {
            scenario = "GateF_NativeScanFilterProjectionAggregate",
            rowCount,
            batchSize,
            selectedRows = result.SelectedCount,
            checksum = result.ProjectionChecksum,
            groups = result.GroupSums.Count,
            elapsedMs = Math.Round(result.Elapsed.TotalMilliseconds, 3),
            rowsPerSecond = Math.Round(rowsPerSecond, 2),
            peakProcessWorkingSetMB = Math.Round(peakWorkingSetMb, 1),
            peakPrivateBytesMB = Math.Round(resources.PeakPrivateBytes / 1024d / 1024d, 1),
            peakManagedHeapMB = Math.Round(resources.PeakManagedHeapBytes / 1024d / 1024d, 1),
            allocatedMB = Math.Round(resources.AllocatedBytes / 1024d / 1024d, 1),
            gcGen0Collections = resources.Gen0Collections,
            gcGen1Collections = resources.Gen1Collections,
            gcGen2Collections = resources.Gen2Collections,
            gcPauseMs = Math.Round(resources.GcPauseTime.TotalMilliseconds, 1),
            cpuTimeMs = Math.Round(resources.CpuTime.TotalMilliseconds, 1),
            cpuUtilizationPercent = resources.CpuUtilizationPercent,
            memoryBoundMB = memoryBoundMb,
            minimumRowsPerSecond,
            passed = true
        };
        var json = JsonSerializer.Serialize(metric);
        output.WriteLine("GATE_F_METRIC:" + json);
        var outputPath = Environment.GetEnvironmentVariable("GATE_F_OUTPUT");
        if (!string.IsNullOrWhiteSpace(outputPath)) File.WriteAllText(outputPath, json);
    }

    private static NativeResult RunNative(long rowCount, int batchSize)
    {
        NativeGroupAggregateResult<int, long>? groups = null;
        long selected = 0;
        long checksum = 0;
        var threshold = checked((int)(rowCount / 2));
        var stopwatch = Stopwatch.StartNew();
        for (long start = 1; start <= rowCount; start += batchSize)
        {
            var count = (int)Math.Min(batchSize, rowCount - start + 1);
            var ids = ColumnBuffer<int>.Rent(count);
            var groupIds = ColumnBuffer<int>.Rent(count);
            var amounts = ColumnBuffer<long>.Rent(count);
            for (var offset = 0; offset < count; offset++)
            {
                var id = checked((int)(start + offset));
                ids.Values.Span[offset] = id;
                groupIds.Values.Span[offset] = id % 100;
                amounts.Values.Span[offset] = id % 1_000;
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
        return new NativeResult(
            selected,
            checksum,
            groups?.Groups.ToDictionary(pair => pair.Key.Value, pair => pair.Value.Sum)
                ?? new Dictionary<int, decimal>(),
            stopwatch.Elapsed,
            groups);
    }

    private static long SumRange(long first, long last)
        => first > last ? 0 : checked((first + last) * (last - first + 1) / 2);

    private static long SumModuloRange(long first, long last, int modulus)
        => checked(SumModuloTo(last, modulus) - SumModuloTo(first - 1, modulus));

    private static long SumModuloTo(long value, int modulus)
    {
        if (value <= 0) return 0;
        var fullCycles = value / modulus;
        var remainder = value % modulus;
        return checked(fullCycles * modulus * (modulus - 1L) / 2 + remainder * (remainder + 1) / 2);
    }

    private static long ReadPositiveLong(string name, long fallback)
        => long.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var value) && value > 0 ? value : fallback;

    private static int ReadPositiveInt(string name, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var value) && value > 0 ? value : fallback;

    private static double? ReadPositiveDouble(string name)
        => double.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;

    private sealed class NativeResult(
        long selectedCount,
        long projectionChecksum,
        IReadOnlyDictionary<int, decimal> groupSums,
        TimeSpan elapsed,
        NativeGroupAggregateResult<int, long>? state) : IDisposable
    {
        public long SelectedCount { get; } = selectedCount;
        public long ProjectionChecksum { get; } = projectionChecksum;
        public IReadOnlyDictionary<int, decimal> GroupSums { get; } = groupSums;
        public TimeSpan Elapsed { get; } = elapsed;
        public void Dispose() => state?.Dispose();
    }
}
