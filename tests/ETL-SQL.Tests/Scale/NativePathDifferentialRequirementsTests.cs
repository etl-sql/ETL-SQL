using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ETL_SQL.Tests.Scale;

public sealed class NativePathDifferentialRequirementsTests
{
    [Fact]
    public void DifferentialRequirements_DefineNativePathAdmissionCoverage()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RequirementsPath()));
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(new[] { 1000, 50000 }, root.GetProperty("requiredRowCounts").EnumerateArray().Select(e => e.GetInt32()).ToArray());
        Assert.Contains("row-engine", root.GetProperty("requiredComparisonMode").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.True(root.GetProperty("commonRequiredCases").GetArrayLength() >= 5);

        var expectedPaths = new[]
        {
            "ColumnarProjection",
            "ColumnarAggregate",
            "ColumnarGroupedAggregate",
            "ColumnarSort",
            "ColumnarJoin",
            "ColumnarSelectInto"
        };
        var paths = root.GetProperty("paths").EnumerateArray().ToArray();
        Assert.Equal(expectedPaths.Length, paths.Length);

        var source = File.ReadAllText(ColumnarRoutingTestPath());
        foreach (var candidatePath in expectedPaths)
        {
            var path = paths.Single(p => p.GetProperty("candidatePath").GetString() == candidatePath);
            Assert.True(path.GetProperty("requiredCases").GetArrayLength() >= 4);
            var coverage = path.GetProperty("existingCoverage").EnumerateArray().Select(e => e.GetString()!).ToArray();
            Assert.NotEmpty(coverage);
            foreach (var testName in coverage.Select(c => c.Split('.').Last()))
                Assert.Contains($"Task {testName}(", source, StringComparison.Ordinal);
        }
    }

    private static string RequirementsPath()
    {
        var root = RepoRoot();
        return Path.Combine(root, "certification-results", "native-path-differential-requirements.json");
    }

    private static string ColumnarRoutingTestPath()
    {
        var root = RepoRoot();
        return Path.Combine(root, "tests", "ETL-SQL.Tests", "Engine", "ColumnarSelectRoutingTests.cs");
    }

    private static string RepoRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
