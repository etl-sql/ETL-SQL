using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// P1.2 — the subscription row is the source of truth; startup reconciliation converges the
/// generated script and the Orchestrator job to it, so a crash anywhere in the
/// create/update/delete sequence heals at the next startup.
/// </summary>
[Trait("Category", "Portal")]
public class SubscriptionLifecycleRecoveryTests
{
    [Fact]
    public async Task Reconcile_ConvergesScriptsAndOrchestratorJobsToRowState()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<PortalConfig>();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var owner = new PortalUser
        {
            UserName = $"recovery-owner-{suffix}",
            Email = $"recovery-owner-{suffix}@test.local",
            IsActive = true
        };
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        var folder = new Folder
        {
            Name = $"Recovery Folder {suffix}",
            Path = $"/recovery-{suffix}",
            OwnerId = owner.Id
        };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();

        var report = new Report
        {
            FolderId = folder.Id,
            Name = $"RecoveryReport{suffix}",
            ScriptPath = Path.Combine(config.ScriptRootPath, $"recovery-{suffix}.rptsql"),
            CreatedBy = owner.Id
        };
        db.Reports.Add(report);
        await db.SaveChangesAsync();

        Subscription NewSub(string schedule, bool isActive, string? atTime = null) => new()
        {
            ReportId = report.Id,
            UserId = owner.Id,
            Schedule = schedule,
            AtTime = atTime,
            Format = SubscriptionFormat.CSV,
            SmtpAlias = "alias",
            Recipients = "r@test.local",
            IsActive = isActive
        };

        // Crash artifact 1: row exists but the script write and job registration never happened.
        var missingEverything = NewSub("Daily", isActive: true, atTime: "08:30");
        // Crash artifact 2: row updated (Weekly, paused) but the job kept the old schedule and
        // stayed enabled; the job also carries run bookkeeping that must survive realignment.
        var drifted = NewSub("Weekly", isActive: false);
        db.Subscriptions.AddRange(missingEverything, drifted);
        await db.SaveChangesAsync();

        var subscriptionDir = Path.Combine(config.ScriptRootPath, "subscriptions");
        Directory.CreateDirectory(subscriptionDir);

        var driftedScript = Path.Combine(
            subscriptionDir, SubscriptionOrchestration.ScriptFileName(drifted.Id, report.Name));
        SubscriptionTriggerScript.Write(driftedScript, drifted.Id);
        drifted.ScriptPath = driftedScript;
        await db.SaveChangesAsync();

        // Crash artifact 3: an abandoned atomic-write temp file.
        var abandonedTmp = driftedScript + ".tmp-deadbeef";
        await File.WriteAllTextAsync(abandonedTmp, "partial write");

        // Orchestrator job DB with: a job for a deleted subscription, a stale-named duplicate
        // for the drifted subscription, and the drifted job itself with wrong schedule/enable.
        var orchDbPath = Path.Combine(factory.TempDir, $"recovery-orch-{suffix}.db");
        var store = new SQLiteJobHistoryStore(orchDbPath);
        await store.InitializeAsync();

        var orphanName = SubscriptionOrchestration.JobName(999_999, "DeletedReport");
        await store.SaveJobAsync(new JobDefinition(
            orphanName, "RUN SCRIPT 'ghost';", 1, "DAY", null, null, null, true));

        var staleName = SubscriptionOrchestration.JobName(drifted.Id, "OldReportName");
        await store.SaveJobAsync(new JobDefinition(
            staleName, "RUN SCRIPT 'stale';", 1, "DAY", null, null, null, true));

        var lastRun = DateTime.Now.AddHours(-2);
        await store.SaveJobAsync(new JobDefinition(
            SubscriptionOrchestration.JobName(drifted.Id, report.Name),
            $"RUN SCRIPT '{driftedScript.Replace("\\", "\\\\")}';",
            1, "DAY", "07:00", lastRun, null, IsEnabled: true));

        await SubscriptionScriptMaintenance.ReconcileAsync(db, config, orchDbPath, NullLogger.Instance);

        // Healed: ScriptPath regenerated and the trigger script written.
        await db.Entry(missingEverything).ReloadAsync();
        Assert.False(string.IsNullOrWhiteSpace(missingEverything.ScriptPath));
        Assert.Equal(
            SubscriptionTriggerScript.Compose(missingEverything.Id),
            await File.ReadAllTextAsync(missingEverything.ScriptPath!));

        var jobs = (await store.GetAllJobsAsync()).ToList();
        var catalog = (IJobCatalogStore)store;

        // Healed: a job recreated from row state, including the persisted delivery time.
        var recreated = Assert.Single(jobs, j =>
            j.Name == SubscriptionOrchestration.JobName(missingEverything.Id, report.Name));
        Assert.Equal(1, recreated.Interval);
        Assert.Equal("DAY", recreated.Unit);
        Assert.Equal("08:30", recreated.AtTime);
        Assert.True(recreated.IsEnabled);
        Assert.Contains(missingEverything.ScriptPath!.Replace("\\", "\\\\"), recreated.Script);
        var recreatedSchedule = await catalog.GetScheduleAsync(
            SubscriptionOrchestration.ScheduleName(missingEverything.Id));
        Assert.NotNull(recreatedSchedule);
        Assert.Equal("30 8 * * *", recreatedSchedule!.Cron);
        Assert.True(recreatedSchedule.IsEnabled);
        Assert.Contains(await catalog.GetJobSchedulesAsync(recreated.Name),
            link => link.ScheduleName == recreatedSchedule.Name && link.NextRun is not null);
        var recreatedNotification = await catalog.GetNotificationAsync(
            SubscriptionOrchestration.NotificationName(missingEverything.Id));
        Assert.NotNull(recreatedNotification);
        Assert.Equal("alias", recreatedNotification!.ConnectionName);
        Assert.Equal("r@test.local", recreatedNotification.Recipient);
        Assert.False(recreatedNotification.IsEnabled);
        Assert.Contains(await catalog.GetJobNotificationsAsync(recreated.Name),
            link => link.NotificationName == recreatedNotification.Name
                && link.Trigger == NotificationTrigger.Success);

        // Converged: schedule and enablement follow the row; run bookkeeping survives.
        var realigned = Assert.Single(jobs, j =>
            j.Name == SubscriptionOrchestration.JobName(drifted.Id, report.Name));
        Assert.Equal(1, realigned.Interval);
        Assert.Equal("WEEK", realigned.Unit);
        Assert.False(realigned.IsEnabled);
        Assert.Equal(lastRun, realigned.LastRun);
        var realignedSchedule = await catalog.GetScheduleAsync(
            SubscriptionOrchestration.ScheduleName(drifted.Id));
        Assert.NotNull(realignedSchedule);
        Assert.Equal("0 0 * * 1", realignedSchedule!.Cron);
        Assert.False(realignedSchedule.IsEnabled);
        Assert.Contains(await catalog.GetJobSchedulesAsync(realigned.Name),
            link => link.ScheduleName == realignedSchedule.Name && link.NextRun is not null);
        var realignedNotification = await catalog.GetNotificationAsync(
            SubscriptionOrchestration.NotificationName(drifted.Id));
        Assert.NotNull(realignedNotification);
        Assert.Equal("alias", realignedNotification!.ConnectionName);
        Assert.Equal("r@test.local", realignedNotification.Recipient);
        Assert.False(realignedNotification.IsEnabled);
        Assert.Contains(await catalog.GetJobNotificationsAsync(realigned.Name),
            link => link.NotificationName == realignedNotification.Name
                && link.Trigger == NotificationTrigger.Success);

        // Removed: the orphaned job, the stale-named duplicate, and the abandoned temp file.
        Assert.DoesNotContain(jobs, j => j.Name == orphanName);
        Assert.DoesNotContain(jobs, j => j.Name == staleName);
        Assert.Null(await catalog.GetScheduleAsync(SubscriptionOrchestration.ScheduleName(999_999)));
        Assert.Null(await catalog.GetNotificationAsync(SubscriptionOrchestration.NotificationName(999_999)));
        Assert.False(File.Exists(abandonedTmp));

        // Idempotent: a second pass changes nothing.
        var scriptWriteTime = File.GetLastWriteTimeUtc(missingEverything.ScriptPath!);
        await SubscriptionScriptMaintenance.ReconcileAsync(db, config, orchDbPath, NullLogger.Instance);
        Assert.Equal(scriptWriteTime, File.GetLastWriteTimeUtc(missingEverything.ScriptPath!));
        Assert.Equal(jobs.Count, (await store.GetAllJobsAsync()).Count());
    }

    [Fact]
    public async Task Create_PersistsAtTime_SoAHealedJobKeepsItsDeliveryTime()
    {
        using var factory = new PortalWebFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

        // The entity carries AtTime so reconciliation can rebuild a lost job without losing
        // the configured wall-clock delivery time (it is not recoverable from anywhere else).
        var entity = db.Model.FindEntityType(typeof(Subscription));
        Assert.NotNull(entity);
        Assert.NotNull(entity!.FindProperty(nameof(Subscription.AtTime)));
    }

    [Fact]
    public async Task Reconcile_SkipsWhenAnotherPortalNodeOwnsClusterLock()
    {
        using var factory = new PortalWebFactory();
        using var scope = factory.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<PortalConfig>();
        var locks = scope.ServiceProvider.GetRequiredService<IClusterLockStore>();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var owner = new PortalUser
        {
            UserName = $"locked-owner-{suffix}",
            Email = $"locked-owner-{suffix}@test.local",
            IsActive = true
        };
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        var folder = new Folder
        {
            Name = $"Locked Folder {suffix}",
            Path = $"/locked-{suffix}",
            OwnerId = owner.Id
        };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();

        var report = new Report
        {
            FolderId = folder.Id,
            Name = $"LockedReport{suffix}",
            ScriptPath = Path.Combine(config.ScriptRootPath, $"locked-{suffix}.rptsql"),
            CreatedBy = owner.Id
        };
        db.Reports.Add(report);
        await db.SaveChangesAsync();

        var subscription = new Subscription
        {
            ReportId = report.Id,
            UserId = owner.Id,
            Schedule = "Daily",
            Format = SubscriptionFormat.CSV,
            SmtpAlias = "alias",
            Recipients = "r@test.local",
            IsActive = true
        };
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var startupHolder = await locks.GetLockHolderAsync(SubscriptionScriptMaintenance.ClusterLockName);
        if (startupHolder is not null)
            await locks.ReleaseLockAsync(SubscriptionScriptMaintenance.ClusterLockName, startupHolder);

        Assert.True(await locks.TryAcquireLockAsync(
            SubscriptionScriptMaintenance.ClusterLockName,
            "other-node",
            TimeSpan.FromMinutes(10)));

        await SubscriptionScriptMaintenance.ReconcileAsync(
            db,
            config,
            null,
            NullLogger.Instance,
            clusterLockStore: locks,
            clusterLockOwner: "this-node");

        await db.Entry(subscription).ReloadAsync();
        Assert.Null(subscription.ScriptPath);
    }
}
