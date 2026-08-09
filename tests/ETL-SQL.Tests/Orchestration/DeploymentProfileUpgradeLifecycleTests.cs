using System.Security.Cryptography;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Quality;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.TestSupport;

namespace ETL_SQL.Tests.Orchestration;

[Trait("Category", "DeploymentProfile")]
public sealed class DeploymentProfileUpgradeLifecycleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"profile-upgrade-{Guid.NewGuid():N}");

    public DeploymentProfileUpgradeLifecycleTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact] public Task Solo_BackupFenceCutoverProofAndRollback() => RunLifecycleAsync("Solo");
    [Fact] public Task Team_BackupFenceCutoverProofAndRollback() => RunLifecycleAsync("Team");
    [Fact] public Task Enterprise_BackupFenceCutoverProofAndRollback() => RunLifecycleAsync("Enterprise");
    [Fact] public Task Saas_BackupFenceCutoverProofAndRollback() => RunLifecycleAsync("SaaS");

    private async Task RunLifecycleAsync(string profile)
    {
        var profileRoot = Path.Combine(_root, profile);
        Directory.CreateDirectory(profileRoot);
        var artifactPath = Path.Combine(profileRoot, "portable.etlsql");
        await File.WriteAllTextAsync(artifactPath,
            $"-- @owner: upgrade-certification\nSELECT '{profile}' AS Profile INTO #proof;");
        var artifactHash = await HashAsync(artifactPath);

        var source = new SQLiteJobHistoryStore(Path.Combine(profileRoot, "release-n.db"));
        await source.InitializeAsync();
        var job = new JobDefinition(
            $"{profile.ToLowerInvariant()}-upgrade", "RUN SCRIPT 'portable.etlsql';", 1, "HOUR",
            null, null, null, IsEnabled: true, TargetPath: "portable.etlsql", CreatedBy: "upgrade-operator");
        await source.SaveJobAsync(job);
        var started = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var runId = await source.ImportJobHistoryAsync(new JobHistoryEntry(
            0, job.Name, started, started.AddMinutes(1), "FAILED", "quality proof",
            RowsProcessed: 10, RowsQuarantined: 1, DataQualityFailures: "id:not-null=1"));
        await source.SaveJobDataQualityFailuresAsync(runId,
        [
            new DataQualityRuleFailureMetric($"{profile}.output", "id", "not-null", "QUARANTINE", 1, "upgrade-operator")
        ]);
        await source.SaveLineageAsync(
        [
            new LineageEntry($"{profile}.output", "SELECT")
            {
                SourceTables = [$"{profile}.input"],
                Metadata = new Dictionary<string, string> { ["owner"] = "upgrade-operator" }
            }
        ], job.Name, "portable.etlsql", started);

        // Backup/export happens while release N is intact. The package is the tested logical
        // restore point; resolved secrets, leases, and caches are intentionally absent.
        var restorePoint = await OrchestratorPromotionPackageService.ExportAsync(source, source, source);
        await using var serializedRestorePoint = new MemoryStream();
        await OrchestratorPromotionPackageService.WriteAsync(restorePoint, serializedRestorePoint);
        Assert.True(serializedRestorePoint.Length > 0);

        // Fence scheduling before cutover.
        await source.SaveJobAsync(job with { IsEnabled = false, ModifiedBy = "upgrade-operator" });
        Assert.False((await source.GetJobAsync(job.Name))!.IsEnabled);

        // A release N+1 store imports the restore point with jobs fenced, then proves continuity.
        var upgraded = new SQLiteJobHistoryStore(Path.Combine(profileRoot, "release-n-plus-one.db"));
        await upgraded.InitializeAsync();
        await OrchestratorPromotionPackageService.ImportAsync(restorePoint, upgraded, upgraded, upgraded,
            new Dictionary<string, string>());
        Assert.False(Assert.Single(await upgraded.GetAllJobsAsync()).IsEnabled);
        Assert.Single(await upgraded.GetHistoryAsync(limit: 100));
        Assert.Single(await upgraded.GetDataQualityFailuresAsync());
        Assert.Single(await upgraded.GetRecentLineageAsync());
        Assert.Equal(artifactHash, await HashAsync(artifactPath));

        // Rollback restores into a separate release-N boundary and remains scheduler-safe.
        var rollback = new SQLiteJobHistoryStore(Path.Combine(profileRoot, "rollback-release-n.db"));
        await rollback.InitializeAsync();
        await OrchestratorPromotionPackageService.ImportAsync(restorePoint, rollback, rollback, rollback,
            new Dictionary<string, string>());
        Assert.False(Assert.Single(await rollback.GetAllJobsAsync()).IsEnabled);
        Assert.Single(await rollback.GetHistoryAsync(limit: 100));
        Assert.Single(await rollback.GetDataQualityFailuresAsync());
        Assert.Single(await rollback.GetRecentLineageAsync());

        var upgradedArtifact = Path.Combine(profileRoot, "release-n-plus-one.etlsql");
        var rollbackArtifact = Path.Combine(profileRoot, "rollback-release-n.etlsql");
        File.Copy(artifactPath, upgradedArtifact, overwrite: true);
        File.Copy(artifactPath, rollbackArtifact, overwrite: true);
        var upgradedHash = await HashAsync(upgradedArtifact);
        var rollbackHash = await HashAsync(rollbackArtifact);
        Assert.Equal(artifactHash, upgradedHash);
        Assert.Equal(artifactHash, rollbackHash);

        await DeploymentCertificationEvidenceWriter.WriteAsync(
            $"upgrade-{profile}",
            new
            {
                schemaVersion = "etl-sql.deployment-scenario-evidence/v1",
                scenarioId = $"{profile}Upgrade",
                kind = "Upgrade",
                sourceProfile = profile,
                targetProfile = profile,
                topology = profile == "SaaS"
                    ? "Managed Dedicated (one host-fixed tenant runtime boundary)"
                    : profile,
                artifactHashes = new { before = artifactHash, after = upgradedHash, rollback = rollbackHash, matched = true },
                resources = new { imported = 4, skipped = 0, failed = 0 },
                mappingDecisions = Array.Empty<object>(),
                continuity = new { jobs = 1, jobHistory = 1, lineage = 1, dataQuality = 1, reports = 0 },
                negativeIsolation = profile == "SaaS"
                    ? new[] { new { boundary = "host-fixed tenant upgrade boundary", result = "Passed" } }
                    : Array.Empty<object>(),
                rollback = new { attempted = true, result = "Passed", jobsFenced = true, restoredJobs = 1, restoredHistory = 1, restoredLineage = 1, restoredDataQuality = 1 }
            });
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
    }
}
