using System.Text.RegularExpressions;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using DatabaseProvider = ETL_SQL.Common.DatabaseProvider;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Startup reconciliation for subscriptions (P0.1 + P1.2). The subscription row is the source of
/// truth; this converges the other two resources toward it so a crash anywhere in the
/// create/update/delete sequence is healed at the next startup:
/// <list type="bullet">
/// <item>generated job scripts are (re)written to the credential-free trigger form — including
/// pre-upgrade scripts that embedded decrypted SMTP credentials — and a missing
/// <c>ScriptPath</c> is regenerated;</item>
/// <item>generated scripts and abandoned atomic-write temp files that no longer belong to any
/// subscription are removed;</item>
/// <item>Orchestrator jobs are aligned to the row: jobs for deleted subscriptions are removed,
/// missing jobs are recreated from row state, stale-named duplicates are dropped, and
/// schedule/enablement drift is corrected.</item>
/// </list>
/// </summary>
public static class SubscriptionScriptMaintenance
{
    public const string ClusterLockName = "portal-subscription-reconciliation";
    private static readonly TimeSpan ClusterLockTtl = TimeSpan.FromMinutes(10);

    private static readonly Regex GeneratedFileName =
        new(@"^sub_(\d+)_.*\.etlsql$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task ReconcileAsync(
        PortalDbContext db,
        PortalConfig config,
        string? orchestratorDbPath,
        ILogger logger,
        IOrchestratorStoreFactory? storeFactory = null,
        IClusterLockStore? clusterLockStore = null,
        string? clusterLockOwner = null)
    {
        if (clusterLockStore is not null)
        {
            var owner = string.IsNullOrWhiteSpace(clusterLockOwner)
                ? $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}"
                : clusterLockOwner;
            if (!await clusterLockStore.TryAcquireLockAsync(ClusterLockName, owner, ClusterLockTtl))
            {
                logger.LogInformation(
                    "Subscription reconciliation skipped because another Portal node owns cluster lock {LockName}.",
                    ClusterLockName);
                return;
            }
        }

        var subscriptions = await db.Subscriptions
            .Include(s => s.Report)
            .ToListAsync();
        var liveIds = subscriptions.Select(s => s.Id).ToHashSet();

        await ReconcileScriptsAsync(db, config, subscriptions, logger);
        CleanSubscriptionDirectory(config, liveIds, logger);
        await ReconcileOrchestratorJobsAsync(subscriptions, liveIds, orchestratorDbPath, logger, storeFactory);
    }

    // ── Scripts: trigger form, atomic writes, healed ScriptPath ──────────────────

    private static async Task ReconcileScriptsAsync(
        PortalDbContext db,
        PortalConfig config,
        IReadOnlyList<Subscription> subscriptions,
        ILogger logger)
    {
        var rewritten = 0;
        var healedPaths = 0;
        foreach (var sub in subscriptions)
        {
            string resolved;
            if (string.IsNullOrWhiteSpace(sub.ScriptPath))
            {
                // Crash between the row insert and the script write: regenerate from the row.
                if (sub.Report is null || sub.Report.IsDeleted)
                    continue;

                var fileName = SubscriptionOrchestration.ScriptFileName(sub.Id, sub.Report.Name);
                if (!PortalPathGuard.TryResolveScript(
                        config, Path.Combine("subscriptions", fileName), out resolved))
                    continue;
                healedPaths++;
            }
            else if (!PortalPathGuard.TryResolveScript(config, sub.ScriptPath, out resolved))
            {
                logger.LogWarning(
                    "Subscription {SubscriptionId} script path is outside the script root and was not reconciled: {Path}",
                    sub.Id, sub.ScriptPath);
                continue;
            }

            try
            {
                var trigger = SubscriptionTriggerScript.Compose(sub.Id);
                if (!File.Exists(resolved)
                    || !string.Equals(await File.ReadAllTextAsync(resolved), trigger, StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(resolved)!);
                    SubscriptionTriggerScript.Write(resolved, sub.Id);
                    rewritten++;
                }

                if (!string.Equals(sub.ScriptPath, resolved, StringComparison.Ordinal))
                    sub.ScriptPath = resolved;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Could not reconcile subscription {SubscriptionId} job script: {Path}", sub.Id, resolved);
            }
        }

        await db.SaveChangesAsync();

        if (rewritten > 0)
            logger.LogWarning(
                "Rewrote {Count} subscription job script(s) to the credential-free trigger form.", rewritten);
        if (healedPaths > 0)
            logger.LogWarning(
                "Regenerated {Count} missing subscription script path(s) from row state.", healedPaths);
    }

    private static void CleanSubscriptionDirectory(PortalConfig config, HashSet<int> liveIds, ILogger logger)
    {
        if (!PortalPathGuard.TryResolveScript(config, "subscriptions", out var subscriptionDir)
            || !Directory.Exists(subscriptionDir))
            return;

        // Abandoned atomic-write temp files from a crash mid-write.
        foreach (var path in Directory.EnumerateFiles(subscriptionDir, "*.tmp-*", SearchOption.TopDirectoryOnly))
            TryDelete(path, logger, "abandoned subscription script temp file");

        // A generated script whose subscription no longer exists may still carry pre-upgrade
        // credentials — remove it. Only files matching the generated naming pattern are touched.
        foreach (var path in Directory.EnumerateFiles(subscriptionDir, "*.etlsql", SearchOption.TopDirectoryOnly))
        {
            var match = GeneratedFileName.Match(Path.GetFileName(path));
            if (!match.Success
                || !int.TryParse(match.Groups[1].Value, out var id)
                || liveIds.Contains(id))
                continue;

            TryDelete(path, logger, "orphaned generated subscription script");
        }
    }

    // ── Orchestrator jobs: converge to row state ─────────────────────────────────

    private static async Task ReconcileOrchestratorJobsAsync(
        IReadOnlyList<Subscription> subscriptions,
        HashSet<int> liveIds,
        string? orchestratorDbPath,
        ILogger logger,
        IOrchestratorStoreFactory? storeFactory)
    {
        if ((storeFactory is null || storeFactory.Provider == DatabaseProvider.Sqlite)
            && (orchestratorDbPath is null || !File.Exists(orchestratorDbPath)))
        {
            logger.LogDebug("Subscription job reconciliation skipped — Orchestrator DB unavailable.");
            return;
        }

        IJobHistoryStore store;
        List<JobDefinition> subscriptionJobs;
        try
        {
            // Provider-aware when a factory is supplied (production); falls back to SQLite for
            // callers that don't inject one (tests run against SQLite fixtures).
            store = storeFactory?.Create(orchestratorDbPath) ?? new SQLiteJobHistoryStore(orchestratorDbPath);
            await store.InitializeAsync();
            subscriptionJobs = (await store.GetAllJobsAsync())
                .Where(j => j.Name.StartsWith(
                    SubscriptionOrchestration.JobNamePrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Subscription job reconciliation skipped — Orchestrator DB unreadable.");
            return;
        }

        var jobsBySubscription = new Dictionary<int, List<JobDefinition>>();
        foreach (var job in subscriptionJobs)
        {
            // A job whose subscription row no longer exists must not keep firing.
            if (!SubscriptionOrchestration.TryParseSubscriptionId(job.Name, out var subId)
                || !liveIds.Contains(subId))
            {
                await TryDeleteJob(store, job.Name, logger, "orphaned subscription job");
                if (subId > 0)
                {
                    await TryDeleteSchedule(store, subId, logger, "orphaned subscription schedule");
                    await TryDeleteNotification(store, subId, logger, "orphaned subscription notification");
                }
                continue;
            }

            (jobsBySubscription.TryGetValue(subId, out var list)
                ? list
                : jobsBySubscription[subId] = []).Add(job);
        }

        foreach (var sub in subscriptions)
        {
            if (sub.Report is null || sub.Report.IsDeleted || string.IsNullOrWhiteSpace(sub.ScriptPath))
                continue;

            var desired = SubscriptionOrchestration.BuildJobDefinition(sub, sub.Report.Name, sub.ScriptPath);
            jobsBySubscription.TryGetValue(sub.Id, out var existing);
            var current = existing?.FirstOrDefault(j =>
                string.Equals(j.Name, desired.Name, StringComparison.OrdinalIgnoreCase));

            // Stale-named duplicates (e.g. the report was renamed) are removed.
            foreach (var stale in existing ?? [])
            {
                if (!ReferenceEquals(stale, current))
                    await TryDeleteJob(store, stale.Name, logger, "stale-named subscription job");
            }

            try
            {
                if (current is null)
                {
                    // Crash between the portal row and the job DB: recreate from the row.
                    // NextRun starts null, so the healed occurrence runs at the next scheduler
                    // pass (at-least-once recovery, consistent with the P1.1 lease semantics).
                    await SubscriptionOrchestration.SaveJobAndScheduleAsync(
                        store, sub, sub.Report.Name, sub.ScriptPath);
                    logger.LogWarning(
                        "Recreated missing Orchestrator job for subscription {SubscriptionId}.", sub.Id);
                }
                else
                {
                    var scheduleChanged = current.Interval != desired.Interval
                        || !string.Equals(current.Unit, desired.Unit, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(current.AtTime, desired.AtTime, StringComparison.Ordinal);
                    var enabledChanged = current.IsEnabled != desired.IsEnabled;
                    var scriptChanged = !string.Equals(current.Script, desired.Script, StringComparison.Ordinal);
                    if (!scheduleChanged && !enabledChanged && !scriptChanged)
                    {
                        await SubscriptionOrchestration.SaveScheduleLinkAsync(store, sub, desired.Name);
                        await SubscriptionOrchestration.SaveNotificationLinkAsync(store, sub, desired.Name);
                        continue;
                    }

                    // Drift: converge schedule/enablement/script to the row while preserving
                    // the job's run bookkeeping and configured delivery time.
                    await store.SaveJobAsync(current with
                    {
                        Interval = desired.Interval,
                        Unit = desired.Unit,
                        IsEnabled = desired.IsEnabled,
                        Script = desired.Script,
                        AtTime = desired.AtTime
                    });
                    await SubscriptionOrchestration.SaveScheduleLinkAsync(
                        store,
                        sub,
                        desired.Name,
                        rearmExisting: scheduleChanged);
                    await SubscriptionOrchestration.SaveNotificationLinkAsync(store, sub, desired.Name);
                    logger.LogInformation(
                        "Realigned Orchestrator job for subscription {SubscriptionId} with the portal row.", sub.Id);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Could not reconcile the Orchestrator job for subscription {SubscriptionId}.", sub.Id);
            }
        }
    }

    private static void TryDelete(string path, ILogger logger, string description)
    {
        try
        {
            File.Delete(path);
            logger.LogInformation("Removed {Description}: {Path}", description, path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not remove {Description}: {Path}", description, path);
        }
    }

    private static async Task TryDeleteJob(
        IJobHistoryStore store, string jobName, ILogger logger, string description)
    {
        try
        {
            await store.DeleteJobAsync(jobName);
            logger.LogWarning("Removed {Description}: {JobName}", description, jobName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not remove {Description}: {JobName}", description, jobName);
        }
    }

    private static async Task TryDeleteSchedule(
        IJobHistoryStore store, int subscriptionId, ILogger logger, string description)
    {
        try
        {
            await SubscriptionOrchestration.DeleteScheduleIfUnusedAsync(store, subscriptionId);
            logger.LogWarning(
                "Removed {Description}: {ScheduleName}",
                description,
                SubscriptionOrchestration.ScheduleName(subscriptionId));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not remove {Description}: {ScheduleName}",
                description,
                SubscriptionOrchestration.ScheduleName(subscriptionId));
        }
    }

    private static async Task TryDeleteNotification(
        IJobHistoryStore store, int subscriptionId, ILogger logger, string description)
    {
        try
        {
            await SubscriptionOrchestration.DeleteNotificationIfUnusedAsync(store, subscriptionId);
            logger.LogWarning(
                "Removed {Description}: {NotificationName}",
                description,
                SubscriptionOrchestration.NotificationName(subscriptionId));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not remove {Description}: {NotificationName}",
                description,
                SubscriptionOrchestration.NotificationName(subscriptionId));
        }
    }
}
