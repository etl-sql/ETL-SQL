using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ETL_SQL.Tests.Scale;

public sealed class BillionRowOperatorManifestTests
{
    [Fact]
    public void Phase4Manifest_DefinesSortAndJoinAdmissionAndSuccessCriteria()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        var scenarios = document.RootElement.GetProperty("scenarios").EnumerateArray().ToArray();

        var sort = FindScenario(scenarios, "ExternalSort_MultiKey_1B");
        Assert.Equal("Candidate", sort.GetProperty("state").GetString());
        Assert.Equal("ExternalSort", sort.GetProperty("operator").GetString());
        AssertRequiredObject(sort, "shape", "sortKeys", "randomSeed", "skew");
        AssertRequiredObject(sort, "admission", "minimumFreeDiskFormula", "memoryBoundMB", "operatorMemoryGrantMB", "spillPath");
        AssertRequiredObject(sort, "successCriteria", "correctnessOracle", "requiredTelemetry", "maxPeakWorkingSetMB");
        AssertNonEmptyArray(sort, "nonGoals");
        AssertNonEmptyArray(sort, "resumeKeyFields");

        var join = FindScenario(scenarios, "ExternalEquiJoin_ControlledSkew_1B");
        Assert.Equal("Candidate", join.GetProperty("state").GetString());
        Assert.Equal("ExternalEquiJoin", join.GetProperty("operator").GetString());
        AssertRequiredObject(join, "shape", "joinType", "keyOverlap", "duplicateFactor", "randomSeed", "skew");
        AssertRequiredObject(join, "admission", "minimumFreeDiskFormula", "memoryBoundMB", "operatorMemoryGrantMB", "spillPath");
        AssertRequiredObject(join, "successCriteria", "correctnessOracle", "requiredTelemetry", "maxPeakWorkingSetMB");
        AssertNonEmptyArray(join, "nonGoals");
        AssertNonEmptyArray(join, "resumeKeyFields");
    }

    [Fact]
    public void Phase4Manifest_DoesNotAdvertiseUnprovenLaterOperatorsAsCertified()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        var scenarios = document.RootElement.GetProperty("scenarios").EnumerateArray().ToArray();
        var laterOperators = new[]
        {
            "HighCardinalityGrouping_1B",
            "EligibleWindowRowNumber_1B",
            "HolisticAggregates_1B",
            "HeterogeneousMerge_1B"
        };

        foreach (var scenarioId in laterOperators)
        {
            var scenario = FindScenario(scenarios, scenarioId);
            Assert.NotEqual("Certified", scenario.GetProperty("state").GetString());
            AssertNonEmptyArray(scenario, "nonGoals");
        }
    }

    private static JsonElement FindScenario(JsonElement[] scenarios, string scenarioId)
        => scenarios.Single(s => s.GetProperty("scenarioId").GetString() == scenarioId);

    private static void AssertRequiredObject(JsonElement scenario, string objectName, params string[] requiredProperties)
    {
        var obj = scenario.GetProperty(objectName);
        foreach (var propertyName in requiredProperties)
            Assert.True(
                obj.TryGetProperty(propertyName, out _),
                $"{scenario.GetProperty("scenarioId").GetString()} missing {objectName}.{propertyName}");
    }

    private static void AssertNonEmptyArray(JsonElement scenario, string propertyName)
    {
        var array = scenario.GetProperty(propertyName);
        Assert.Equal(JsonValueKind.Array, array.ValueKind);
        Assert.True(array.GetArrayLength() > 0, $"{scenario.GetProperty("scenarioId").GetString()} has empty {propertyName}");
    }

    private static string ManifestPath()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(root, "certification-results", "billion-row-operator-scenarios.json");
    }
}
