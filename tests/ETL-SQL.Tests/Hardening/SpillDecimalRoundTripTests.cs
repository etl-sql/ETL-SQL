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
}
