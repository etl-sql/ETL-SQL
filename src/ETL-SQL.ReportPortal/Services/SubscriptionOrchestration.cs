using System.Globalization;
using ETL_SQL.Core.Data;
using ETL_SQL.ReportPortal.Data;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// Shared naming, scheduling, and job-definition rules for subscription Orchestrator jobs (P1.2).
/// The subscription row is the source of truth: the controller, poller, delivery-status service,
/// and startup reconciliation all derive job names and definitions from here, so a crash between
/// the portal DB, the job DB, and the script file can always be healed back toward the row.
/// </summary>
public static class SubscriptionOrchestration
{
    public const string JobNamePrefix = "SUB:";

    public static string JobName(int subscriptionId, string? reportName) =>
        $"{JobNamePrefix}{subscriptionId}:{reportName}";

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

    public static string SanitizeName(string name) =>
        new(name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
}
