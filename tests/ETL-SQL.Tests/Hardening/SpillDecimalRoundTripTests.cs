using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Spill;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Spill;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Hardening;

[Trait("CompatBreak", "0.21.0")]
public sealed class SpillDecimalRoundTripTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DecimalValuesAndScaleSurviveRowAndColumnarSpill(bool writeColumns, bool readColumns)
    {
        var provider = DependencyInjectionSetup.BuildServiceProvider();
        await using var lifetime = provider as IAsyncDisposable;
        provider.GetRequiredService<ILogger>().SuppressConsole = true;
        var evaluator = provider.GetRequiredService<Evaluator>();
        using var store = new SpillStore(evaluator);
        decimal?[] expected = [1m, 1.2300m, null, 0.1234567890123456789012345678m, decimal.MaxValue, decimal.MinValue];
        await using (var writer = await store.CreateWriterAsync("decimal_round_trip"))
        {
            if (writeColumns)
            {
                var values = ColumnBuffer<decimal>.Rent(expected.Length);
                for (var i = 0; i < expected.Length; i++)
                    if (expected[i] is { } value) values.Values.Span[i] = value;
                    else values.SetNull(i);
                using var batch = new ColumnBatch(
                    new ColumnBatchSchema([new ColumnBatchField("Amount", typeof(decimal), "Decimal")]),
                    [values], expected.Length);
                await Assert.IsAssignableFrom<IColumnarSpillWriter>(writer).WriteBatchAsync(batch);
            }
            else
            {
                foreach (var value in expected)
                    await writer.WriteRowAsync(new Row { ["Amount"] = value });
            }
        }

        var actual = new List<decimal?>();
        await using var reader = await store.CreateReaderAsync("decimal_round_trip");
        if (readColumns)
        {
            await foreach (var batch in Assert.IsAssignableFrom<IColumnarSpillReader>(reader).AsColumnBatchesAsync())
            {
                using (batch)
                {
                    var values = batch.GetColumn<decimal>("Amount");
                    for (var i = 0; i < batch.RowCount; i++)
                        actual.Add(values.IsNull(i) ? null : values.Values.Span[i]);
                }
            }
        }
        else
        {
            await foreach (var row in reader.AsEnumerableAsync())
                actual.Add(row["Amount"] is null ? null : Assert.IsType<decimal>(row["Amount"]));
        }

        Assert.Equal(expected, actual);
        for (var i = 0; i < expected.Length; i++)
            if (expected[i] is { } value)
                Assert.Equal(decimal.GetBits(value), decimal.GetBits(actual[i]!.Value));
    }

    [Fact]
    public async Task DecimalArrowSpill_UsesFixedSizeBinary16AndZeroCopyColumnBatchHandoff()
    {
        var provider = DependencyInjectionSetup.BuildServiceProvider();
        await using var lifetime = provider as IAsyncDisposable;
        provider.GetRequiredService<ILogger>().SuppressConsole = true;
        var evaluator = provider.GetRequiredService<Evaluator>();
        evaluator.SpillEncryptionEnabled = false;
        evaluator.SpillCompressionEnabled = false;

        using var store = new SpillStore(evaluator);
        var ids = ColumnBuffer<long>.Rent(4);
        var amounts = ColumnBuffer<decimal>.Rent(4);
        var metrics = ColumnBuffer<double>.Rent(4);
        ids.Values.Span[0] = 10; ids.Values.Span[1] = 20; ids.Values.Span[2] = 30; ids.SetNull(3);
        amounts.Values.Span[0] = 1.2300m; amounts.SetNull(1); amounts.Values.Span[2] = decimal.MaxValue; amounts.Values.Span[3] = 0.00000001m;
        metrics.Values.Span[0] = 1.5; metrics.Values.Span[1] = 2.5; metrics.SetNull(2); metrics.Values.Span[3] = 4.5;
        var labels = Utf8ColumnBuffer.FromStrings(["alpha", null, "gamma", "delta"]);

        using (var batch = new ColumnBatch(
            new ColumnBatchSchema([
                new ColumnBatchField("Id", typeof(long), "Integer"),
                new ColumnBatchField("Amount", typeof(decimal), "Decimal"),
                new ColumnBatchField("Metric", typeof(double), "Double"),
                new ColumnBatchField("Label", typeof(string), "String")
            ]),
            [ids, amounts, metrics, labels], 4))
        {
            await using var writer = await store.CreateWriterAsync("binary_decimal_schema_check");
            await ((IColumnarSpillWriter)writer).WriteBatchAsync(batch);
        }

        // Verify raw Arrow IPC schema stores Amount as FixedSizeBinary(16), not StringType.
        var rootPath = typeof(SpillStore)
            .GetField("_cachedRootPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(store) as string;
        await using (var fs = File.OpenRead(Path.Combine(rootPath!, "binary_decimal_schema_check")))
        using (var rawArrow = new Apache.Arrow.Ipc.ArrowStreamReader(fs))
        {
            var rawBatch = await rawArrow.ReadNextRecordBatchAsync();
            Assert.NotNull(rawBatch);
            using (rawBatch)
            {
                var amountField = rawBatch!.Schema.GetFieldByName("Amount");
                var binType = Assert.IsType<Apache.Arrow.Types.FixedSizeBinaryType>(amountField.DataType);
                Assert.Equal(16, binType.ByteWidth);
            }
        }

        // Verify ColumnBatch zero-copy handoff + Retain() keeps RecordBatch alive until final Dispose().
        await using var reader = await store.CreateReaderAsync("binary_decimal_schema_check");
        ColumnBatch? retained = null;
        await foreach (var readBatch in ((IColumnarSpillReader)reader).AsColumnBatchesAsync())
        {
            retained = readBatch.Retain();
            readBatch.Dispose();
        }

        Assert.NotNull(retained);
        using (retained!)
        {
            var readIds = retained.GetColumn<long>("Id");
            Assert.Equal(10L, readIds.Values.Span[0]);
            Assert.True(readIds.IsNull(3));

            var readAmounts = retained.GetColumn<decimal>("Amount");
            Assert.Equal(1.2300m, readAmounts.Values.Span[0]);
            Assert.Equal(decimal.GetBits(1.2300m), decimal.GetBits(readAmounts.Values.Span[0]));
            Assert.True(readAmounts.IsNull(1));
            Assert.Equal(decimal.MaxValue, readAmounts.Values.Span[2]);

            var readMetrics = retained.GetColumn<double>("Metric");
            Assert.Equal(1.5, readMetrics.Values.Span[0]);
            Assert.True(readMetrics.IsNull(2));

            var readLabels = retained.GetUtf8Column("Label");
            Assert.Equal("alpha", readLabels.GetBoxedValue(0));
            Assert.True(readLabels.IsNull(1));
            Assert.Equal("delta", readLabels.GetBoxedValue(3));
        }
    }
}
