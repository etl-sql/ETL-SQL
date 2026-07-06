using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ETL_SQL.Tests.Scale;

public sealed class GateFCertificationFactAttribute : FactAttribute
{
    public GateFCertificationFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("GATE_F_CERTIFICATION"), "1",
                StringComparison.Ordinal))
            Skip = "Gate F is an operator-run certification test. Use scripts/Test-GateF.ps1.";
    }
}

public sealed class BillionRowCertificationTests(ITestOutputHelper output)
{
    [GateFCertificationFact]
    [Trait("Category", "BillionRowCertification")]
    public async Task NativeScanFilterProjectionAndLowCardinalityAggregateStayBounded()
    {
        var rowCount = ReadPositiveLong("GATE_F_ROWS", 0);
        if (rowCount <= 0)
            throw new InvalidOperationException(
                "GATE_F_ROWS must be explicitly set for Gate F certification.");
        var batchSize = ReadPositiveInt("GATE_F_BATCH_ROWS", 100_000);
        var memoryBoundMb = ReadPositiveDouble("GATE_F_MEMORY_BOUND_MB") ?? 16_384;
        var minimumRowsPerSecond = ReadPositiveDouble("GATE_F_MIN_ROWS_PER_SECOND");

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        using var sampler = new ScenarioResourceSampler();
        await using var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await using var source = new StreamingColumnarSource(rowCount, batchSize);
        evaluator.Connections["gate_f_source"] = source;

        var threshold = checked((int)(rowCount / 2));
        var stopwatch = Stopwatch.StartNew();
        var tables = await evaluator.ExecuteQuery(TestHelpers.Parse($"""
            SELECT GroupId, COUNT(*) AS N, SUM(Id) AS IdSum, SUM(Amount) AS AmountSum
            FROM gate_f_source
            WHERE Id > {threshold}
            GROUP BY GroupId;
            """).Statements[0]).ToListAsync();
        stopwatch.Stop();
        var resources = sampler.SnapshotAndReset();

        var rows = tables.SelectMany(table => table.Rows).ToArray();
        var selectedCount = rows.Sum(row => Convert.ToInt64(row["N"], CultureInfo.InvariantCulture));
        var idSum = rows.Sum(row => Convert.ToDecimal(row["IdSum"], CultureInfo.InvariantCulture));
        var amountSum = rows.Sum(row => Convert.ToDecimal(row["AmountSum"], CultureInfo.InvariantCulture));
        var projectionChecksum = checked((long)(idSum + amountSum * 2));
        var expectedSelected = rowCount - threshold;
        var expectedIdSum = SumRange(threshold + 1, rowCount);
        var expectedAmountSum = SumModuloRange(threshold + 1, rowCount, 1_000);
        var expectedChecksum = checked(expectedIdSum + expectedAmountSum * 2);
        var rowsPerSecond = rowCount / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
        var peakWorkingSetMb = resources.PeakWorkingSetBytes / 1024d / 1024d;

        Assert.Equal(0, source.RowReadAttempts);
        Assert.Equal(expectedSelected, selectedCount);
        Assert.Equal(expectedChecksum, projectionChecksum);
        Assert.Equal(100, rows.Length);
        Assert.Equal((decimal)expectedAmountSum, amountSum);
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
            selectedRows = selectedCount,
            checksum = projectionChecksum,
            groups = rows.Length,
            rowReadAttempts = source.RowReadAttempts,
            elapsedMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
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

    private sealed class StreamingColumnarSource(long rowCount, int preferredBatchSize)
        : IDataSource, IColumnarDataSource, IEstimatedCardinalityDataSource
    {
        private static readonly ColumnBatchSchema Schema = new(new[]
        {
            new ColumnBatchField("Id", typeof(int), "INT", false),
            new ColumnBatchField("GroupId", typeof(int), "INT", false),
            new ColumnBatchField("Amount", typeof(long), "BIGINT", false)
        });

        public int RowReadAttempts { get; private set; }
        public long EstimatedRowCount => rowCount;
        public string Path => string.Empty;
        public Dictionary<string, string>? Options => null;
        public string ConnectorType => "GATE_F_COLUMNAR";

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10_000)
        {
            RowReadAttempts++;
            await Task.Yield();
            throw new InvalidOperationException("Gate F must remain on the native columnar evaluator route.");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }

        public async IAsyncEnumerable<ColumnBatch> ReadColumnBatches(
            int batchSize = 10_000,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var size = preferredBatchSize > 0 ? preferredBatchSize : batchSize;
            for (long start = 1; start <= rowCount; start += size)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = (int)Math.Min(size, rowCount - start + 1);
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
                yield return new ColumnBatch(Schema, new IColumnBuffer[] { ids, groupIds, amounts }, count);
            }
        }

        public Task<IEnumerable<string>> GetColumnsAsync()
            => Task.FromResult<IEnumerable<string>>(Schema.Fields.Select(field => field.Name).ToArray());
        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false)
            => throw new NotSupportedException();
        public object? Snapshot() => throw new NotSupportedException();
        public void Restore(object? snapshot) => throw new NotSupportedException();
        public IDataSource WithTable(string tableName) => this;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
