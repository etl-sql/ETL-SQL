using System;
using System.Diagnostics;
using System.Text;
using ETL_SQL.Core;
using Xunit;

namespace ETL_SQL.Tests.Scale;

public sealed class ScriptScaleAssessmentTests
{
    [Fact]
    [Trait("Category", "ScaleAssessment")]
    public void LexerAndParserScaleLinearlyFromTenThousandToOneHundredThousandLines()
    {
        _ = Parse(100); // JIT and initialize parser metadata outside measurements.

        var small = Parse(10_000);
        var large = Parse(100_000);

        Assert.Equal(10_000, small.StatementCount);
        Assert.Equal(100_000, large.StatementCount);
        Assert.True(large.AllocatedBytes <= small.AllocatedBytes * 15L,
            $"Allocation scaling exceeded 15x: 10k={small.AllocatedBytes:N0}, 100k={large.AllocatedBytes:N0}.");
        Assert.True(large.AllocatedBytes < 512L * 1024 * 1024,
            $"100k-line parse allocated {large.AllocatedBytes:N0} bytes.");
        Assert.True(large.Elapsed <= small.Elapsed * 15 + TimeSpan.FromSeconds(1),
            $"Elapsed scaling exceeded tolerance: 10k={small.Elapsed}, 100k={large.Elapsed}.");
    }

    private static ParseMeasurement Parse(int lineCount)
    {
        var source = new StringBuilder(lineCount * 11);
        for (var line = 0; line < lineCount; line++) source.AppendLine("PRINT 'x';");
        var text = source.ToString();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var script = new Parser(new Lexer(text).Tokenize(), text).Parse();
        stopwatch.Stop();
        return new ParseMeasurement(
            script.Statements.Count,
            GC.GetAllocatedBytesForCurrentThread() - before,
            stopwatch.Elapsed);
    }

    private sealed record ParseMeasurement(int StatementCount, long AllocatedBytes, TimeSpan Elapsed);
}
