using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ETL_SQL.Tests.Scale;

public sealed class HaLargeJobSoakManifestTests
{
    [Fact]
    public void Manifest_DefinesMixedConcurrentWorkloadAndCancellationPoints()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("ha-soak-contract", root.GetProperty("matrixStatus").GetString());

        var budgets = root.GetProperty("sharedBudgets");
        Assert.True(budgets.GetProperty("memoryBoundMB").GetInt32() > 0);
        Assert.True(budgets.GetProperty("operatorGrantMB").GetInt32() > 0);
        Assert.True(budgets.GetProperty("minimumFreeDiskGB").GetInt32() > 0);

        var invariants = root.GetProperty("cleanupInvariants").EnumerateArray()
            .Select(e => e.GetString()!)
            .ToArray();
        Assert.Contains(invariants, value => value.Contains("MemoryGrantArbiter", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(invariants, value => value.Contains("spill extents", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(invariants, value => value.Contains("file handles", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(invariants, value => value.Contains("duplicate committed mutation", StringComparison.OrdinalIgnoreCase));

        var scenarios = root.GetProperty("scenarios").EnumerateArray().ToArray();
        var mixed = FindScenario(scenarios, "MixedScanSpillSortJoinAggregate_Concurrent");
        Assert.Equal("Planned", mixed.GetProperty("state").GetString());
        AssertRequiredArrayMembers(mixed, "workloads",
            "StreamingScanFilterProjection",
            "TempTableSpillRoundTrip",
            "ExternalSort",
            "ExternalEquiJoin",
            "HighCardinalityExternalAggregate");
        AssertRequiredArrayMembers(mixed, "requiredTelemetry",
            "memoryGrantOutstanding",
            "spillExtentCount",
            "schedulerQueueDepth",
            "jobStartDelayMs");

        var cancellationPoints = scenarios
            .Where(s => s.TryGetProperty("cancellationPoint", out _))
            .Select(s => s.GetProperty("cancellationPoint").GetString())
            .OrderBy(value => value)
            .ToArray();
        Assert.Equal(new[] { "repartition", "scan", "spill-read", "spill-write" }, cancellationPoints);

        foreach (var scenario in scenarios)
        {
            Assert.Equal("Planned", scenario.GetProperty("state").GetString());
            Assert.True(scenario.GetProperty("requiredTelemetry").GetArrayLength() > 0);
        }
    }

    private static JsonElement FindScenario(JsonElement[] scenarios, string scenarioId)
        => scenarios.Single(s => s.GetProperty("scenarioId").GetString() == scenarioId);

    private static void AssertRequiredArrayMembers(JsonElement scenario, string propertyName, params string[] expected)
    {
        var values = scenario.GetProperty(propertyName).EnumerateArray()
            .Select(e => e.GetString())
            .ToHashSet(StringComparer.Ordinal);
        foreach (var value in expected)
            Assert.Contains(value, values);
    }

    private static string ManifestPath()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(root, "certification-results", "ha-large-job-soak-scenarios.json");
    }
}
