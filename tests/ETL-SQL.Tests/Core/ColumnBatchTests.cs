using System;
using System.Collections.Generic;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using Xunit;

namespace ETL_SQL.Tests.Core;

public sealed class ColumnBatchTests
{
    [Fact]
    public void TypedBuffers_ExposeValuesAndBitPackedNulls()
    {
        using var ids = ColumnBuffer<int>.Rent(10);
        for (var i = 0; i < ids.Count; i++) ids.Values.Span[i] = i + 1;
        ids.SetNull(3);
        ids.SetNull(9);

        Assert.Equal(2, ids.NullBitmap.Length);
        Assert.Equal(3, ids.GetBoxedValue(2));
        Assert.Null(ids.GetBoxedValue(3));
        Assert.True(ids.IsNull(9));
        Assert.True(ids.AllocatedBytes >= 10 * sizeof(int) + 2);
    }

    [Fact]
    public void Batch_ValidatesSchemaAndSupportsTypedLookup()
    {
        var schema = new ColumnBatchSchema(new[]
        {
            new ColumnBatchField("Id", typeof(long), "BIGINT", false),
            new ColumnBatchField("Amount", typeof(decimal), "DECIMAL(18,2)")
        });
        var ids = new ColumnBuffer<long>(new long[] { 10, 20, 30 }, 3);
        var amounts = new ColumnBuffer<decimal>(new decimal[] { 1.25m, 2.50m, 3.75m }, 3);

        using var batch = new ColumnBatch(schema, new IColumnBuffer[] { ids, amounts }, 3);

        Assert.Equal(3, batch.RowCount);
        Assert.Equal(20, batch.GetColumn<long>("id").Values.Span[1]);
        Assert.Equal(2.50m, batch.GetColumn<decimal>("AMOUNT").Values.Span[1]);
        Assert.True(batch.AllocatedBytes > 0);
    }

    [Fact]
    public void Utf8Buffer_UsesOffsetsAndPreservesNulls()
    {
        using var names = Utf8ColumnBuffer.FromStrings(new string?[] { "alpha", null, "βeta", "" });

        Assert.Equal(4, names.Count);
        Assert.Equal(new[] { 0, 5, 5, 10, 10 }, names.Offsets.ToArray());
        Assert.Equal("alpha", names.GetBoxedValue(0));
        Assert.Null(names.GetBoxedValue(1));
        Assert.Equal("βeta", names.GetBoxedValue(2));
        Assert.Equal(string.Empty, names.GetBoxedValue(3));
        Assert.Equal(10, names.Utf8Data.Length);
    }

    [Fact]
    public void Utf8Adapter_PreservesDbNullAsNull()
    {
        var table = new DataTable();
        table.SetColumns(new[] { "Name" });
        var row = table.NewRow();
        row["Name"] = DBNull.Value;
        table.Rows.Add(row);
        var schema = new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = new("Name", "VARCHAR(20)", false)
        };

        using var batch = ColumnBatchAdapter.FromDataTable(table, schema);

        Assert.True(batch.GetUtf8Column("Name").IsNull(0));
        Assert.Null(ColumnBatchAdapter.ToDataTable(batch).Rows[0]["Name"]);
    }

    [Fact]
    public void SchemaAndColumnCollectionsCannotBeMutatedByArrayCast()
    {
        var schema = new ColumnBatchSchema(new[] { new ColumnBatchField("Id", typeof(int), "INT") });
        using var batch = new ColumnBatch(schema, new IColumnBuffer[]
        {
            new ColumnBuffer<int>(new[] { 1 }, 1)
        }, 1);

        Assert.False(schema.Fields is ColumnBatchField[]);
        Assert.False(batch.Columns is IColumnBuffer[]);
    }

    [Fact]
    public void Batch_AcceptsUtf8StringPhysicalStorage()
    {
        var schema = new ColumnBatchSchema(new[]
        {
            new ColumnBatchField("Name", typeof(string), "VARCHAR")
        });
        var names = Utf8ColumnBuffer.FromStrings(new string?[] { "one", "two" });

        using var batch = new ColumnBatch(schema, new IColumnBuffer[] { names }, 2);
        Assert.Equal("two", batch.GetUtf8Column("name").GetBoxedValue(1));
    }

    [Fact]
    public void Batch_RejectsMismatchedPhysicalTypeAndLength()
    {
        var schema = new ColumnBatchSchema(new[]
        {
            new ColumnBatchField("Id", typeof(long), "BIGINT")
        });

        using var wrongType = new ColumnBuffer<int>(new[] { 1, 2 }, 2);
        Assert.Throws<ArgumentException>(() =>
            new ColumnBatch(schema, new IColumnBuffer[] { wrongType }, 2));

        using var wrongLength = new ColumnBuffer<long>(new long[] { 1 }, 1);
        Assert.Throws<ArgumentException>(() =>
            new ColumnBatch(schema, new IColumnBuffer[] { wrongLength }, 2));
    }

    [Fact]
    public void DisposedPooledBuffersRejectAccess()
    {
        var values = ColumnBuffer<double>.Rent(4);
        values.Dispose();

        Assert.Throws<ObjectDisposedException>(() => values.IsNull(0));
        Assert.Throws<ObjectDisposedException>(() => values.Values);
    }

    [Fact]
    public void RowBoundaryAdapter_UsesLogicalWidthsAndRoundTripsEngineValues()
    {
        var table = new DataTable();
        table.SetColumns(new[] { "Tiny", "Id", "Amount", "Active", "Name" });
        AddRow(table, 7m, 42m, 12.50m, true, "alpha");
        AddRow(table, null, -5m, 0m, false, null);
        var schema = new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["Tiny"] = new("Tiny", "TINYINT", false),
            ["Id"] = new("Id", "INT", false),
            ["Amount"] = new("Amount", "DECIMAL(18,2)", false),
            ["Active"] = new("Active", "BIT", false),
            ["Name"] = new("Name", "VARCHAR(20)", false)
        };

        using var batch = ColumnBatchAdapter.FromDataTable(table, schema);

        Assert.IsType<ColumnBuffer<byte>>(batch.GetColumn("Tiny"));
        Assert.IsType<ColumnBuffer<int>>(batch.GetColumn("Id"));
        Assert.IsType<ColumnBuffer<decimal>>(batch.GetColumn("Amount"));
        Assert.IsType<ColumnBuffer<byte>>(batch.GetColumn("Active"));
        Assert.IsType<Utf8ColumnBuffer>(batch.GetColumn("Name"));
        Assert.True(batch.GetColumn<byte>("Tiny").IsNull(1));

        var roundTrip = ColumnBatchAdapter.ToDataTable(batch);
        Assert.Equal(42m, roundTrip.Rows[0]["Id"]);
        Assert.Equal(-5m, roundTrip.Rows[1]["Id"]);
        Assert.Equal(true, roundTrip.Rows[0]["Active"]);
        Assert.Null(roundTrip.Rows[1]["Name"]);

        static void AddRow(DataTable target, params object?[] values)
        {
            var row = target.NewRow();
            for (var i = 0; i < values.Length; i++) row[i] = values[i];
            target.Rows.Add(row);
        }
    }
}
