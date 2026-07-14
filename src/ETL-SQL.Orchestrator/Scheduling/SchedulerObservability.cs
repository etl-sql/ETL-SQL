using System.Diagnostics;
using System.Diagnostics.Metrics;
using ETL_SQL.Core.Observability;

namespace ETL_SQL.Orchestrator.Scheduling;

internal static class SchedulerObservability
{
    public const string ActivitySourceName = "ETL-SQL.Orchestrator";
    public const string MeterName = "ETL-SQL.Orchestrator";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> JobCompletedCounter =
        Meter.CreateCounter<long>("etlsql.orchestrator.scheduled_job.completed");
    private static readonly Histogram<double> JobDurationMs =
        Meter.CreateHistogram<double>("etlsql.orchestrator.scheduled_job.duration_ms");
    private static readonly Histogram<long> JobRowsProcessed =
        Meter.CreateHistogram<long>("etlsql.orchestrator.scheduled_job.rows_processed");
    private static readonly Histogram<long> JobPeakMemoryBytes =
        Meter.CreateHistogram<long>("etlsql.orchestrator.scheduled_job.peak_memory_bytes");
    private static readonly Histogram<double> JobCpuTimeSeconds =
        Meter.CreateHistogram<double>("etlsql.orchestrator.scheduled_job.cpu_time_seconds");
    private static readonly Histogram<long> JobQueueWaitMs =
        Meter.CreateHistogram<long>("etlsql.orchestrator.scheduled_job.queue_wait_ms");
    private static readonly Histogram<long> JobAttempts =
        Meter.CreateHistogram<long>("etlsql.orchestrator.scheduled_job.attempts");

    public static Activity? StartScheduledJobActivity(long historyId, string scriptHash, int attempt)
    {
        var activity = ActivitySource.StartActivity("orchestrator.scheduled_job", ActivityKind.Internal);
        if (activity is null)
            return null;

        activity.SetTag(ObservabilityConventions.Tags.Environment, Environment.GetEnvironmentVariable("ETLSQL_ENV") ?? "default");
        activity.SetTag(ObservabilityConventions.Tags.Node, Environment.MachineName);
        activity.SetTag(ObservabilityConventions.Tags.Component, "orchestrator");
        activity.SetTag(ObservabilityConventions.Tags.ExecutionMode, "orchestrator");
        activity.SetTag(ObservabilityConventions.Tags.WorkloadKind, "scheduled");
        activity.SetTag(ObservabilityConventions.Tags.ScriptHash, scriptHash);
        if (historyId > 0)
            activity.SetTag(ObservabilityConventions.Tags.JobId, historyId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (attempt > 0)
            activity.SetTag(ObservabilityConventions.Tags.JobAttempt, attempt);
        return activity;
    }

    public static void CompleteScheduledJobActivity(Activity? activity, string status, long durationMs,
        long rowsProcessed, long peakMemoryBytes, double cpuTimeSeconds, long queueWaitMs = 0, int attempt = 0)
    {
        if (activity is not null)
        {
            activity.SetTag(ObservabilityConventions.Tags.Status, status);
            activity.SetTag(ObservabilityConventions.Tags.RowsProcessed, rowsProcessed);
            activity.SetTag(ObservabilityConventions.Tags.PeakMemoryBytes, peakMemoryBytes);
            activity.SetTag(ObservabilityConventions.Tags.CpuTimeSeconds, cpuTimeSeconds);
            activity.SetTag(ObservabilityConventions.Tags.QueueWaitMs, Math.Max(0, queueWaitMs));
            activity.SetStatus(status is "SUCCESS" ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
        }

        var tags = new TagList
        {
            { ObservabilityConventions.Tags.Environment, Environment.GetEnvironmentVariable("ETLSQL_ENV") ?? "default" },
            { ObservabilityConventions.Tags.Node, Environment.MachineName },
            { ObservabilityConventions.Tags.Component, "orchestrator" },
            { ObservabilityConventions.Tags.ExecutionMode, "orchestrator" },
            { ObservabilityConventions.Tags.WorkloadKind, "scheduled" },
            { ObservabilityConventions.Tags.Status, status }
        };

        JobCompletedCounter.Add(1, tags);
        JobDurationMs.Record(Math.Max(0, durationMs), tags);
        JobRowsProcessed.Record(Math.Max(0, rowsProcessed), tags);
        JobPeakMemoryBytes.Record(Math.Max(0, peakMemoryBytes), tags);
        JobCpuTimeSeconds.Record(Math.Max(0, cpuTimeSeconds), tags);
        JobQueueWaitMs.Record(Math.Max(0, queueWaitMs), tags);
        JobAttempts.Record(Math.Max(0, attempt), tags);
    }
}
