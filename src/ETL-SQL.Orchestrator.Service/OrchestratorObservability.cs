using System.Diagnostics;
using System.Diagnostics.Metrics;
using ETL_SQL.Core.Observability;
using ETL_SQL.Orchestrator.Channels;

namespace ETL_SQL.Orchestrator.Service;

internal static class OrchestratorObservability
{
    public const string ServiceName = "ETL-SQL.Orchestrator.Service";
    public const string ActivitySourceName = "ETL-SQL.Orchestrator.Service";
    public const string MeterName = "ETL-SQL.Orchestrator.Service";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> JobCompletedCounter =
        Meter.CreateCounter<long>("etlsql.orchestrator.job.completed");
    private static readonly Histogram<double> JobDurationMs =
        Meter.CreateHistogram<double>("etlsql.orchestrator.job.duration_ms");
    private static readonly Histogram<long> JobRowsProcessed =
        Meter.CreateHistogram<long>("etlsql.orchestrator.job.rows_processed");
    private static readonly Histogram<long> JobPeakMemoryBytes =
        Meter.CreateHistogram<long>("etlsql.orchestrator.job.peak_memory_bytes");
    private static readonly Histogram<double> JobCpuTimeSeconds =
        Meter.CreateHistogram<double>("etlsql.orchestrator.job.cpu_time_seconds");

    public static Activity? StartAdHocJobActivity(string jobId, string? correlationId)
    {
        var activity = ActivitySource.StartActivity("orchestrator.job", ActivityKind.Internal);
        if (activity is null)
            return null;

        activity.SetTag(ObservabilityConventions.Tags.Environment, Environment.GetEnvironmentVariable("ETLSQL_ENV") ?? "default");
        activity.SetTag(ObservabilityConventions.Tags.Node, Environment.MachineName);
        activity.SetTag(ObservabilityConventions.Tags.Component, "orchestrator");
        activity.SetTag(ObservabilityConventions.Tags.JobId, jobId);
        activity.SetTag(ObservabilityConventions.Tags.ExecutionMode, "orchestrator");
        activity.SetTag(ObservabilityConventions.Tags.WorkloadKind, "ad-hoc");
        if (!string.IsNullOrWhiteSpace(correlationId))
            activity.SetTag(ObservabilityConventions.Tags.CorrelationId, correlationId);
        return activity;
    }

    public static void CompleteAdHocJobActivity(Activity? activity, string jobId, JobRunStatus status,
        long durationMs, long rowsProcessed, long peakMemoryBytes, double cpuTimeSeconds)
    {
        var statusText = status.ToString();
        if (activity is not null)
        {
            activity.SetTag(ObservabilityConventions.Tags.Status, statusText);
            activity.SetTag(ObservabilityConventions.Tags.RowsProcessed, rowsProcessed);
            activity.SetTag(ObservabilityConventions.Tags.PeakMemoryBytes, peakMemoryBytes);
            activity.SetTag(ObservabilityConventions.Tags.CpuTimeSeconds, cpuTimeSeconds);
            activity.SetStatus(status == JobRunStatus.Failed ? ActivityStatusCode.Error : ActivityStatusCode.Ok);
        }

        var tags = new TagList
        {
            { ObservabilityConventions.Tags.Environment, Environment.GetEnvironmentVariable("ETLSQL_ENV") ?? "default" },
            { ObservabilityConventions.Tags.Component, "orchestrator" },
            { ObservabilityConventions.Tags.ExecutionMode, "orchestrator" },
            { ObservabilityConventions.Tags.WorkloadKind, "ad-hoc" },
            { ObservabilityConventions.Tags.Status, statusText }
        };

        JobCompletedCounter.Add(1, tags);
        JobDurationMs.Record(Math.Max(0, durationMs), tags);
        JobRowsProcessed.Record(Math.Max(0, rowsProcessed), tags);
        JobPeakMemoryBytes.Record(Math.Max(0, peakMemoryBytes), tags);
        JobCpuTimeSeconds.Record(Math.Max(0, cpuTimeSeconds), tags);
    }
}
