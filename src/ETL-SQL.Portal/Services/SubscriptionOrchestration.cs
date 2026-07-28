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
            // Keep the catalog object and attachment in place, but do not let the Orchestrator's
            // generic job-notification dispatcher send a second, attachment-free email while the
            // Portal delivery executor still owns report export, RLS, attachments, and the ledger.
            IsEnabled: false,
            DisplayName: $"Subscription {sub.Id}",
            Description: "Portal subscription delivery destination");
    }

    public static string ScriptFileName(int subscriptionId, string reportName) =>
        $"sub_{subscriptionId}_{SanitizeName(reportName)}.etlsql";

    /// <summary>Builds the scheduled trigger-job definition for a subscription from row state.</summary>
    public static JobDefinition BuildJobDefinition(Subscription sub, string reportName, string scriptPath)
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
            RetryDelaySeconds: 60);
    }

    public static async Task SaveJobAndScheduleAsync(
        IJobHistoryStore store,
        Subscription sub,
        string reportName,
        string scriptPath)
    {
        var job = BuildJobDefinition(sub, reportName, scriptPath);
        await store.SaveJobAsync(job);
        await SaveScheduleLinkAsync(store, sub, job.Name);
        await SaveNotificationLinkAsync(store, sub, job.Name);
    }

    public static async Task SaveScheduleLinkAsync(
        IJobHistoryStore store,
        Subscription sub,
        string jobName,
        bool rearmExisting = false,
        DateTimeOffset? asOf = null)
    {
        if (store is not IJobCatalogStore catalog)
            return;

        var schedule = BuildScheduleDefinition(sub);
        await catalog.SaveScheduleAsync(schedule);
        var added = await JobScheduleAttachment.AttachAsync(catalog, jobName, schedule.Name, asOf);
        if (!added && rearmExisting)
        {
            var nextRun = CronSchedule.GetNextOccurrence(
                schedule.Cron,
                schedule.TimeZone,
                asOf ?? DateTimeOffset.UtcNow);
            await catalog.ArmJobScheduleAsync(jobName, schedule.Name, nextRun);
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
        string jobName)
    {
        if (store is not IJobCatalogStore catalog)
            return;

        var notificationName = NotificationName(sub.Id);
        var notification = BuildNotificationDefinition(sub);
        if (notification is null)
        {
            _ = await catalog.RemoveJobNotificationAsync(
                jobName, notificationName, NotificationTrigger.Success);
            _ = await catalog.DeleteNotificationAsync(notificationName);
            return;
        }

        await catalog.SaveNotificationAsync(notification);
        await catalog.AddJobNotificationAsync(jobName, notification.Name, NotificationTrigger.Success);
    }

    public static async Task DeleteNotificationIfUnusedAsync(IJobHistoryStore store, int subscriptionId)
    {
        if (store is not IJobCatalogStore catalog)
            return;

        _ = await catalog.DeleteNotificationAsync(NotificationName(subscriptionId));
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
