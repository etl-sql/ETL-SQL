using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Data;
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

    private static async Task<Comparison> CompareAsync(bool highEntropy)
    {
        var uncompressed = await MeasureAsync(compress: false, highEntropy);
        var compressed = await MeasureAsync(compress: true, highEntropy);
        return new Comparison(
            uncompressed,
            compressed,
            Math.Round((double)compressed.PhysicalBytes / uncompressed.PhysicalBytes, 4),
            Math.Round((double)compressed.WriteElapsedMs / Math.Max(1, uncompressed.WriteElapsedMs), 4),
            Math.Round((double)compressed.ReadElapsedMs / Math.Max(1, uncompressed.ReadElapsedMs), 4));
    }

    private static void AssertComparison(Comparison comparison)
    {
        Assert.Equal(RowCount, comparison.Uncompressed.RowsRead);
        Assert.Equal(RowCount, comparison.Compressed.RowsRead);
        Assert.Equal(comparison.Uncompressed.Checksum, comparison.Compressed.Checksum);
    }

    private static async Task<Measurement> MeasureAsync(bool compress, bool highEntropy)
    {
        await using var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.SpillEncryptionEnabled = false;
        evaluator.SpillCompressionEnabled = compress;
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

    private static IEnumerable<Row> GenerateRows(bool highEntropy)
    {
        for (var i = 1; i <= RowCount; i++)
        {
            yield return new Row
            {
                ["Id"] = i,
                ["Group"] = i % 100,
                ["Status"] = i % 5 == 0 ? "complete" : "pending",
                ["Payload"] = highEntropy
                    ? HighEntropyPayload(i)
                    : $"customer-{i % 10_000:D5}|region-{i % 20:D2}|repeatable-assessment-payload"
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
}
