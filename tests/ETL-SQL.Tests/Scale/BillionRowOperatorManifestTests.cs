using System;
using System.Collections.Generic;
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

    [Fact]
    public void LargeDataCertificationMatrix_MatchesManifestStatesAndAvoidsBlanketClaim()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        var scenarios = document.RootElement.GetProperty("scenarios").EnumerateArray().ToArray();
        var markdown = File.ReadAllText(LargeDataCertificationPath());
        var matrixRows = ParseMatrixRows(markdown);

        foreach (var scenario in scenarios)
        {
            var scenarioId = scenario.GetProperty("scenarioId").GetString()!;
            Assert.True(matrixRows.TryGetValue(scenarioId, out var row), $"Large data matrix missing {scenarioId}.");
            var state = scenario.GetProperty("state").GetString()!;
            Assert.Equal(state, row.State);

            if (state == "Certified")
            {
                Assert.DoesNotContain("Pending", row.Artifact, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("No 1B claim", row.Artifact, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                Assert.NotEqual("Certified", row.State);
                Assert.True(
                    row.Artifact.Contains("Pending", StringComparison.OrdinalIgnoreCase) ||
                    row.Artifact.Contains("No 1B claim", StringComparison.OrdinalIgnoreCase),
                    $"{scenarioId} is {state} but the public artifact cell does not make the non-certified status explicit.");
            }
        }

        Assert.DoesNotContain("1B SQL support", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("all SQL at 1B", markdown, StringComparison.OrdinalIgnoreCase);
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

    private static Dictionary<string, MatrixRow> ParseMatrixRows(string markdown)
    {
        var rows = new Dictionary<string, MatrixRow>(StringComparer.Ordinal);
        foreach (var rawLine in markdown.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("| `", StringComparison.Ordinal)) continue;
            var cells = line.Split('|')
                .Select(cell => cell.Trim())
                .Where(cell => cell.Length > 0)
                .ToArray();
            if (cells.Length != 4) continue;
            var scenarioId = cells[0].Trim('`');
            rows[scenarioId] = new MatrixRow(cells[1], cells[2], cells[3]);
        }
        return rows;
    }

    private static string ManifestPath()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(root, "certification-results", "billion-row-operator-scenarios.json");
    }

    private static string LargeDataCertificationPath()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(root, "Docs", "Large_Data_Certification.md");
    }

    private sealed record MatrixRow(string Operator, string State, string Artifact);
}
