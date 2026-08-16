using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Tests;

/// <summary>
/// Name-addressed conveniences for tests that set a job up by name and then act on it.
///
/// <para>Production resolves a name to a <see cref="JobId"/> exactly once, at the request boundary,
/// and everything below that addresses the object by identity. These helpers are that same single
/// resolution in the unbound (Solo) tenant scope, so a test reads the way it did before surrogate
/// identity without the store's typed surface having to accept a name again.</para>
///
/// <para>They throw when the name resolves to nothing. A test that meant to address a real object
/// and silently addressed none is the exact failure <see cref="JobId"/> exists to make loud, and a
/// helper that quietly passed <see cref="JobId.None"/> would hand it back.</para>
/// </summary>
internal static class JobStoreNameAddressingTestExtensions
{
    public static async Task<JobId> JobIdOfAsync(this IJobHistoryStore store, string jobName) =>
        (await store.GetJobAsync(null, jobName))?.Id
        ?? throw new InvalidOperationException(
            $"This test expected a saved job named '{jobName}'. Save the definition before addressing it.");

    public static async Task<ScheduleId> ScheduleIdOfAsync(this IJobCatalogStore catalog, string scheduleName) =>
        (await catalog.GetScheduleAsync(null, scheduleName))?.Id
        ?? throw new InvalidOperationException(
            $"This test expected a saved schedule named '{scheduleName}'.");

    public static async Task<NotificationId> NotificationIdOfAsync(
        this IJobCatalogStore catalog, string notificationName) =>
        (await catalog.GetNotificationAsync(null, notificationName))?.Id
        ?? throw new InvalidOperationException(
            $"This test expected a saved notification named '{notificationName}'.");

    // Name lookups in the unbound (Solo) scope, which is the scope every non-tenant test runs in.
    // Spelled out here so a test does not have to write `null` for a tenant on every line and risk
    // it reading as "any tenant" — it is one specific scope.

    public static Task<JobDefinition?> GetJobAsync(this IJobHistoryStore store, string name) =>
        store.GetJobAsync(null, name);

    public static Task<ScheduleDefinition?> GetScheduleAsync(this IJobCatalogStore catalog, string name) =>
        catalog.GetScheduleAsync(null, name);

    public static Task<NotificationDefinition?> GetNotificationAsync(this IJobCatalogStore catalog, string name) =>
        catalog.GetNotificationAsync(null, name);

    // ── History and state ─────────────────────────────────────────────────────

    /// <summary>
    /// Records a run of <paramref name="jobName"/>, registering a placeholder definition first when
    /// the test has not saved one. Recording a run <em>is</em> the assertion that the job exists, and
    /// most callers here care about the run's history, metrics, or retention rather than the job's
    /// shape; a test that cares about the definition saves its own, and this finds it.
    ///
    /// <para>This is the one helper that creates rather than resolves. It is confined to test setup
    /// because production has no such case: a run always originates from a job the scheduler already
    /// loaded, and a script run with no job goes to <see cref="IJobHistoryStore.LogAdHocRunStartAsync"/>.</para>
    /// </summary>
    public static async Task<long> LogJobStartAsync(this IJobHistoryStore store, string jobName)
    {
        if (await store.GetJobAsync(null, jobName) is null)
        {
            // Refuse when the name already belongs to a tenant. Creating an unbound twin would give
            // the test two objects of one name and quietly answer every later question about the
            // wrong one — which is the confusion this whole change exists to end. A tenant-scoped
            // test resolves its own job and passes the identity.
            if ((await store.GetAllJobsAsync()).Any(
                    job => job.Name.Equals(jobName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(
                    $"A job named '{jobName}' exists in a tenant, so this unbound helper will not " +
                    "create another. Resolve it with GetJobAsync(tenantId, name) and pass its Id.");

            await store.SaveJobAsync(new JobDefinition(
                jobName, "SELECT 1;", 1, "HOUR", null, null, null));
        }
        return await store.LogJobStartAsync(await store.JobIdOfAsync(jobName));
    }

    /// <summary>
    /// Runs recorded under a job <em>name</em>. Deliberately maps to
    /// <see cref="IJobHistoryStore.GetHistoryForNameAsync"/> rather than resolving an identity:
    /// history outlives the job, so a test that drops a job and then reads its runs must still see
    /// them, and an ad-hoc run has no identity to resolve at all.
    /// </summary>
    public static Task<IEnumerable<JobHistoryEntry>> GetHistoryAsync(
        this IJobHistoryStore store, string jobName, int limit = 100) =>
        store.GetHistoryForNameAsync(null, jobName, limit);

    public static async Task<string?> GetJobStateAsync(
        this IJobHistoryStore store, string jobName, string key) =>
        await store.GetJobStateAsync(await store.JobIdOfAsync(jobName), key);

    public static async Task SetJobStateAsync(
        this IJobHistoryStore store, string jobName, string key, string? value) =>
        await store.SetJobStateAsync(await store.JobIdOfAsync(jobName), key, value);

    public static async Task<IReadOnlyList<JobStateEntry>> GetJobStatesAsync(
        this IJobHistoryStore store, string jobName, int limit = 1000) =>
        await store.GetJobStatesAsync(await store.JobIdOfAsync(jobName), limit);

    public static async Task<IReadOnlyList<JobHistoryDailySummary>> GetJobHistoryDailyAsync(
        this IJobHistoryStore store, string jobName, DateTime sinceDay, int limit = 1000) =>
        await store.GetJobHistoryDailyAsync(await store.JobIdOfAsync(jobName), sinceDay, limit);

    public static async Task DeleteJobAsync(this IJobHistoryStore store, string jobName) =>
        await store.DeleteJobAsync(await store.JobIdOfAsync(jobName));

    public static async Task UpdateJobLastRunAsync(
        this IJobHistoryStore store, string jobName, DateTime lastRun, DateTime? nextRun) =>
        await store.UpdateJobLastRunAsync(await store.JobIdOfAsync(jobName), lastRun, nextRun);

    // ── Leases and fencing ────────────────────────────────────────────────────

    public static async Task<bool> TryAcquireJobLeaseAsync(
        this IJobHistoryStore store, string jobName, string owner, TimeSpan duration) =>
        await store.TryAcquireJobLeaseAsync(await store.JobIdOfAsync(jobName), owner, duration);

    public static async Task<bool> TryRenewJobLeaseAsync(
        this IJobHistoryStore store, string jobName, string owner, TimeSpan duration) =>
        await store.TryRenewJobLeaseAsync(await store.JobIdOfAsync(jobName), owner, duration);

    public static async Task ReleaseJobLeaseAsync(
        this IJobHistoryStore store, string jobName, string owner) =>
        await store.ReleaseJobLeaseAsync(await store.JobIdOfAsync(jobName), owner);

    public static async Task<long?> AcquireJobLeaseAsync(
        this IJobHistoryStore store, string jobName, string owner, TimeSpan duration) =>
        await store.AcquireJobLeaseAsync(await store.JobIdOfAsync(jobName), owner, duration);

    public static async Task<bool> ValidateFenceTokenAsync(
        this IJobHistoryStore store, string jobName, long fenceToken) =>
        await store.ValidateFenceTokenAsync(await store.JobIdOfAsync(jobName), fenceToken);

    public static async Task<bool> TryUpdateJobLastRunFencedAsync(
        this IJobHistoryStore store, string jobName, DateTime lastRun, DateTime? nextRun, long fenceToken) =>
        await store.TryUpdateJobLastRunFencedAsync(
            await store.JobIdOfAsync(jobName), lastRun, nextRun, fenceToken);

    // ── Catalog attachments ───────────────────────────────────────────────────

    public static async Task<bool> AddJobScheduleAsync(
        this IJobCatalogStore catalog, string jobName, string scheduleName, DateTime? nextRun) =>
        await catalog.AddJobScheduleAsync(
            await RequireJobStore(catalog).JobIdOfAsync(jobName),
            await catalog.ScheduleIdOfAsync(scheduleName),
            nextRun);

    public static async Task<bool> RemoveJobScheduleAsync(
        this IJobCatalogStore catalog, string jobName, string scheduleName) =>
        await catalog.RemoveJobScheduleAsync(
            await RequireJobStore(catalog).JobIdOfAsync(jobName),
            await catalog.ScheduleIdOfAsync(scheduleName));

    public static async Task<IReadOnlyList<JobScheduleLink>> GetJobSchedulesAsync(
        this IJobCatalogStore catalog, string jobName) =>
        await catalog.GetJobSchedulesAsync(await RequireJobStore(catalog).JobIdOfAsync(jobName));

    public static async Task UpdateJobScheduleRunAsync(
        this IJobCatalogStore catalog, string jobName, string scheduleName,
        DateTime lastRun, DateTime? nextRun) =>
        await catalog.UpdateJobScheduleRunAsync(
            await RequireJobStore(catalog).JobIdOfAsync(jobName),
            await catalog.ScheduleIdOfAsync(scheduleName),
            lastRun,
            nextRun);

    public static async Task<bool> AddJobNotificationAsync(
        this IJobCatalogStore catalog, string jobName, string notificationName, NotificationTrigger trigger) =>
        await catalog.AddJobNotificationAsync(
            await RequireJobStore(catalog).JobIdOfAsync(jobName),
            await catalog.NotificationIdOfAsync(notificationName),
            trigger);

    public static async Task<bool> RemoveJobNotificationAsync(
        this IJobCatalogStore catalog, string jobName, string notificationName, NotificationTrigger trigger) =>
        await catalog.RemoveJobNotificationAsync(
            await RequireJobStore(catalog).JobIdOfAsync(jobName),
            await catalog.NotificationIdOfAsync(notificationName),
            trigger);

    public static async Task<IReadOnlyList<JobNotificationLink>> GetJobNotificationsAsync(
        this IJobCatalogStore catalog, string jobName) =>
        await catalog.GetJobNotificationsAsync(await RequireJobStore(catalog).JobIdOfAsync(jobName));

    public static async Task<bool> SetScheduleEnabledAsync(
        this IJobCatalogStore catalog, string scheduleName, bool isEnabled) =>
        await catalog.SetScheduleEnabledAsync(await catalog.ScheduleIdOfAsync(scheduleName), isEnabled);

    public static async Task<bool> SetNotificationEnabledAsync(
        this IJobCatalogStore catalog, string notificationName, bool isEnabled) =>
        await catalog.SetNotificationEnabledAsync(
            await catalog.NotificationIdOfAsync(notificationName), isEnabled);

    public static async Task<IReadOnlyList<string>> DeleteScheduleAsync(
        this IJobCatalogStore catalog, string scheduleName) =>
        await catalog.DeleteScheduleAsync(await catalog.ScheduleIdOfAsync(scheduleName));

    public static async Task<IReadOnlyList<string>> DeleteNotificationAsync(
        this IJobCatalogStore catalog, string notificationName) =>
        await catalog.DeleteNotificationAsync(await catalog.NotificationIdOfAsync(notificationName));

    private static IJobHistoryStore RequireJobStore(IJobCatalogStore catalog) =>
        catalog as IJobHistoryStore
        ?? throw new InvalidOperationException(
            "Resolving a job name needs the job store; this catalog does not implement IJobHistoryStore.");
}
