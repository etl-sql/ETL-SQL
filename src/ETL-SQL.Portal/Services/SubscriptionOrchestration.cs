using System.Globalization;
using ETL_SQL.Core.Data;
using ETL_SQL.Engine.Scheduling;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Portal.Data;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Shared naming, scheduling, and job-definition rules for subscription Orchestrator jobs (P1.2).
/// The subscription row is the source of truth: the controller, poller, delivery-status service,
/// and startup reconciliation all derive job names and definitions from here, so a crash between
/// the portal DB, the job DB, and the script file can always be healed back toward the row.
/// </summary>
public static class SubscriptionOrchestration
{
    public const string JobNamePrefix = "SUB:";
    public const string ScheduleNamePrefix = "SUBSCHED:";
    public const string NotificationNamePrefix = "SUBNOTIFY:";

    public static string JobName(int subscriptionId, string? reportName) =>
        $"{JobNamePrefix}{subscriptionId}:{reportName}";

    public static string ScheduleName(int subscriptionId) =>
        $"{ScheduleNamePrefix}{subscriptionId}";

    public static string NotificationName(int subscriptionId) =>
        $"{NotificationNamePrefix}{subscriptionId}";

    public static bool TryParseSubscriptionId(string jobName, out int subscriptionId)
    {
        subscriptionId = 0;
        if (!jobName.StartsWith(JobNamePrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var separator = jobName.IndexOf(':', JobNamePrefix.Length);
        var idText = separator < 0
            ? jobName.AsSpan(JobNamePrefix.Length)
            : jobName.AsSpan(JobNamePrefix.Length, separator - JobNamePrefix.Length);
        return int.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out subscriptionId)
            && subscriptionId > 0;
    }

    public static (int Interval, string Unit) ParseSchedule(string? schedule) =>
        schedule?.ToUpperInvariant() switch
        {
            "HOURLY" => (1, "HOUR"),
            "DAILY" => (1, "DAY"),
            "WEEKLY" => (1, "WEEK"),
            "MONTHLY" => (1, "MONTH"),
            _ => (0, "DAY")
        };

    public static string ToCron(string? schedule, string? atTime)
    {
        var (hour, minute) = ParseAtTime(atTime);
        return schedule?.ToUpperInvariant() switch
        {
            "HOURLY" => $"{minute} * * * *",
            "DAILY" => $"{minute} {hour} * * *",
            "WEEKLY" => $"{minute} {hour} * * 1",
            "MONTHLY" => $"{minute} {hour} 1 * *",
            _ => throw new ArgumentException(
                "Invalid schedule. Use Daily, Weekly, Monthly, or Hourly.", nameof(schedule))
        };
    }

    public static ScheduleDefinition BuildScheduleDefinition(Subscription sub)
    {
        var cron = ToCron(sub.Schedule, sub.AtTime);
        CronSchedule.Validate(cron, CronSchedule.DefaultTimeZone);
        return new ScheduleDefinition(
            Name: ScheduleName(sub.Id),
            Cron: cron,
            TimeZone: CronSchedule.DefaultTimeZone,
            IsEnabled: sub.IsActive,
            DisplayName: $"Subscription {sub.Id}",
            Description: "Portal subscription trigger schedule");
    }

    public static NotificationDefinition? BuildNotificationDefinition(Subscription sub)
    {
        if (string.IsNullOrWhiteSpace(sub.SmtpAlias))
            return null;

        return new NotificationDefinition(
            Name: NotificationName(sub.Id),
            ConnectionName: sub.SmtpAlias.Trim(),
            Recipient: sub.Recipients,
            // Keep the catalog object in place, but do not let the Orchestrator's generic
            // job-success dispatcher send a second, attachment-free email. Portal subscription
            // delivery explicitly dispatches SourceKind=SUBSCRIPTION after export, RLS, and ledger
            // claim work finishes; that explicit dispatch path bypasses this disabled flag.
            IsEnabled: false,
            DisplayName: $"Subscription {sub.Id}",
            Description: "Portal subscription delivery destination");
    }

    public static string ScriptFileName(int subscriptionId, string reportName) =>
        $"sub_{subscriptionId}_{SanitizeName(reportName)}.etlsql";

    /// <summary>Builds the scheduled trigger-job definition for a subscription from row state.</summary>
    public static JobDefinition BuildJobDefinition(
        Subscription sub,
        string reportName,
        string scriptPath,
        string tenantId)
    {
        var (interval, unit) = ParseSchedule(sub.Schedule);
        return new JobDefinition(
            Name: JobName(sub.Id, reportName),
            Script: $"RUN SCRIPT '{scriptPath.Replace("\\", "\\\\")}';",
            Interval: interval > 0 ? interval : 1,
            Unit: interval > 0 ? unit : "DAY",
            AtTime: sub.AtTime,
            LastRun: null,
            NextRun: null,
            IsEnabled: sub.IsActive,
            MaxRetries: 3,
            RetryDelaySeconds: 60,
            TenantId: ETL_SQL.Core.Multitenancy.TenantId.FromTrustedSource(tenantId).Value);
    }

    public static async Task SaveJobAndScheduleAsync(
        IJobHistoryStore store,
        Subscription sub,
        string reportName,
        string scriptPath,
        string tenantId)
    {
        var job = BuildJobDefinition(sub, reportName, scriptPath, tenantId);
        await store.SaveJobAsync(job);
        // Re-read to pick up the identity the store assigned: links hang off the id, not the name.
        var saved = await store.GetJobAsync(tenantId, job.Name);
        if (saved?.Id is not { Length: > 0 }) return;
        await SaveScheduleLinkAsync(store, sub, saved.Id, tenantId);
        await SaveNotificationLinkAsync(store, sub, saved.Id, tenantId);
    }

    public static async Task SaveScheduleLinkAsync(
        IJobHistoryStore store,
        Subscription sub,
        string jobId,
        string? tenantId,
        bool rearmExisting = false,
        DateTimeOffset? asOf = null)
    {
        if (store is not IJobCatalogStore catalog)
            return;

        var schedule = BuildScheduleDefinition(sub) with { TenantId = tenantId };
        await catalog.SaveScheduleAsync(schedule);
        var added = await JobScheduleAttachment.AttachAsync(catalog, jobId, tenantId, schedule.Name, asOf);
        if (!added && rearmExisting)
        {
            var nextRun = CronSchedule.GetNextOccurrence(
                schedule.Cron,
                schedule.TimeZone,
                asOf ?? DateTimeOffset.UtcNow);
            var saved = await catalog.GetScheduleAsync(tenantId, schedule.Name);
            if (saved?.Id is { Length: > 0 } savedScheduleId)
                await catalog.ArmJobScheduleAsync(jobId, savedScheduleId, nextRun);
        }
    }

    public static async Task DeleteScheduleIfUnusedAsync(IJobHistoryStore store, int subscriptionId)
    {
        if (store is not IJobCatalogStore catalog)
            return;

        _ = await catalog.DeleteScheduleAsync(ScheduleName(subscriptionId));
    }

    public static async Task SaveNotificationLinkAsync(
        IJobHistoryStore store,
        Subscription sub,
        string jobId,
        string? tenantId)
    {
        if (store is not IJobCatalogStore catalog)
            return;

        var notificationName = NotificationName(sub.Id);
        var notification = BuildNotificationDefinition(sub);
        if (notification is null)
        {
            // Detach and drop the destination this subscription owns — resolved in its own tenant, so
            // another tenant's subscription notification of the same name is untouched.
            var stale = await catalog.GetNotificationAsync(tenantId, notificationName);
            if (stale?.Id is { Length: > 0 } staleId)
            {
                _ = await catalog.RemoveJobNotificationAsync(jobId, staleId, NotificationTrigger.Success);
                _ = await catalog.DeleteNotificationAsync(staleId);
            }
            return;
        }

        await catalog.SaveNotificationAsync(notification with { TenantId = tenantId });
        var saved = await catalog.GetNotificationAsync(tenantId, notification.Name);
        if (saved?.Id is { Length: > 0 } savedId)
            await catalog.AddJobNotificationAsync(jobId, savedId, NotificationTrigger.Success);
    }

    public static async Task DeleteNotificationIfUnusedAsync(
        IJobHistoryStore store, int subscriptionId, string? tenantId)
    {
        if (store is not IJobCatalogStore catalog)
            return;

        var notification = await catalog.GetNotificationAsync(tenantId, NotificationName(subscriptionId));
        if (notification?.Id is { Length: > 0 } notificationId)
            _ = await catalog.DeleteNotificationAsync(notificationId);
    }

    public static string SanitizeName(string name) =>
        new(name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    private static (int Hour, int Minute) ParseAtTime(string? atTime)
    {
        if (string.IsNullOrWhiteSpace(atTime))
            return (0, 0);

        if (TimeSpan.TryParse(atTime, CultureInfo.InvariantCulture, out var parsed)
            && parsed >= TimeSpan.Zero
            && parsed < TimeSpan.FromDays(1))
        {
            return (parsed.Hours, parsed.Minutes);
        }

        throw new ArgumentException(
            $"'{atTime}' is not a valid delivery time. Use HH:mm in 24-hour time.", nameof(atTime));
    }
}
