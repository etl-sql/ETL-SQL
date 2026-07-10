using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ETL_SQL.Tests.Scale;

public sealed class Phase6FaultInjectionManifestTests
{
    [Fact]
    public void Manifest_DefinesRequiredFaultsSafetyAndCleanupEvidence()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("phase6-fault-contract", root.GetProperty("matrixStatus").GetString());

        var safety = root.GetProperty("runSafety");
        Assert.True(safety.GetProperty("isolatedRunRootRequired").GetBoolean());
        Assert.True(safety.GetProperty("disposablePostgresRequired").GetBoolean());
        Assert.False(safety.GetProperty("productionTargetsAllowed").GetBoolean());
        Assert.True(safety.GetProperty("orchestratorApiAuthenticationRequired").GetBoolean());
        Assert.False(safety.GetProperty("rawSecretLoggingAllowed").GetBoolean());

        var invariants = root.GetProperty("commonCleanupInvariants").EnumerateArray()
            .Select(e => e.GetString()!)
            .ToArray();
        Assert.Contains(invariants, value => value.Contains("MemoryGrantArbiter", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(invariants, value => value.Contains("writers and readers", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(invariants, value => value.Contains("Run temp root", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(invariants, value => value.Contains("duplicate committed mutation", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(invariants, value => value.Contains("redacts", StringComparison.OrdinalIgnoreCase));

        var faults = root.GetProperty("faults").EnumerateArray().ToArray();
        AssertRequiredFault(faults, "DiskLowSpaceBeforeExtentWrite", "disk");
        AssertRequiredFault(faults, "DiskFullDuringExtentWrite", "disk");
        AssertRequiredFault(faults, "SlowDiskWriteAndRead", "disk-latency");
        AssertRequiredFault(faults, "CorruptExtentBeforeRead", "corruption");
        AssertRequiredFault(faults, "IncompleteExtentAfterCrash", "crash-recovery");
        AssertRequiredFault(faults, "WorkerProcessCrashMidJob", "crash-recovery");
        AssertRequiredFault(faults, "PortalNodeLossWithActiveSession", "node-loss");
        AssertRequiredFault(faults, "OrchestratorLeaderLossDuringSchedule", "node-loss");
        AssertRequiredFault(faults, "PostgresOutageBrief", "database-outage");
        AssertRequiredFault(faults, "TempRootExhaustion", "filesystem");

        foreach (var fault in faults)
        {
            Assert.Equal("Planned", fault.GetProperty("state").GetString());
            Assert.False(string.IsNullOrWhiteSpace(fault.GetProperty("injectionPoint").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(fault.GetProperty("injectionMethod").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(fault.GetProperty("expectedResult").GetString()));
            Assert.True(fault.GetProperty("requiredEvidence").GetArrayLength() >= 4);
        }
    }

    private static void AssertRequiredFault(JsonElement[] faults, string faultId, string category)
    {
        var fault = faults.Single(f => f.GetProperty("faultId").GetString() == faultId);
        Assert.Equal(category, fault.GetProperty("category").GetString());
    }

    private static string ManifestPath()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(root, "certification-results", "phase6-fault-injection-matrix.json");
    }
}
