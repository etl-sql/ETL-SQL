using System;
using System.Collections.Generic;
using System.Linq;
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
        const int rowCount = 100_000;
        var schema = new[]
        {
            new ColumnDefinition("Id", "INT", false) { IsNullable = false },
            new ColumnDefinition("GroupId", "SMALLINT", false) { IsNullable = false },
            new ColumnDefinition("Amount", "DECIMAL(18,2)", false) { IsNullable = false },
            new ColumnDefinition("Active", "BIT", false) { IsNullable = false },
            new ColumnDefinition("Label", "VARCHAR(32)", false)
        };
        var rows = new DataTable();
        rows.SetColumns(schema.Select(column => column.ColumnName));
        long rowHeapBytes = 0;
        long expectedIdSum = 0;
        for (var id = 1; id <= rowCount; id++)
        {
            var row = rows.NewRow();
            row["Id"] = id;
            row["GroupId"] = (short)(id % 100);
            row["Amount"] = id / 100m;
            row["Active"] = (id & 1) == 0;
            row["Label"] = id % 20 == 0 ? null : $"group-{id % 100}";
            rows.Rows.Add(row);
            rowHeapBytes += row.EstimateHeapBytes();
            expectedIdSum += id;
        }

        await using var store = new AppendOnlyColumnDataSource(schema, segmentRowCapacity: 10_000);
        await store.WriteBatches(new[] { rows }.ToAsyncEnumerable());

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

        var ratio = (double)store.MemoryUsageBytes / rowHeapBytes;
        output.WriteLine(
            $"COLUMNAR_STORAGE_METRIC rows={rowCount} rowHeapBytes={rowHeapBytes} " +
            $"nativeAllocatedBytes={store.MemoryUsageBytes} ratio={ratio:F4} segments={store.SegmentCount}");
        Assert.Equal(rowCount, scannedRows);
        Assert.Equal(expectedIdSum, scannedIdSum);
        Assert.True(ratio < 0.50,
            $"Expected native allocated capacity below 50% of estimated row heap; observed {ratio:P2}.");
    }
}
