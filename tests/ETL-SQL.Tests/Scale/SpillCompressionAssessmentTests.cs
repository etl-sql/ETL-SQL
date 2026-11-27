using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core.Spill;
using ETL_SQL.Data;
using ETL_SQL.Engine.Spill;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ETL_SQL.Tests.Scale;

[Trait("Category", "ScaleAssessment")]
public sealed class SpillCompressionAssessmentTests(ITestOutputHelper output)
{
    private const int RowCount = 100_000;

    [Fact]
    public async Task ArrowSpill_CompressionTradeoff_ReportsPhysicalBytesAndCpu()
    {
        var compressible = await CompareAsync(highEntropy: false);
        var highEntropy = await CompareAsync(highEntropy: true);

        output.WriteLine("SPILL_COMPRESSION_METRIC:" + JsonSerializer.Serialize(new
        {
            rowCount = RowCount,
            compressible,
            highEntropy
        }));

        AssertComparison(compressible);
        AssertComparison(highEntropy);
    }

    [Fact]
    public async Task ArrowSpill_EncryptedCompressionLevelTradeoff_ReportsFastestVsOptimal()
    {
        var compressible = await CompareEncryptedLevelsAsync(highEntropy: false);
        var highEntropy = await CompareEncryptedLevelsAsync(highEntropy: true);

        output.WriteLine("SPILL_ENCRYPTED_COMPRESSION_LEVEL_METRIC:" + JsonSerializer.Serialize(new
        {
            rowCount = RowCount,
            compressible,
            highEntropy
        }));

        AssertEncryptedComparison(compressible);
        AssertEncryptedComparison(highEntropy);
    }

    [Fact]
    public async Task ArrowSpill_ConcurrentEncryptedPipeline_ComparesAdaptiveFastestAndOptimal()
    {
        const int writerCount = 4;
        const int rowsPerWriter = 25_000;

        var fastest = await MeasureConcurrentEncryptedAsync(
            writerCount, rowsPerWriter, configuredLevel: "Fastest", overrideLevel: CompressionLevel.Fastest);
        var optimal = await MeasureConcurrentEncryptedAsync(
            writerCount, rowsPerWriter, configuredLevel: "Optimal", overrideLevel: CompressionLevel.Optimal);
        var adaptive = await MeasureConcurrentEncryptedAsync(
            writerCount, rowsPerWriter, configuredLevel: "Adaptive", overrideLevel: null);

        output.WriteLine("SPILL_CONCURRENT_ENCRYPTED_METRIC:" + JsonSerializer.Serialize(new
        {
            writerCount,
            rowsPerWriter,
            totalRows = writerCount * rowsPerWriter,
            fastest,
            optimal,
            adaptive,
            fastestToOptimalWriteElapsedRatio = Math.Round((double)fastest.WriteElapsedMs / Math.Max(1, optimal.WriteElapsedMs), 4),
            adaptiveToOptimalWriteElapsedRatio = Math.Round((double)adaptive.WriteElapsedMs / Math.Max(1, optimal.WriteElapsedMs), 4)
        }));

        var expectedTotalRows = (long)writerCount * rowsPerWriter;
        Assert.Equal(expectedTotalRows, fastest.RowsRead);
        Assert.Equal(expectedTotalRows, optimal.RowsRead);
        Assert.Equal(expectedTotalRows, adaptive.RowsRead);
        Assert.Equal(fastest.Checksum, optimal.Checksum);
        Assert.Equal(fastest.Checksum, adaptive.Checksum);
        Assert.True(adaptive.FastestSelections >= writerCount - 1,
            $"Expected concurrent adaptive mode to select Fastest for concurrent writers, observed Fastest={adaptive.FastestSelections}, Optimal={adaptive.OptimalSelections}.");
    }

    [Fact]
    public async Task ArrowSpill_BoundedDictionaryEncoding_EvaluatesLowAndHighCardinalityStrings()
    {
        var lowCardUncompressed = await MeasureDictionaryWorkloadAsync(highCardinality: false, compress: false, encrypt: false);
        var highCardUncompressed = await MeasureDictionaryWorkloadAsync(highCardinality: true, compress: false, encrypt: false);
        var lowCardEncrypted = await MeasureDictionaryWorkloadAsync(highCardinality: false, compress: true, encrypt: true);
        var highCardEncrypted = await MeasureDictionaryWorkloadAsync(highCardinality: true, compress: true, encrypt: true);

        output.WriteLine("SPILL_DICTIONARY_EVALUATION_METRIC:" + JsonSerializer.Serialize(new
        {
            rowCount = RowCount,
            lowCardUncompressed,
            highCardUncompressed,
            lowCardEncrypted,
            highCardEncrypted,
            uncompressedLowToHighByteRatio = Math.Round((double)lowCardUncompressed.PhysicalBytes / Math.Max(1, highCardUncompressed.PhysicalBytes), 4),
            encryptedLowToHighByteRatio = Math.Round((double)lowCardEncrypted.PhysicalBytes / Math.Max(1, highCardEncrypted.PhysicalBytes), 4),
        }));

        Assert.Equal(RowCount, lowCardUncompressed.RowsRead);
        Assert.Equal(RowCount, highCardUncompressed.RowsRead);
        Assert.Equal(RowCount, lowCardEncrypted.RowsRead);
        Assert.Equal(RowCount, highCardEncrypted.RowsRead);
        Assert.Equal(lowCardUncompressed.Checksum, lowCardEncrypted.Checksum);
        Assert.Equal(highCardUncompressed.Checksum, highCardEncrypted.Checksum);
        Assert.True(lowCardUncompressed.PhysicalBytes < highCardUncompressed.PhysicalBytes / 2,
            $"Expected bounded dictionary encoding on repeated low-cardinality strings ({lowCardUncompressed.PhysicalBytes} B) to be less than half of high-cardinality fallback ({highCardUncompressed.PhysicalBytes} B).");
    }

    private static async Task<Measurement> MeasureDictionaryWorkloadAsync(bool highCardinality, bool compress, bool encrypt)
    {
        await using var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.SpillEncryptionEnabled = encrypt;
        evaluator.SpillCompressionEnabled = compress;
        evaluator.Telemetry.Clear();

        var categories = Enumerable.Range(0, 16)
            .Select(i => $"DEPARTMENT-ANALYTICS-REGION-CATEGORY-{i:D4}-STANDARD-LABEL")
            .ToArray();
        var schema = new TableSchema(["Id", "Label"]);
        IEnumerable<Row> Rows()
        {
            for (var i = 0; i < RowCount; i++)
            {
                var row = new Row(schema);
                row["Id"] = (decimal)(i + 1);
                row["Label"] = highCardinality
                    ? $"UNIQUE-ENTITY-PAYLOAD-IDENTIFIER-RECORD-{i:D10}-SUFFIX"
                    : categories[i % categories.Length];
                yield return row;
            }
        }

        var chunkName = $"dict-eval-{highCardinality}-{compress}-{Guid.NewGuid():N}.arrow";
        var process = Process.GetCurrentProcess();
        var cpuBeforeWrite = process.TotalProcessorTime;
        var writeWatch = Stopwatch.StartNew();
        // Single-batch chunk so bounded DictionaryType activates when cardinality is within bounds.
        await using (var writer = await evaluator.SpillStore.CreateWriterAsync(chunkName, flushBatchRows: RowCount + 1, flushBatchBytes: 0))
            await writer.WriteRowsAsync(Rows());
        writeWatch.Stop();
        process.Refresh();
        var writeCpu = process.TotalProcessorTime - cpuBeforeWrite;

        var physicalBytes = new FileInfo(Path.Combine(evaluator.SpillStore.RootPath, chunkName)).Length;
        long rowsRead = 0;
        long checksum = 0;
        var cpuBeforeRead = process.TotalProcessorTime;
        var readWatch = Stopwatch.StartNew();
        await using (var reader = await evaluator.SpillStore.CreateReaderAsync(chunkName))
        {
            await foreach (var row in reader.AsEnumerableAsync())
            {
                rowsRead++;
                checksum += Convert.ToInt64(row["Id"]) + ((string?)row["Label"])!.Length;
            }
        }
        readWatch.Stop();
        process.Refresh();
        var readCpu = process.TotalProcessorTime - cpuBeforeRead;

        evaluator.SpillStore.DeleteChunk(chunkName);
        return new Measurement(
            physicalBytes,
            writeWatch.ElapsedMilliseconds,
            Math.Round(writeCpu.TotalMilliseconds, 1),
            readWatch.ElapsedMilliseconds,
            Math.Round(readCpu.TotalMilliseconds, 1),
            rowsRead,
            checksum);
    }

    private static async Task<Comparison> CompareAsync(bool highEntropy)
    {
        var uncompressed = await MeasureAsync(compress: false, highEntropy, encrypt: false);
        var compressed = await MeasureAsync(compress: true, highEntropy, encrypt: false);
        return new Comparison(
            uncompressed,
            compressed,
            Math.Round((double)compressed.PhysicalBytes / uncompressed.PhysicalBytes, 4),
            Math.Round((double)compressed.WriteElapsedMs / Math.Max(1, uncompressed.WriteElapsedMs), 4),
            Math.Round((double)compressed.ReadElapsedMs / Math.Max(1, uncompressed.ReadElapsedMs), 4));
    }

    private static async Task<EncryptedLevelComparison> CompareEncryptedLevelsAsync(bool highEntropy)
    {
        var uncompressed = await MeasureAsync(compress: false, highEntropy, encrypt: true);
        var fastest = await MeasureAsync(compress: true, highEntropy, encrypt: true, CompressionLevel.Fastest);
        var optimal = await MeasureAsync(compress: true, highEntropy, encrypt: true, CompressionLevel.Optimal);
        return new EncryptedLevelComparison(
            uncompressed,
            fastest,
            optimal,
            Math.Round((double)fastest.PhysicalBytes / uncompressed.PhysicalBytes, 4),
            Math.Round((double)optimal.PhysicalBytes / uncompressed.PhysicalBytes, 4),
            Math.Round((double)fastest.WriteElapsedMs / Math.Max(1, optimal.WriteElapsedMs), 4),
            Math.Round((double)fastest.WriteCpuMs / Math.Max(1.0, optimal.WriteCpuMs), 4));
    }

    private static void AssertComparison(Comparison comparison)
    {
        Assert.Equal(RowCount, comparison.Uncompressed.RowsRead);
        Assert.Equal(RowCount, comparison.Compressed.RowsRead);
        Assert.Equal(comparison.Uncompressed.Checksum, comparison.Compressed.Checksum);
    }

    private static void AssertEncryptedComparison(EncryptedLevelComparison comparison)
    {
        Assert.Equal(RowCount, comparison.Uncompressed.RowsRead);
        Assert.Equal(RowCount, comparison.Fastest.RowsRead);
        Assert.Equal(RowCount, comparison.Optimal.RowsRead);
        Assert.Equal(comparison.Uncompressed.Checksum, comparison.Fastest.Checksum);
        Assert.Equal(comparison.Uncompressed.Checksum, comparison.Optimal.Checksum);
    }

    private static async Task<Measurement> MeasureAsync(
        bool compress,
        bool highEntropy,
        bool encrypt,
        CompressionLevel? compressionLevel = null)
    {
        await using var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.SpillEncryptionEnabled = encrypt;
        evaluator.SpillCompressionEnabled = compress;
        if (compressionLevel.HasValue && evaluator.SpillStore is SpillStore spillStore)
            spillStore.CompressionLevelOverride = compressionLevel.Value;
        evaluator.Telemetry.Clear();

        var chunkName = $"compression-assessment-{compress}-{Guid.NewGuid():N}.arrow";
        var process = Process.GetCurrentProcess();
        var cpuBeforeWrite = process.TotalProcessorTime;
        var writeWatch = Stopwatch.StartNew();
        await using (var writer = await evaluator.SpillStore.CreateWriterAsync(chunkName))
            await writer.WriteRowsAsync(GenerateRows(highEntropy));
        writeWatch.Stop();
        process.Refresh();
        var writeCpu = process.TotalProcessorTime - cpuBeforeWrite;

        var physicalBytes = new FileInfo(Path.Combine(evaluator.SpillStore.RootPath, chunkName)).Length;
        long rowsRead = 0;
        long checksum = 0;
        var cpuBeforeRead = process.TotalProcessorTime;
        var readWatch = Stopwatch.StartNew();
        await using (var reader = await evaluator.SpillStore.CreateReaderAsync(chunkName))
        {
            await foreach (var row in reader.AsEnumerableAsync())
            {
                rowsRead++;
                checksum += Convert.ToInt64(row["Id"]);
            }
        }
        readWatch.Stop();
        process.Refresh();
        var readCpu = process.TotalProcessorTime - cpuBeforeRead;

        evaluator.SpillStore.DeleteChunk(chunkName);
        return new Measurement(
            physicalBytes,
            writeWatch.ElapsedMilliseconds,
            Math.Round(writeCpu.TotalMilliseconds, 1),
            readWatch.ElapsedMilliseconds,
            Math.Round(readCpu.TotalMilliseconds, 1),
            rowsRead,
            checksum);
    }

    private static async Task<ConcurrentMeasurement> MeasureConcurrentEncryptedAsync(
        int writerCount,
        int rowsPerWriter,
        string configuredLevel,
        CompressionLevel? overrideLevel)
    {
        await using var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.SpillEncryptionEnabled = true;
        evaluator.SpillCompressionEnabled = true;
        evaluator.SpillCompressionLevel = configuredLevel;
        evaluator.Telemetry.Clear();

        if (evaluator.SpillStore is SpillStore spillStore && overrideLevel.HasValue)
            spillStore.CompressionLevelOverride = overrideLevel.Value;

        var chunkNames = Enumerable.Range(0, writerCount)
            .Select(idx => $"concurrent-assessment-{configuredLevel}-{idx}-{Guid.NewGuid():N}.arrow")
            .ToArray();

        var process = Process.GetCurrentProcess();
        var cpuBeforeWrite = process.TotalProcessorTime;
        var writeWatch = Stopwatch.StartNew();

        // Overlap all concurrent writers cleanly with an async barrier so adaptive admission sees all open writers.
        var remainingWriters = writerCount;
        var allOpen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Task.WhenAll(chunkNames.Select((chunkName, idx) => Task.Run(async () =>
        {
            ISpillWriter writer;
            try
            {
                writer = await evaluator.SpillStore.CreateWriterAsync(chunkName);
                if (Interlocked.Decrement(ref remainingWriters) == 0)
                    allOpen.TrySetResult();
                else
                    await allOpen.Task;
            }
            catch (Exception ex)
            {
                allOpen.TrySetException(ex);
                throw;
            }

            await using (writer)
                await writer.WriteRowsAsync(GenerateRowsRange(idx * rowsPerWriter + 1, rowsPerWriter, highEntropy: false));
        })));

        writeWatch.Stop();
        process.Refresh();
        var writeCpu = process.TotalProcessorTime - cpuBeforeWrite;

        var physicalBytes = chunkNames.Sum(name => new FileInfo(Path.Combine(evaluator.SpillStore.RootPath, name)).Length);

        var cpuBeforeRead = process.TotalProcessorTime;
        var readWatch = Stopwatch.StartNew();
        var readResults = await Task.WhenAll(chunkNames.Select(async chunkName =>
        {
            long rows = 0;
            long sum = 0;
            await using var reader = await evaluator.SpillStore.CreateReaderAsync(chunkName);
            await foreach (var row in reader.AsEnumerableAsync())
            {
                rows++;
                sum += Convert.ToInt64(row["Id"]);
            }
            return (rows, sum);
        }));
        readWatch.Stop();
        process.Refresh();
        var readCpu = process.TotalProcessorTime - cpuBeforeRead;

        foreach (var chunkName in chunkNames)
            evaluator.SpillStore.DeleteChunk(chunkName);

        var fastestSelections = evaluator.SpillStore is SpillStore store
            ? store.AdaptiveFastestSelections
            : 0;
        var optimalSelections = evaluator.SpillStore is SpillStore store2
            ? store2.AdaptiveOptimalSelections
            : 0;

        return new ConcurrentMeasurement(
            physicalBytes,
            writeWatch.ElapsedMilliseconds,
            Math.Round(writeCpu.TotalMilliseconds, 1),
            readWatch.ElapsedMilliseconds,
            Math.Round(readCpu.TotalMilliseconds, 1),
            readResults.Sum(r => r.rows),
            readResults.Sum(r => r.sum),
            fastestSelections,
            optimalSelections);
    }

    private static IEnumerable<Row> GenerateRows(bool highEntropy) =>
        GenerateRowsRange(1, RowCount, highEntropy);

    private static IEnumerable<Row> GenerateRowsRange(int startId, int count, bool highEntropy)
    {
        for (var i = 0; i < count; i++)
        {
            var id = startId + i;
            yield return new Row
            {
                ["Id"] = id,
                ["Group"] = id % 100,
                ["Status"] = id % 5 == 0 ? "complete" : "pending",
                ["Payload"] = highEntropy
                    ? HighEntropyPayload(id)
                    : $"customer-{id % 10_000:D5}|region-{id % 20:D2}|repeatable-assessment-payload"
            };
        }
    }

    private static string HighEntropyPayload(int value)
    {
        // SplitMix64 produces deterministic, uniformly distributed bits without adding crypto cost
        // to this assessment. Four words make each payload effectively unique and hard to compress.
        var state = (ulong)value;
        Span<char> output = stackalloc char[64];
        for (var word = 0; word < 4; word++)
        {
            state += 0x9E3779B97F4A7C15UL;
            var mixed = state;
            mixed = (mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL;
            mixed = (mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL;
            mixed ^= mixed >> 31;
            mixed.TryFormat(output.Slice(word * 16, 16), out _, "X16");
        }
        return new string(output);
    }

    private sealed record Measurement(
        long PhysicalBytes,
        long WriteElapsedMs,
        double WriteCpuMs,
        long ReadElapsedMs,
        double ReadCpuMs,
        long RowsRead,
        long Checksum);

    private sealed record Comparison(
        Measurement Uncompressed,
        Measurement Compressed,
        double PhysicalByteRatio,
        double WriteElapsedRatio,
        double ReadElapsedRatio);

    private sealed record EncryptedLevelComparison(
        Measurement Uncompressed,
        Measurement Fastest,
        Measurement Optimal,
        double FastestByteRatio,
        double OptimalByteRatio,
        double FastestToOptimalWriteElapsedRatio,
        double FastestToOptimalWriteCpuRatio);

    private sealed record ConcurrentMeasurement(
        long PhysicalBytes,
        long WriteElapsedMs,
        double WriteCpuMs,
        long ReadElapsedMs,
        double ReadCpuMs,
        long RowsRead,
        long Checksum,
        long FastestSelections,
        long OptimalSelections);
}
