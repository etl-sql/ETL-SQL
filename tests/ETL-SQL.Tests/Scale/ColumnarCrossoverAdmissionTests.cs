using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ETL_SQL.Tests.Scale;

public sealed class ColumnarCrossoverAdmissionTests
{
    [Fact]
    public void AdmissionPolicy_DefinesThresholdsForEveryColumnarBenchmarkPair()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(PolicyPath()));
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("ETL_SQL.Benchmarks.ColumnarCrossoverBenchmarks", root.GetProperty("benchmarkClass").GetString());
        Assert.Equal(new[] { 1000, 50000 }, root.GetProperty("rowCounts").EnumerateArray().Select(e => e.GetInt32()).ToArray());

        var criteria = root.GetProperty("commonCriteria");
        Assert.Equal(1.10m, criteria.GetProperty("smallWorkloadMaxSlowdownRatio").GetDecimal());
        Assert.Equal(1.00m, criteria.GetProperty("mediumWorkloadMaxSlowdownRatio").GetDecimal());
        Assert.Equal(1.00m, criteria.GetProperty("mediumWorkloadMaxAllocationRatio").GetDecimal());
        Assert.True(criteria.GetProperty("minimumSamples").GetInt32() >= 5);
        Assert.Contains("checksum", criteria.GetProperty("correctnessOracle").GetString(), StringComparison.OrdinalIgnoreCase);

        var expected = new[]
        {
            ("ColumnarProjection", "RowReference_FilterProject", "NativeColumnar_FilterProject"),
            ("ColumnarGroupedAggregate", "RowReference_GroupAggregate", "NativeColumnar_GroupAggregate"),
            ("ColumnarSort", "RowReference_Sort", "NativeColumnar_Sort"),
            ("ColumnarJoin", "RowReference_InnerJoin", "NativeColumnar_InnerJoin")
        };
        var candidates = root.GetProperty("candidates").EnumerateArray().ToArray();
        Assert.Equal(expected.Length, candidates.Length);

        foreach (var (candidatePath, rowReference, native) in expected)
        {
            var candidate = candidates.Single(c => c.GetProperty("candidatePath").GetString() == candidatePath);
            Assert.Equal(rowReference, candidate.GetProperty("rowReferenceBenchmark").GetString());
            Assert.Equal(native, candidate.GetProperty("nativeBenchmark").GetString());
            Assert.False(string.IsNullOrWhiteSpace(candidate.GetProperty("semanticEnvelope").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(candidate.GetProperty("fallbackReasonForExcludedShapes").GetString()));
        }

        var status = root.GetProperty("resultStatus");
        Assert.False(status.GetProperty("checkedInResults").GetBoolean());
    }

    private static string PolicyPath()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(root, "certification-results", "columnar-crossover-admission.json");
    }
}
