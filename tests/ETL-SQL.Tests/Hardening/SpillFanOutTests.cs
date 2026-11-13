using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core.Spill;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Spill;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Hardening;

/// <summary>
/// An operator that partitions its input to disk keeps one spill writer open per partition, and
/// each writer buffers rows until it flushes. With a full batch per writer, buffered rows grew with
/// the partition count, which the planner raises with the input size, so the external join and
/// window used memory in proportion to their input (Scale Measurement 43509938978: 16 partitions at
/// 10M rows, 128 at 100M, about 5 MB more heap per partition).
/// </summary>
public sealed class SpillFanOutTests
{
    [Theory]
    [InlineData(10_000, 1, 10_000)]
    [InlineData(10_000, 16, 10_000)]
    [InlineData(10_000, 128, 1_250)]
    [InlineData(10_000, 1_024, 256)]
    [InlineData(100, 1_024, 100)]
    [InlineData(0, 8, 1)]
    public void FlushRowsShareOneBudgetAcrossPartitions(int batchRows, int partitions, int expected)
    {
        Assert.Equal(expected, SpillFanOut.FlushRows(batchRows, partitions));
    }

    [Fact]
    public void BufferedRowsStayBoundedAsPartitionsGrow()
    {
        const int batchRows = 10_000;
        var bound = (long)batchRows * SpillFanOut.BufferedBatches;
        for (var partitions = 1; partitions <= 4_096; partitions *= 2)
        {
            var buffered = (long)partitions * SpillFanOut.FlushRows(batchRows, partitions);
            Assert.True(buffered <= Math.Max(bound, (long)partitions * SpillFanOut.MinimumFlushRows),
                $"{partitions} partitions buffer {buffered} rows.");
        }
    }

    [Fact]
    public async Task WriterFlushesAtTheRequestedRowCount()
    {
        var provider = DependencyInjectionSetup.BuildServiceProvider();
        await using var lifetime = provider as IAsyncDisposable;
        provider.GetRequiredService<ILogger>().SuppressConsole = true;
        var evaluator = provider.GetRequiredService<Evaluator>();
        using var store = new SpillStore(evaluator);

        await using (var writer = await store.CreateWriterAsync("fan_out_flush", flushBatchRows: 3))
        {
            for (var i = 0; i < 7; i++)
                await writer.WriteRowAsync(new Row { ["Id"] = (long)i });
        }

        var batchSizes = new List<int>();
        var ids = new List<long>();
        await using var reader = await store.CreateReaderAsync("fan_out_flush");
        await foreach (var batch in Assert.IsAssignableFrom<IColumnarSpillReader>(reader).AsColumnBatchesAsync())
        {
            using (batch)
            {
                batchSizes.Add(batch.RowCount);
                var values = batch.GetColumn<long>("Id");
                for (var i = 0; i < batch.RowCount; i++) ids.Add(values.Values.Span[i]);
            }
        }

        Assert.Equal([3, 3, 1], batchSizes);
        Assert.Equal([0L, 1, 2, 3, 4, 5, 6], ids);
    }
}
