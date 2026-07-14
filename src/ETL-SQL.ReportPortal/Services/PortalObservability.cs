using System.Diagnostics;
using ETL_SQL.Core.Observability;

namespace ETL_SQL.ReportPortal.Services;

public static class PortalObservability
{
    public const string ServiceName = "ETL-SQL.ReportPortal";
    public const string ActivitySourceName = "ETL-SQL.ReportPortal";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

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

    public static void CompleteExecutionJobActivity(Activity? activity, ExecutionJob job, string? scriptHash = null)
    {
        if (activity is null)
            return;

        activity.SetTag(Tags.Status, job.Status.ToString());
        activity.SetTag(Tags.RowsProcessed, job.RowsProcessed);
        activity.SetTag(Tags.PeakMemoryBytes, job.PeakMemoryBytes);
        activity.SetTag(Tags.CpuTimeSeconds, job.CpuTimeSeconds);
        if (!string.IsNullOrWhiteSpace(scriptHash))
            activity.SetTag(Tags.ScriptHash, scriptHash);

        activity.SetStatus(job.Status == JobStatus.Failed
            ? ActivityStatusCode.Error
            : ActivityStatusCode.Ok);
    }
}
