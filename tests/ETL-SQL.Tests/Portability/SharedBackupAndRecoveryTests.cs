using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Core.Quality;
using ETL_SQL.Orchestrator.Storage;
using Xunit;

namespace ETL_SQL.Tests.Portability;

public sealed class SharedBackupAndRecoveryTests : IDisposable
{
    private readonly string _sharedDbPath = Path.Combine(
        Path.GetTempPath(), $"etlsql-shared-backup-{Guid.NewGuid():N}.db");
    private readonly string _targetDbPath = Path.Combine(
        Path.GetTempPath(), $"etlsql-target-restore-{Guid.NewGuid():N}.db");

    private RelationalJobHistoryStore SharedStore => new(
        new SqliteOrchestratorDialect($"Data Source={_sharedDbPath}"));

    private RelationalJobHistoryStore TargetStore => new(
        new SqliteOrchestratorDialect($"Data Source={_targetDbPath}"));

    [Fact]
    public async Task TenantScopedExportFromSharedStore_ExtractsOnlyTargetTenantRows()
    {
        var store = SharedStore;
        var alpha = TenantContext.FromVerifiedCredential("tenant-alpha");
        var beta = TenantContext.FromVerifiedCredential("tenant-beta");

        // Seed tenant-alpha
        await store.SaveJobAsync(new JobDefinition(
            "alpha-job-1", "SELECT 1;", 1, "HOUR", null, null, null, IsEnabled: true, TenantId: "tenant-alpha"));
        await store.SaveJobAsync(new JobDefinition(
            "alpha-job-2", "SELECT 2;", 1, "DAY", null, null, null, IsEnabled: false, TenantId: "tenant-alpha"));
        await store.SaveScheduleAsync(new ScheduleDefinition(
            "alpha-sched-1", "0 * * * *", "UTC", TenantId: "tenant-alpha"));
        await store.SaveNotificationAsync(new NotificationDefinition(
            "alpha-notify-1", "conn-alpha", "ops@alpha.test", TenantId: "tenant-alpha"));

        var alphaJob1 = (await store.GetJobAsync("tenant-alpha", "alpha-job-1"))!;
        var alphaSched1 = (await store.GetScheduleAsync("tenant-alpha", "alpha-sched-1"))!;
        var alphaNotify1 = (await store.GetNotificationAsync("tenant-alpha", "alpha-notify-1"))!;
        await store.AddJobScheduleAsync(alphaJob1.Id, alphaSched1.Id, DateTime.UtcNow);
        await store.AddJobNotificationAsync(alphaJob1.Id, alphaNotify1.Id, NotificationTrigger.Failure);

        var alphaHistoryId = await store.LogJobStartAsync(alphaJob1.Id);
        await store.LogJobEndAsync(alphaHistoryId, "SUCCESS");
        await store.SaveJobDataQualityFailuresAsync(alphaHistoryId,
        [
            new DataQualityRuleFailureMetric("customers", "email", "MATCHES '^[^@]+@[^@]+$'", "WARN", 3, "data-eng")
        ]);

        await store.SaveLineageAsync(alpha,
        [
            new LineageEntry("customers", "INSERT")
            {
                TargetColumn = "email",
                SourceTables = ["raw_customers"],
                Metadata = new Dictionary<string, string> { ["tier"] = "gold" }
            }
        ], "alpha-job-1", "alpha.etlsql", DateTime.UtcNow);

        // Seed tenant-beta
        await store.SaveJobAsync(new JobDefinition(
            "beta-job-1", "SELECT 100;", 1, "HOUR", null, null, null, IsEnabled: true, TenantId: "tenant-beta"));
        await store.SaveScheduleAsync(new ScheduleDefinition(
            "beta-sched-1", "*/5 * * * *", "UTC", TenantId: "tenant-beta"));
        await store.SaveNotificationAsync(new NotificationDefinition(
            "beta-notify-1", "conn-beta", "#beta-ops", TenantId: "tenant-beta"));

        var betaJob1 = (await store.GetJobAsync("tenant-beta", "beta-job-1"))!;
        var betaHistoryId = await store.LogJobStartAsync(betaJob1.Id);
        await store.LogJobEndAsync(betaHistoryId, "SUCCESS");

        // Export ONLY tenant-alpha
        var package = await OrchestratorPromotionPackageService.ExportAsync(
            alpha, store, store, store);

        // Assert strictly alpha items, zero beta leakage
        Assert.Equal(2, package.Jobs.Count);
        Assert.All(package.Jobs, j => Assert.Equal("tenant-alpha", j.TenantId));
        Assert.DoesNotContain(package.Jobs, j => j.Name == "beta-job-1");

        Assert.Single(package.Schedules);
        Assert.Equal("alpha-sched-1", package.Schedules[0].Name);
        Assert.Equal("tenant-alpha", package.Schedules[0].TenantId);

        Assert.Single(package.Notifications);
        Assert.Equal("alpha-notify-1", package.Notifications[0].Name);
        Assert.Equal("tenant-alpha", package.Notifications[0].TenantId);

        Assert.Single(package.JobSchedules);
        Assert.Single(package.JobNotifications);

        Assert.Single(package.QualityHistory);
        Assert.Equal("alpha-job-1", package.QualityHistory[0].JobName);

        Assert.Single(package.QualityFailures);
        Assert.Equal("email", package.QualityFailures[0].ColumnName);

        Assert.Single(package.LineageAndTags);
        Assert.Equal("customers", package.LineageAndTags[0].TargetTable);
    }

    [Fact]
    public async Task TenantScopedRestoreIntoSharedStore_PreservesForeignTenantsAndRestoresTarget()
    {
        var sourceStore = SharedStore;
        var targetStore = TargetStore;
        var alpha = TenantContext.FromVerifiedCredential("tenant-alpha");

        // Prepare alpha package
        await sourceStore.SaveJobAsync(new JobDefinition(
            "migrated-job", "SELECT 42;", 1, "DAY", null, null, null, IsEnabled: true, TenantId: "tenant-alpha"));
        await sourceStore.SaveScheduleAsync(new ScheduleDefinition(
            "migrated-sched", "0 0 * * *", "UTC", TenantId: "tenant-alpha"));
        var alphaJob = (await sourceStore.GetJobAsync("tenant-alpha", "migrated-job"))!;
        var alphaSched = (await sourceStore.GetScheduleAsync("tenant-alpha", "migrated-sched"))!;
        await sourceStore.AddJobScheduleAsync(alphaJob.Id, alphaSched.Id, DateTime.UtcNow);

        var package = await OrchestratorPromotionPackageService.ExportAsync(
            alpha, sourceStore, sourceStore, sourceStore);

        // Target store has pre-existing tenant-gamma data
        await targetStore.SaveJobAsync(new JobDefinition(
            "gamma-job", "SELECT 99;", 1, "HOUR", null, null, null, IsEnabled: true, TenantId: "tenant-gamma"));
        await targetStore.SaveScheduleAsync(new ScheduleDefinition(
            "gamma-sched", "*/10 * * * *", "UTC", TenantId: "tenant-gamma"));

        // Restore alpha package into target shared store
        var result = await OrchestratorPromotionPackageService.ImportAsync(
            package, targetStore, targetStore, targetStore);

        Assert.Equal(1, result.Jobs);
        Assert.Equal(1, result.Schedules);
        Assert.Equal(1, result.JobSchedules);

        // Verify gamma data untouched
        var gammaJob = await targetStore.GetJobAsync("tenant-gamma", "gamma-job");
        Assert.NotNull(gammaJob);
        Assert.True(gammaJob.IsEnabled);

        // Verify alpha data restored
        var restoredJob = await targetStore.GetJobAsync("tenant-alpha", "migrated-job");
        Assert.NotNull(restoredJob);
        Assert.False(restoredJob.IsEnabled); // Imported jobs default to disabled until activated
        var restoredSched = await targetStore.GetScheduleAsync("tenant-alpha", "migrated-sched");
        Assert.NotNull(restoredSched);

        var restoredLinks = (await targetStore.GetJobSchedulesAsync())
            .Where(l => l.JobName == "migrated-job")
            .ToList();
        Assert.Single(restoredLinks);
    }

    [Fact]
    public async Task ReplayAndCacheRollup_NeverIntroducesOrLeaksCrossTenantRows()
    {
        var store = SharedStore;
        var alpha = TenantContext.FromVerifiedCredential("tenant-alpha");
        var beta = TenantContext.FromVerifiedCredential("tenant-beta");

        await store.SaveJobAsync(new JobDefinition(
            "job-alpha", "SELECT 1;", 1, "DAY", null, null, null, IsEnabled: true, TenantId: "tenant-alpha"));
        await store.SaveJobAsync(new JobDefinition(
            "job-beta", "SELECT 2;", 1, "DAY", null, null, null, IsEnabled: true, TenantId: "tenant-beta"));

        var alphaJob = (await store.GetJobAsync("tenant-alpha", "job-alpha"))!;
        var betaJob = (await store.GetJobAsync("tenant-beta", "job-beta"))!;

        // Run multiple executions
        for (int i = 0; i < 3; i++)
        {
            var hAlpha = await store.LogJobStartAsync(alphaJob.Id);
            await store.LogJobEndAsync(hAlpha, "SUCCESS");

            var hBeta = await store.LogJobStartAsync(betaJob.Id);
            await store.LogJobEndAsync(hBeta, "SUCCESS");
        }

        // Trigger rollups
        await store.RollUpJobHistoryAsync();

        // Query history per-tenant
        var alphaHistory = (await store.GetHistoryForNameAsync("tenant-alpha", "job-alpha")).ToList();
        var betaHistory = (await store.GetHistoryForNameAsync("tenant-beta", "job-beta")).ToList();

        Assert.Equal(3, alphaHistory.Count);
        Assert.Equal(3, betaHistory.Count);
        Assert.All(alphaHistory, h => Assert.Equal("job-alpha", h.JobName));
        Assert.All(betaHistory, h => Assert.Equal("job-beta", h.JobName));

        // Daily rollup checks
        var alphaDaily = await store.GetJobHistoryDailyAsync(alphaJob.Id, DateTime.UtcNow.AddDays(-1));
        var betaDaily = await store.GetJobHistoryDailyAsync(betaJob.Id, DateTime.UtcNow.AddDays(-1));

        Assert.All(alphaDaily, d => Assert.Equal("job-alpha", d.JobName));
        Assert.All(betaDaily, d => Assert.Equal("job-beta", d.JobName));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_sharedDbPath)) File.Delete(_sharedDbPath);
        if (File.Exists(_targetDbPath)) File.Delete(_targetDbPath);
    }
}
