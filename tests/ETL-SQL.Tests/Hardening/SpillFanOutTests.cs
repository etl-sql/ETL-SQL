using System.Globalization;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Spill;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Engines;
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

    [Theory]
    [InlineData(0L, 8, 0L)]
    [InlineData(4_194_304L, 1, 4_194_304L)]
    [InlineData(4_194_304L, 16, 4_194_304L)]
    [InlineData(4_194_304L, 128, 524_288L)]
    [InlineData(4_194_304L, 2_048, 65_536L)]
    [InlineData(32_768L, 1_024, 32_768L)]
    public void FlushBytesShareOneBudgetAcrossPartitions(long batchBytes, int partitions, long expected)
    {
        Assert.Equal(expected, SpillFanOut.FlushBytes(batchBytes, partitions));
    }

    [Fact]
    public void BufferedBytesStayBoundedAsPartitionsGrow()
    {
        const long batchBytes = SpillFanOut.DefaultTargetBatchBytes;
        long bound = batchBytes * SpillFanOut.BufferedBatches;
        for (int partitions = 1; partitions <= 4_096; partitions *= 2)
        {
            long buffered = (long)partitions * SpillFanOut.FlushBytes(batchBytes, partitions);
            Assert.True(buffered <= Math.Max(bound, (long)partitions * SpillFanOut.MinimumFlushBytes),
                $"{partitions} partitions buffer {buffered} bytes.");
        }
    }

    [Fact]
    public async Task WriterFlushesEarlyWhenWideStringsExceedByteBudget()
    {
        var provider = DependencyInjectionSetup.BuildServiceProvider();
        await using var lifetime = provider as IAsyncDisposable;
        provider.GetRequiredService<ILogger>().SuppressConsole = true;
        var evaluator = provider.GetRequiredService<Evaluator>();
        using var store = new SpillStore(evaluator);

        const int rowCount = 12;
        await using (var writer = await store.CreateWriterAsync("fan_out_bytes", flushBatchRows: 1_000, flushBatchBytes: 8_192))
        {
            for (int i = 0; i < rowCount; i++)
            {
                string? payload = i == 5 ? null : new string((char)('A' + (i % 26)), 1_500);
                await writer.WriteRowAsync(new Row
                {
                    ["Id"] = (long)i,
                    ["Payload"] = payload
                });
            }
        }

        var batchSizes = new List<int>();
        var ids = new List<long>();
        var payloads = new List<string?>();
        await using var reader = await store.CreateReaderAsync("fan_out_bytes");
        await foreach (var batch in Assert.IsAssignableFrom<IColumnarSpillReader>(reader).AsColumnBatchesAsync())
        {
            using (batch)
            {
                batchSizes.Add(batch.RowCount);
                var idCol = batch.GetColumn<long>("Id");
                var payloadCol = batch.GetUtf8Column("Payload");
                for (int i = 0; i < batch.RowCount; i++)
                {
                    ids.Add(idCol.Values.Span[i]);
                    payloads.Add((string?)payloadCol.GetBoxedValue(i));
                }
            }
        }

        Assert.True(batchSizes.Count >= 4, $"Expected multiple flushes driven by byte budget, got {batchSizes.Count} batch(es).");
        Assert.Equal(rowCount, ids.Count);
        for (int i = 0; i < rowCount; i++)
        {
            Assert.Equal((long)i, ids[i]);
            string? expected = i == 5 ? null : new string((char)('A' + (i % 26)), 1_500);
            Assert.Equal(expected, payloads[i]);
        }
    }

    [Fact]
    public async Task ColumnarWriterSlicesOversizedWideStringBatchAndRoundTrips()
    {
        var provider = DependencyInjectionSetup.BuildServiceProvider();
        await using var lifetime = provider as IAsyncDisposable;
        provider.GetRequiredService<ILogger>().SuppressConsole = true;
        var evaluator = provider.GetRequiredService<Evaluator>();
        using var store = new SpillStore(evaluator);

        const int rowCount = 20;
        var batchSchema = new ColumnBatchSchema([
            new ColumnBatchField("Id", typeof(long), "BIGINT", IsNullable: false),
            new ColumnBatchField("Amount", typeof(decimal), "DECIMAL(18,2)", IsNullable: true),
            new ColumnBatchField("Text", typeof(string), "VARCHAR(MAX)", IsNullable: true)
        ]);
        var idValues = Enumerable.Range(0, rowCount).Select(i => (long)i).ToArray();
        var decValues = Enumerable.Range(0, rowCount).Select(i => i * 1.25m).ToArray();
        var textValues = Enumerable.Range(0, rowCount)
            .Select(i => i == 7 ? null : $"row_{i.ToString("D2", CultureInfo.InvariantCulture)}_" + new string((char)('a' + (i % 26)), 2_000))
            .ToArray();

        using var idBuf = new ColumnBuffer<long>(idValues, rowCount);
        using var decBuf = new ColumnBuffer<decimal>(decValues, rowCount);
        using var textBuf = Utf8ColumnBuffer.FromStrings(textValues);
        using var inputBatch = new ColumnBatch(batchSchema, [idBuf, decBuf, textBuf], rowCount);

        await using (var writer = await store.CreateWriterAsync("slice_columnar", flushBatchRows: 1_000, flushBatchBytes: 8_192))
        {
            var columnarWriter = Assert.IsAssignableFrom<IColumnarSpillWriter>(writer);
            await columnarWriter.WriteBatchAsync(inputBatch);
        }

        var readBatchSizes = new List<int>();
        var readIds = new List<long>();
        var readDecimals = new List<decimal>();
        var readTexts = new List<string?>();
        await using var reader = await store.CreateReaderAsync("slice_columnar");
        await foreach (var batch in Assert.IsAssignableFrom<IColumnarSpillReader>(reader).AsColumnBatchesAsync())
        {
            using (batch)
            {
                readBatchSizes.Add(batch.RowCount);
                var ids = batch.GetColumn<long>("Id");
                var decs = batch.GetColumn<decimal>("Amount");
                var texts = batch.GetUtf8Column("Text");
                for (int i = 0; i < batch.RowCount; i++)
                {
                    readIds.Add(ids.Values.Span[i]);
                    readDecimals.Add(decs.Values.Span[i]);
                    readTexts.Add((string?)texts.GetBoxedValue(i));
                }
            }
        }

        Assert.True(readBatchSizes.Count > 1, $"Expected oversized ColumnBatch to slice across record batches, got {readBatchSizes.Count}.");
        Assert.Equal(idValues, readIds);
        Assert.Equal(decValues, readDecimals);
        Assert.Equal(textValues, readTexts);
    }

    [Fact]
    public async Task ReadersAndStreamingQueryFlushWideStringBatchesByByteBudget()
    {
        var provider = DependencyInjectionSetup.BuildServiceProvider();
        await using var lifetime = provider as IAsyncDisposable;
        provider.GetRequiredService<ILogger>().SuppressConsole = true;
        var evaluator = provider.GetRequiredService<Evaluator>();
        evaluator.IsPersistentSession = false;
        evaluator.BatchSize = 1_000;
        evaluator.BatchTargetBytes = 8_192;

        const int rowCount = 10;
        var input = new DataTable();
        input.SetColumns(["Id", "Payload"]);
        for (int i = 0; i < rowCount; i++)
        {
            var r = input.NewRow();
            r["Id"] = (decimal)i;
            r["Payload"] = new string((char)('K' + (i % 10)), 2_000);
            await input.AddRowAsync(r);
        }

        // 1. InMemoryDataSource.ReadBatches splits wide resident batches by BatchTargetBytes
        var mem = new InMemoryDataSource { ExecutionContext = evaluator };
        mem.SetSchema([new ColumnDefinition("Id", "INT", false), new ColumnDefinition("Payload", "VARCHAR(MAX)", false)]);
        await mem.WriteBatches(new[] { input }.ToAsyncEnumerable());

        var memBatchSizes = new List<int>();
        await foreach (var b in mem.ReadBatches(evaluator.BatchSize))
            memBatchSizes.Add(b.Rows.Count);
        Assert.True(memBatchSizes.Count > 1, $"Expected InMemoryDataSource to yield multiple batches, got {memBatchSizes.Count}.");
        Assert.Equal(rowCount, memBatchSizes.Sum());

        // 2. AppendOnlyColumnDataSource.WriteBatches freezes wide head segments by SegmentByteCapacity
        var colStore = new AppendOnlyColumnDataSource(
            [new ColumnDefinition("Id", "INT", false), new ColumnDefinition("Payload", "VARCHAR(MAX)", false)],
            segmentRowCapacity: 1_000,
            segmentByteCapacity: 8_192);
        await using (colStore)
        {
            await colStore.WriteBatches(new[] { input }.ToAsyncEnumerable());
            Assert.True(colStore.SegmentCount > 1, $"Expected multiple segments for wide head, got {colStore.SegmentCount}.");
            Assert.Equal(rowCount, colStore.EstimatedRowCount);
        }

        // 3. StreamingQueryEngine yields multiple batches by EffectiveBatchTargetBytes
        const string sql = "SELECT Id, Payload FROM #src";
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var stmt = Assert.IsType<SelectStatement>(script.Statements[0]);
        var streaming = new StreamingQueryEngine(evaluator, provider.GetRequiredService<ILogger>());
        var streamBatchSizes = new List<int>();
        await foreach (var b in streaming.ExecuteStreamingSelect(stmt, new[] { input }.ToAsyncEnumerable(), stmt.Columns, ["Id", "Payload"]))
            streamBatchSizes.Add(b.Rows.Count);
        Assert.True(streamBatchSizes.Count > 1, $"Expected StreamingQueryEngine to flush multiple batches, got {streamBatchSizes.Count}.");
        Assert.Equal(rowCount, streamBatchSizes.Sum());
    }
}
