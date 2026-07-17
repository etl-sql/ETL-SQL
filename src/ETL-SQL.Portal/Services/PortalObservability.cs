using System.Diagnostics;
using System.Diagnostics.Metrics;
using ETL_SQL.Core.Observability;

namespace ETL_SQL.Portal.Services;

public static class PortalObservability
{
    public const string ServiceName = "ETL-SQL.Portal";
    public const string ActivitySourceName = "ETL-SQL.Portal";
    public const string MeterName = "ETL-SQL.Portal";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> ExecutionCompletedCounter =
        Meter.CreateCounter<long>("etlsql.portal.execution.completed");
    private static readonly Histogram<double> ExecutionDurationMs =
        Meter.CreateHistogram<double>("etlsql.portal.execution.duration_ms");
    private static readonly Histogram<long> ExecutionRowsProcessed =
        Meter.CreateHistogram<long>("etlsql.portal.execution.rows_processed");
    private static readonly Histogram<long> ExecutionPeakMemoryBytes =
        Meter.CreateHistogram<long>("etlsql.portal.execution.peak_memory_bytes");
    private static readonly Histogram<double> ExecutionCpuTimeSeconds =
        Meter.CreateHistogram<double>("etlsql.portal.execution.cpu_time_seconds");

    public static class Tags
    {
        public const string Environment = ObservabilityConventions.Tags.Environment;
        public const string Node = ObservabilityConventions.Tags.Node;
        public const string Component = ObservabilityConventions.Tags.Component;
        public const string JobId = ObservabilityConventions.Tags.JobId;
        public const string ReportId = ObservabilityConventions.Tags.ReportId;
        public const string UserId = "etlsql.user.id";
        public const string WorkloadKind = ObservabilityConventions.Tags.WorkloadKind;
        public const string ExecutionMode = ObservabilityConventions.Tags.ExecutionMode;
        public const string Status = ObservabilityConventions.Tags.Status;
        public const string RowsProcessed = ObservabilityConventions.Tags.RowsProcessed;
        public const string PeakMemoryBytes = ObservabilityConventions.Tags.PeakMemoryBytes;
        public const string CpuTimeSeconds = ObservabilityConventions.Tags.CpuTimeSeconds;
        public const string ScriptHash = ObservabilityConventions.Tags.ScriptHash;
        public const string CorrelationId = ObservabilityConventions.Tags.CorrelationId;
    }

    public static Activity? StartExecutionJobActivity(ExecutionJob job, string workloadKind)
    {
        var activity = ActivitySource.StartActivity("portal.execution_job", ActivityKind.Internal);
        if (activity is null)
            return null;

        activity.SetTag(Tags.Environment, Environment.GetEnvironmentVariable("ETLSQL_ENV") ?? "default");
        activity.SetTag(Tags.Node, Environment.MachineName);
        activity.SetTag(Tags.Component, "portal");
        activity.SetTag(Tags.JobId, job.Id);
        activity.SetTag(Tags.ReportId, job.ReportId);
        activity.SetTag(Tags.UserId, job.UserId);
        activity.SetTag(Tags.WorkloadKind, workloadKind);
        activity.SetTag(Tags.ExecutionMode, job.TrustedDatasetExecution ? "trusted-dataset" : "portal");
        if (!string.IsNullOrWhiteSpace(job.CorrelationId))
            activity.SetTag(Tags.CorrelationId, job.CorrelationId);
        return activity;
    }

    public static void CompleteExecutionJobActivity(
        Activity? activity,
        ExecutionJob job,
        string? workloadKind = null,
        string? scriptHash = null)
    {
        var status = job.Status.ToString();
        if (activity is not null)
        {
            activity.SetTag(Tags.Status, status);
            activity.SetTag(Tags.RowsProcessed, job.RowsProcessed);
            activity.SetTag(Tags.PeakMemoryBytes, job.PeakMemoryBytes);
            activity.SetTag(Tags.CpuTimeSeconds, job.CpuTimeSeconds);
            if (!string.IsNullOrWhiteSpace(scriptHash))
                activity.SetTag(Tags.ScriptHash, scriptHash);

            activity.SetStatus(job.Status == JobStatus.Failed
                ? ActivityStatusCode.Error
                : ActivityStatusCode.Ok);
        }

        var tags = new TagList
        {
            { Tags.Environment, Environment.GetEnvironmentVariable("ETLSQL_ENV") ?? "default" },
            { Tags.Node, Environment.MachineName },
            { Tags.Component, "portal" },
            { Tags.Status, status },
            { Tags.ExecutionMode, job.TrustedDatasetExecution ? "trusted-dataset" : "portal" }
        };
        if (!string.IsNullOrWhiteSpace(workloadKind))
            tags.Add(Tags.WorkloadKind, workloadKind);

        ExecutionCompletedCounter.Add(1, tags);
        if (job.StartedAt is not null && job.CompletedAt is not null)
            ExecutionDurationMs.Record(Math.Max(0, (job.CompletedAt.Value - job.StartedAt.Value).TotalMilliseconds), tags);
        ExecutionRowsProcessed.Record(Math.Max(0, job.RowsProcessed), tags);
        ExecutionPeakMemoryBytes.Record(Math.Max(0, job.PeakMemoryBytes), tags);
        ExecutionCpuTimeSeconds.Record(Math.Max(0, job.CpuTimeSeconds), tags);
    }
}
