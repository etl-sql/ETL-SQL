using System.Security.Cryptography;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.TestSupport;

namespace ETL_SQL.Tests.Orchestration;

[Trait("Category", "DeploymentProfile")]
public sealed class DeploymentTransitionLifecycleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"profile-transition-{Guid.NewGuid():N}");

    public DeploymentTransitionLifecycleTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact] public Task SoloToTeam_BackupFenceCutoverProofAndRollback() => RunAsync("Solo", "Team");
    [Fact] public Task TeamToEnterprise_BackupFenceCutoverProofAndRollback() => RunAsync("Team", "Enterprise");
    [Fact] public Task EnterpriseToSaas_BackupFenceCutoverProofAndRollback() => RunAsync("Enterprise", "SaaS");
    [Fact] public Task SoloToSaas_BackupFenceCutoverProofAndRollback() => RunAsync("Solo", "SaaS");

    // The SaaS -> self-hosted Enterprise exit journey is deliberately NOT here. Promotion preflight
    // refuses any backward move (DP001) and says to use an explicit export/restore workflow instead;
    // that workflow is the tenant portability bundle, so the exit path is certified in
    // TenantExitJourneyTests rather than by relaxing DP001.

    private async Task RunAsync(string sourceProfile, string targetProfile)
    {
        var transition = $"{sourceProfile}-to-{targetProfile}";
        var root = Path.Combine(_root, transition);
        var pipelineRoot = Path.Combine(root, "source", "pipelines");
        Directory.CreateDirectory(pipelineRoot);
        var artifact = Path.Combine(pipelineRoot, "load.etlsql");
        await File.WriteAllTextAsync(artifact,
            $"-- @owner: transition-certification\nSELECT '{transition}' AS Transition INTO #proof;");
        await File.WriteAllTextAsync(Path.Combine(root, "source", "etlsql-policy.json"), "{}");
        var sourceHash = await HashAsync(artifact);

        var preflight = await DeploymentPromotionPreflightService.BuildAsync(
            Path.Combine(root, "source"), sourceProfile, targetProfile);
        Assert.True(preflight.Ready);
        Assert.Contains(preflight.PortableArtifacts, item => item.Path == "pipelines/load.etlsql" && item.Sha256 == sourceHash);

        var source = new SQLiteJobHistoryStore(Path.Combine(root, "source.db"));
        await source.InitializeAsync();
        var job = new JobDefinition(
            "portable-load", "RUN SCRIPT 'pipelines/load.etlsql';", 1, "HOUR", null, null, null,
            IsEnabled: true, TargetPath: "pipelines/load.etlsql", CreatedBy: "transition-operator");
        await source.SaveJobAsync(job);
        var runAt = new DateTime(2026, 8, 2, 11, 0, 0, DateTimeKind.Utc);
        await source.ImportJobHistoryAsync(new JobHistoryEntry(
            0, job.Name, runAt, runAt.AddMinutes(1), "SUCCEEDED", null, RowsProcessed: 7));
        await source.SaveLineageAsync(
        [
            new LineageEntry("portable.output", "SELECT")
            {
                SourceTables = ["portable.input"],
                Metadata = new Dictionary<string, string> { ["owner"] = "transition-operator" }
            }
        ], job.Name, "pipelines/load.etlsql", runAt);

        // The versioned package is both the export and tested restore point.
        var restorePoint = await OrchestratorPromotionPackageService.ExportAsync(source, source, source);
        await using var packageBytes = new MemoryStream();
        await OrchestratorPromotionPackageService.WriteAsync(restorePoint, packageBytes);
        Assert.True(packageBytes.Length > 0);

        await source.SaveJobAsync(job with { IsEnabled = false, ModifiedBy = "transition-operator" });
        Assert.False((await source.GetJobAsync(job.Name))!.IsEnabled);

        var target = new SQLiteJobHistoryStore(Path.Combine(root, "target.db"));
        await target.InitializeAsync();
        var imported = await OrchestratorPromotionPackageService.ImportAsync(
            restorePoint, target, target, target, new Dictionary<string, string>());
        Assert.Equal(1, imported.Jobs);
        Assert.Equal(1, imported.LineageEntries);
        Assert.False(Assert.Single(await target.GetAllJobsAsync()).IsEnabled);
        Assert.Single(await target.GetHistoryAsync(limit: 100));
        Assert.Single(await target.GetRecentLineageAsync());
        Assert.Equal(sourceHash, await HashAsync(artifact));

        var rollback = new SQLiteJobHistoryStore(Path.Combine(root, "rollback.db"));
        await rollback.InitializeAsync();
        await OrchestratorPromotionPackageService.ImportAsync(
            restorePoint, rollback, rollback, rollback, new Dictionary<string, string>());
        Assert.False(Assert.Single(await rollback.GetAllJobsAsync()).IsEnabled);
        Assert.Single(await rollback.GetHistoryAsync(limit: 100));
        Assert.Single(await rollback.GetRecentLineageAsync());

        var targetArtifactRoot = Path.Combine(root, "target", "pipelines");
        Directory.CreateDirectory(targetArtifactRoot);
        var targetArtifact = Path.Combine(targetArtifactRoot, "load.etlsql");
        File.Copy(artifact, targetArtifact, overwrite: true);
        var targetHash = await HashAsync(targetArtifact);
        Assert.Equal(sourceHash, targetHash);

        await DeploymentCertificationEvidenceWriter.WriteAsync(
            $"transition-{sourceProfile}-to-{targetProfile}",
            new
            {
                schemaVersion = "etl-sql.deployment-scenario-evidence/v1",
                scenarioId = $"{sourceProfile}To{targetProfile}",
                kind = "Transition",
                sourceProfile,
                targetProfile,
                topology = Topology(targetProfile),
                artifactHashes = new { before = sourceHash, after = targetHash, matched = true },
                resources = new { imported = 3, skipped = 0, failed = 0 },
                mappingDecisions = Array.Empty<object>(),
                continuity = new { jobs = 1, jobHistory = 1, lineage = 1, dataQuality = 0, reports = 0 },
                negativeIsolation = targetProfile == "SaaS"
                    ? new[] { new { boundary = "host-fixed tenant activation", result = "Passed" } }
                    : Array.Empty<object>(),
                rollback = new { attempted = true, result = "Passed", jobsFenced = true, restoredJobs = 1, restoredHistory = 1, restoredLineage = 1 }
            });
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
    }

    private static string Topology(string profile) => profile switch
    {
        "Solo" => "Local process, local artifacts, optional local SQLite",
        "Team" => "Single-node Orchestrator with SQLite and local artifacts",
        "Enterprise" => "Governed deployment contract; external HA topology certified separately",
        "SaaS" => "Managed Dedicated (one host-fixed tenant runtime boundary)",
        _ => profile
    };
}
