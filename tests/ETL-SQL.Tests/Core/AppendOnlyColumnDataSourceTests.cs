using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using Xunit;

namespace ETL_SQL.Tests.Core;

public sealed class AppendOnlyColumnDataSourceTests
{
    private static readonly ColumnDefinition[] Schema =
    {
        new("Id", "INT", false),
        new("Name", "VARCHAR(40)", false)
    };

    [Fact]
    public async Task RowWritesFreezeIntoBoundedNativeSegmentsAndRoundTrip()
    {
        await using var store = new AppendOnlyColumnDataSource(Schema, segmentRowCapacity: 10);
        var input = CreateRows(25);

        await store.WriteBatches(new[] { input }.ToAsyncEnumerable());

        Assert.Equal(25, store.EstimatedRowCount);
        Assert.Equal(2, store.SegmentCount);
        Assert.Equal(5, store.MutableHeadRows);

        var columnRows = 0;
        long idSum = 0;
        await foreach (var batch in store.ReadColumnBatches())
        {
            using (batch)
            {
                columnRows += batch.RowCount;
                foreach (var id in batch.GetColumn<int>("Id").Values.Span) idSum += id;
            }
        }

        Assert.Equal(25, columnRows);
        Assert.Equal(325, idSum);
        Assert.Equal(3, store.SegmentCount);
        Assert.Equal(0, store.MutableHeadRows);

        var fallbackRows = 0;
        await foreach (var table in store.ReadBatches()) fallbackRows += table.Rows.Count;
        Assert.Equal(25, fallbackRows);
    }

    [Fact]
    public async Task MutableHeadSnapshotsInputRows()
    {
        await using var store = new AppendOnlyColumnDataSource(Schema, segmentRowCapacity: 10);
        var input = CreateRows(1);
        await store.WriteBatches(new[] { input }.ToAsyncEnumerable());

        input.Rows[0]["Name"] = "mutated";

        await foreach (var batch in store.ReadColumnBatches())
        {
            using (batch)
                Assert.Equal("name-1", batch.GetUtf8Column("Name").GetBoxedValue(0));
        }
    }

    [Fact]
    public async Task NativeWriteTransfersBatchWithoutRowMaterialization()
    {
        await using var store = new AppendOnlyColumnDataSource(Schema, segmentRowCapacity: 10);
        var sourceRows = CreateRows(3);
        var logicalSchema = Schema.ToDictionary(column => column.ColumnName, StringComparer.OrdinalIgnoreCase);
        var nativeBatch = ColumnBatchAdapter.FromDataTable(sourceRows, logicalSchema);

        await store.WriteColumnBatches(new[] { nativeBatch }.ToAsyncEnumerable());

        Assert.Equal(1, store.SegmentCount);
        Assert.Equal(nativeBatch.AllocatedBytes, store.AllocatedSegmentBytes);
        await foreach (var retained in store.ReadColumnBatches())
        {
            using (retained)
                Assert.Same(nativeBatch, retained);
        }
    }

    [Fact]
    public async Task NativeWriteRejectsIncompatiblePhysicalSchemaWithoutTakingOwnership()
    {
        await using var store = new AppendOnlyColumnDataSource(Schema);
        var invalidSchema = new ColumnBatchSchema(new[]
        {
            new ColumnBatchField("Id", typeof(long), "BIGINT"),
            new ColumnBatchField("Name", typeof(string), "VARCHAR(40)")
        });
        var ids = new ColumnBuffer<long>(new long[] { 1 }, 1);
        var names = Utf8ColumnBuffer.FromStrings(new string?[] { "one" });
        using var invalidBatch = new ColumnBatch(invalidSchema, new IColumnBuffer[] { ids, names }, 1);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.WriteColumnBatches(new[] { invalidBatch }.ToAsyncEnumerable()));

        Assert.Equal(0, store.SegmentCount);
        Assert.Equal(1L, invalidBatch.GetColumn<long>("Id").Values.Span[0]);
    }

    [Fact]
    public async Task AllocatedCapacityIsReservedAndReleasedWithStoreLifetime()
    {
        var arbiter = new MemoryGrantArbiter(totalBudgetBytes: 1_000_000);
        var store = new AppendOnlyColumnDataSource(Schema, segmentRowCapacity: 2, memoryArbiter: arbiter);

        await store.WriteBatches(new[] { CreateRows(2) }.ToAsyncEnumerable());

        Assert.True(store.AllocatedSegmentBytes > 0);
        Assert.Equal(store.AllocatedSegmentBytes, store.MemoryUsageBytes);
        Assert.Equal(store.AllocatedSegmentBytes, arbiter.ReservedBytes);

        await store.DisposeAsync();
        Assert.Equal(0, arbiter.ReservedBytes);
    }

    [Fact]
    public async Task MemoryGrantRejectsGrowthBeforeTakingNativeBatchOwnership()
    {
        var arbiter = new MemoryGrantArbiter(totalBudgetBytes: 1);
        await using var store = new AppendOnlyColumnDataSource(Schema, memoryArbiter: arbiter);
        var logicalSchema = Schema.ToDictionary(column => column.ColumnName, StringComparer.OrdinalIgnoreCase);
        using var nativeBatch = ColumnBatchAdapter.FromDataTable(CreateRows(1), logicalSchema);

        var error = await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(() =>
            store.WriteColumnBatches(new[] { nativeBatch }.ToAsyncEnumerable()));

        Assert.Contains("Segment-native spill is not available yet", error.Message);
        Assert.Equal(0, store.SegmentCount);
        Assert.Equal(0, arbiter.ReservedBytes);
        Assert.Equal(1, nativeBatch.RowCount); // caller still owns the rejected batch
    }

    [Fact]
    public async Task RetainedReaderKeepsSegmentReservationAfterStoreDisposal()
    {
        var arbiter = new MemoryGrantArbiter(totalBudgetBytes: 1_000_000);
        var store = new AppendOnlyColumnDataSource(Schema, segmentRowCapacity: 1, memoryArbiter: arbiter);
        await store.WriteBatches(new[] { CreateRows(1) }.ToAsyncEnumerable());

        await using var reader = store.ReadColumnBatches().GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        var retained = reader.Current;
        var reservedBytes = arbiter.ReservedBytes;

        await store.DisposeAsync();

        Assert.Equal(reservedBytes, arbiter.ReservedBytes);
        Assert.Equal(1, retained.GetColumn<int>("Id").Values.Span[0]);
        retained.Dispose();
        Assert.Equal(0, arbiter.ReservedBytes);
    }

    [Fact]
    public async Task ConcurrentDisposalIsIdempotent()
    {
        var store = new AppendOnlyColumnDataSource(Schema);
        await store.WriteBatches(new[] { CreateRows(1) }.ToAsyncEnumerable());

        await Task.WhenAll(store.DisposeAsync().AsTask(), store.DisposeAsync().AsTask());

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            store.WriteBatches(new[] { CreateRows(1) }.ToAsyncEnumerable()));
    }

    [Fact]
    public async Task PrimaryKeyRejectsDuplicateAcrossRowAndNativeWrites()
    {
        var schema = ConstrainedSchema(primaryKey: true);
        await using var store = new AppendOnlyColumnDataSource(schema, segmentRowCapacity: 10);
        await store.WriteBatches(new[] { CreateRows(1) }.ToAsyncEnumerable());

        var logicalSchema = schema.ToDictionary(column => column.ColumnName, StringComparer.OrdinalIgnoreCase);
        using var duplicate = ColumnBatchAdapter.FromDataTable(CreateRows(1), logicalSchema);
        var error = await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(() =>
            store.WriteColumnBatches(new[] { duplicate }.ToAsyncEnumerable(), append: true));

        Assert.Contains("unique constraint", error.Message);
        Assert.Equal(1, store.EstimatedRowCount);
        Assert.Equal(1, duplicate.RowCount); // rejected native ownership stays with the caller
    }

    [Fact]
    public async Task NativeBatchRejectsInternalDuplicateAndRollsBackStagedKeys()
    {
        var schema = ConstrainedSchema(primaryKey: true);
        await using var store = new AppendOnlyColumnDataSource(schema);
        var logicalSchema = schema.ToDictionary(column => column.ColumnName, StringComparer.OrdinalIgnoreCase);
        var rows = CreateRows(2);
        rows.Rows[1]["Id"] = rows.Rows[0]["Id"];
        using var duplicate = ColumnBatchAdapter.FromDataTable(rows, logicalSchema);

        await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(() =>
            store.WriteColumnBatches(new[] { duplicate }.ToAsyncEnumerable()));

        var valid = ColumnBatchAdapter.FromDataTable(CreateRows(1), logicalSchema);
        await store.WriteColumnBatches(new[] { valid }.ToAsyncEnumerable(), append: true);
        Assert.Equal(1, store.EstimatedRowCount);
    }

    [Fact]
    public async Task PrimaryKeyRejectsNullWhileUniqueAllowsMultipleNulls()
    {
        var nullRows = CreateRows(2);
        nullRows.Rows[0]["Id"] = null;
        nullRows.Rows[1]["Id"] = DBNull.Value;

        await using var primaryStore = new AppendOnlyColumnDataSource(ConstrainedSchema(primaryKey: true));
        var primaryError = await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(() =>
            primaryStore.WriteBatches(new[] { nullRows }.ToAsyncEnumerable()));
        Assert.Contains("does not allow NULL", primaryError.Message);

        await using var uniqueStore = new AppendOnlyColumnDataSource(ConstrainedSchema(primaryKey: false));
        await uniqueStore.WriteBatches(new[] { nullRows }.ToAsyncEnumerable());
        Assert.Equal(2, uniqueStore.EstimatedRowCount);
    }

    [Fact]
    public async Task ConstraintKeysAreIncludedInMemoryGrantAndReleased()
    {
        var arbiter = new MemoryGrantArbiter(totalBudgetBytes: 1_000_000);
        var store = new AppendOnlyColumnDataSource(
            ConstrainedSchema(primaryKey: true), segmentRowCapacity: 10, memoryArbiter: arbiter);

        await store.WriteBatches(new[] { CreateRows(1) }.ToAsyncEnumerable());

        Assert.True(store.MemoryUsageBytes > 0);
        Assert.Equal(store.MemoryUsageBytes, arbiter.ReservedBytes);
        await store.DisposeAsync();
        Assert.Equal(0, arbiter.ReservedBytes);
    }

    [Fact]
    public async Task CompositeUniqueEnforcesNormalizedPairsAcrossRowAndNativeWrites()
    {
        var constraint = new TableUniqueConstraint(new List<string> { "Id", "Name" }) { ConstraintName = "UQ_Id_Name" };
        await using var store = new AppendOnlyColumnDataSource(Schema, tableConstraints: new[] { constraint });
        await store.WriteBatches(new[] { CreateRows(2) }.ToAsyncEnumerable());

        var distinctPair = CreateRows(1);
        distinctPair.Rows[0]["Name"] = "other";
        await store.WriteBatches(new[] { distinctPair }.ToAsyncEnumerable(), append: true);

        var logicalSchema = Schema.ToDictionary(column => column.ColumnName, StringComparer.OrdinalIgnoreCase);
        using var duplicate = ColumnBatchAdapter.FromDataTable(CreateRows(1), logicalSchema);
        var error = await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(() =>
            store.WriteColumnBatches(new[] { duplicate }.ToAsyncEnumerable(), append: true));

        Assert.Contains("UQ_Id_Name", error.Message);
        Assert.Equal(3, store.EstimatedRowCount);
    }

    [Fact]
    public async Task CompositePrimaryKeyRejectsNullAndUniqueSkipsNullPairs()
    {
        var rows = CreateRows(2);
        rows.Rows[0]["Name"] = null;
        rows.Rows[1]["Name"] = null;
        rows.Rows[1]["Id"] = rows.Rows[0]["Id"];

        var primary = new TablePrimaryKeyConstraint(new List<string> { "Id", "Name" });
        await using var primaryStore = new AppendOnlyColumnDataSource(Schema, tableConstraints: new[] { primary });
        await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(() =>
            primaryStore.WriteBatches(new[] { rows }.ToAsyncEnumerable()));

        var unique = new TableUniqueConstraint(new List<string> { "Id", "Name" });
        await using var uniqueStore = new AppendOnlyColumnDataSource(Schema, tableConstraints: new[] { unique });
        await uniqueStore.WriteBatches(new[] { rows }.ToAsyncEnumerable());
        Assert.Equal(2, uniqueStore.EstimatedRowCount);
    }

    [Fact]
    public void CompositeConstraintRejectsUnknownColumnsAtConstruction()
    {
        var constraint = new TableUniqueConstraint(new List<string> { "Missing", "Name" });
        var error = Assert.Throws<ArgumentException>(() =>
            new AppendOnlyColumnDataSource(Schema, tableConstraints: new[] { constraint }));
        Assert.Contains("Missing", error.Message);
    }

    [Fact]
    public async Task TruncateReleasesSegmentsKeysAndMemoryButPreservesSchema()
    {
        var arbiter = new MemoryGrantArbiter(1_000_000);
        await using var store = new AppendOnlyColumnDataSource(
            ConstrainedSchema(primaryKey: true), segmentRowCapacity: 1, memoryArbiter: arbiter);
        await store.WriteBatches(new[] { CreateRows(1) }.ToAsyncEnumerable());
        Assert.True(arbiter.ReservedBytes > 0);

        await store.TruncateAsync();

        Assert.Equal(0, store.EstimatedRowCount);
        Assert.Equal(0, store.SegmentCount);
        Assert.Equal(0, arbiter.ReservedBytes);
        await store.WriteBatches(new[] { CreateRows(1) }.ToAsyncEnumerable(), append: true);
        Assert.Equal(1, store.EstimatedRowCount); // key cache was cleared with data
    }

    [Fact]
    public async Task DeclaredNotNullIsEnforcedForRowAndNativeWrites()
    {
        var schema = new[]
        {
            new ColumnDefinition("Id", "INT", false),
            new ColumnDefinition("Name", "VARCHAR(40)", false) { IsNullable = false }
        };
        await using var store = new AppendOnlyColumnDataSource(schema);
        var rows = CreateRows(1);
        rows.Rows[0]["Name"] = null;
        await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(() =>
            store.WriteBatches(new[] { rows }.ToAsyncEnumerable()));

        var nativeSchema = new ColumnBatchSchema(new[]
        {
            new ColumnBatchField("Id", typeof(int), "INT"),
            new ColumnBatchField("Name", typeof(string), "VARCHAR(40)")
        });
        using var native = new ColumnBatch(nativeSchema, new IColumnBuffer[]
        {
            new ColumnBuffer<int>(new[] { 1 }, 1),
            Utf8ColumnBuffer.FromStrings(new string?[] { null })
        }, 1);
        await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(() =>
            store.WriteColumnBatches(new[] { native }.ToAsyncEnumerable()));
        Assert.Equal(0, store.EstimatedRowCount);
    }

    [Fact]
    public async Task RowWritesNormalizeDeclaredTypesBeforeStorage()
    {
        await using var store = new AppendOnlyColumnDataSource(Schema);
        var rows = CreateRows(1);
        rows.Rows[0]["Id"] = "42";

        await store.WriteBatches(new[] { rows }.ToAsyncEnumerable());

        await foreach (var batch in store.ReadColumnBatches())
        {
            using (batch) Assert.Equal(42, batch.GetColumn<int>("Id").Values.Span[0]);
        }
    }

    [Fact]
    public async Task TransactionRollbackRestoresSegmentsCountsAndConstraintKeys()
    {
        await using var store = new AppendOnlyColumnDataSource(
            ConstrainedSchema(primaryKey: true), segmentRowCapacity: 10);
        await store.WriteBatches(new[] { CreateRows(1) }.ToAsyncEnumerable());
        await store.BeginTransactionAsync();
        var appended = CreateRows(1);
        appended.Rows[0]["Id"] = 2;
        await store.WriteBatches(new[] { appended }.ToAsyncEnumerable(), append: true);

        await store.RollbackAsync();

        Assert.Equal(1, store.EstimatedRowCount);
        var restoredIds = new List<int>();
        await foreach (var batch in store.ReadColumnBatches())
        {
            using (batch) restoredIds.AddRange(batch.GetColumn<int>("Id").Values.ToArray());
        }
        Assert.Equal(new[] { 1 }, restoredIds);

        var second = CreateRows(1);
        second.Rows[0]["Id"] = 2;
        await store.WriteBatches(new[] { second }.ToAsyncEnumerable(), append: true);
        Assert.Equal(2, store.EstimatedRowCount);
    }

    [Fact]
    public async Task TransactionCommitKeepsAppendedRowsAndNestedSnapshotsRemainIndependent()
    {
        await using var store = new AppendOnlyColumnDataSource(Schema);
        await store.WriteBatches(new[] { CreateRows(1) }.ToAsyncEnumerable());
        await store.BeginTransactionAsync();
        await store.WriteBatches(new[] { CreateRows(1) }.ToAsyncEnumerable(), append: true);
        await store.BeginTransactionAsync();
        await store.WriteBatches(new[] { CreateRows(1) }.ToAsyncEnumerable(), append: true);

        await store.RollbackAsync();
        Assert.Equal(2, store.EstimatedRowCount);
        await store.CommitAsync();
        Assert.Equal(2, store.EstimatedRowCount);
    }

    private static ColumnDefinition[] ConstrainedSchema(bool primaryKey)
    {
        var id = new ColumnDefinition("Id", "INT", false)
        {
            IsPrimaryKey = primaryKey,
            IsUnique = !primaryKey,
            IsNullable = !primaryKey
        };
        return new[] { id, new ColumnDefinition("Name", "VARCHAR(40)", false) };
    }

    private static DataTable CreateRows(int count)
    {
        var table = new DataTable();
        table.SetColumns(new[] { "Id", "Name" });
        for (var i = 1; i <= count; i++)
        {
            var row = table.NewRow();
            row["Id"] = (decimal)i;
            row["Name"] = $"name-{i}";
            table.Rows.Add(row);
        }
        return table;
    }
}
