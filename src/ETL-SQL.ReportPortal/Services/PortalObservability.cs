using System.Diagnostics;

namespace ETL_SQL.ReportPortal.Services;

public static class PortalObservability
{
    public const string ServiceName = "ETL-SQL.ReportPortal";
    public const string ActivitySourceName = "ETL-SQL.ReportPortal";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public static class Tags
    {
        public const string Environment = "etlsql.environment";
        public const string Node = "etlsql.node";
        public const string Component = "etlsql.component";
        public const string JobId = "etlsql.job.id";
        public const string ReportId = "etlsql.report.id";
        public const string UserId = "etlsql.user.id";
        public const string WorkloadKind = "etlsql.workload.kind";
        public const string ExecutionMode = "etlsql.execution.mode";
        public const string Status = "etlsql.status";
        public const string RowsProcessed = "etlsql.rows_processed";
        public const string PeakMemoryBytes = "etlsql.peak_memory_bytes";
        public const string CpuTimeSeconds = "etlsql.cpu_time_seconds";
        public const string ScriptHash = "etlsql.script_hash";
        public const string CorrelationId = "etlsql.correlation_id";
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
