using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using Xunit;
using Xunit.Abstractions;

namespace ETL_SQL.Tests.Scale;

public sealed class ColumnarStorageAssessmentTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "ScaleAssessment")]
    public async Task NativeStoreUsesMateriallyLessResidentCapacityAndScansTypedBuffers()
    {
        var rowCount = ReadPositiveInt("COLUMNAR_STORAGE_GATE_ROWS", 100_000);
        var segmentRows = Math.Min(rowCount, ReadPositiveInt("COLUMNAR_STORAGE_GATE_SEGMENT_ROWS", 100_000));
        var maximumRatio = ReadPositiveDouble("COLUMNAR_STORAGE_GATE_MAX_RATIO") ?? 0.50;
        var schema = new[]
        {
            new ColumnDefinition("Id", "INT", false) { IsNullable = false },
            new ColumnDefinition("GroupId", "SMALLINT", false) { IsNullable = false },
            new ColumnDefinition("Amount", "DECIMAL(18,2)", false) { IsNullable = false },
            new ColumnDefinition("Active", "BIT", false) { IsNullable = false },
            new ColumnDefinition("Label", "VARCHAR(32)", false)
        };
        var counter = new HeapCounter();
        var stopwatch = Stopwatch.StartNew();
        await using var store = new AppendOnlyColumnDataSource(schema, segmentRowCapacity: segmentRows);
        await store.WriteBatches(CreateBatches(rowCount, segmentRows, schema, counter));

        long scannedRows = 0;
        long scannedIdSum = 0;
        await foreach (var batch in store.ReadColumnBatches())
        {
            using (batch)
            {
                scannedRows += batch.RowCount;
                foreach (var id in batch.GetColumn<int>("Id").Values.Span) scannedIdSum += id;
            }
        }
        stopwatch.Stop();

        var expectedIdSum = (long)rowCount * (rowCount + 1) / 2;
        var ratio = (double)store.MemoryUsageBytes / counter.RowHeapBytes;
        var metric = new
        {
            rowCount,
            segmentRows,
            segments = store.SegmentCount,
            rowHeapBytes = counter.RowHeapBytes,
            nativeAllocatedBytes = store.MemoryUsageBytes,
            ratio = Math.Round(ratio, 6),
            elapsedMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
            rowsPerSecond = Math.Round(rowCount / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds), 2),
            scannedRows,
            scannedIdSum,
            maximumRatio
        };
        var json = JsonSerializer.Serialize(metric);
        output.WriteLine("COLUMNAR_STORAGE_GATE " + json);
        var outputPath = Environment.GetEnvironmentVariable("COLUMNAR_STORAGE_GATE_OUTPUT");
        if (!string.IsNullOrWhiteSpace(outputPath)) File.WriteAllText(outputPath, json);

        Assert.Equal(rowCount, scannedRows);
        Assert.Equal(expectedIdSum, scannedIdSum);
        Assert.True(ratio < maximumRatio,
            $"Expected native allocated capacity below {maximumRatio:P2} of estimated row heap; observed {ratio:P2}.");
    }

    private static async IAsyncEnumerable<DataTable> CreateBatches(
        int rowCount,
        int batchRows,
        IReadOnlyList<ColumnDefinition> schema,
        HeapCounter counter)
    {
        for (var start = 1; start <= rowCount; start += batchRows)
        {
            var count = Math.Min(batchRows, rowCount - start + 1);
            var rows = new DataTable();
            rows.SetColumns(schema.Select(column => column.ColumnName));
            for (var offset = 0; offset < count; offset++)
            {
                var id = start + offset;
                var row = rows.NewRow();
                row["Id"] = id;
                row["GroupId"] = (short)(id % 100);
                row["Amount"] = id / 100m;
                row["Active"] = (id & 1) == 0;
                row["Label"] = id % 20 == 0 ? null : $"group-{id % 100}";
                rows.Rows.Add(row);
                counter.RowHeapBytes += row.EstimateHeapBytes();
            }
            yield return rows;
            await Task.Yield();
        }
    }

    private static int ReadPositiveInt(string name, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var value) && value > 0 ? value : fallback;

    private static double? ReadPositiveDouble(string name)
        => double.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;

    private sealed class HeapCounter { public long RowHeapBytes; }
}
