using System.Diagnostics;
using System.Diagnostics.Metrics;
using ETL_SQL.Core.Observability;

namespace ETL_SQL.Orchestrator.Execution;

internal static class EngineExecutionObservability
{
    public const string ActivitySourceName = "ETL-SQL.Engine";
    public const string MeterName = "ETL-SQL.Engine";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> ExecutionCompletedCounter =
        Meter.CreateCounter<long>("etlsql.engine.execution.completed");
    private static readonly Histogram<double> ExecutionDurationMs =
        Meter.CreateHistogram<double>("etlsql.engine.execution.duration_ms");
    private static readonly Histogram<long> ExecutionRowsProcessed =
        Meter.CreateHistogram<long>("etlsql.engine.execution.rows_processed");
    private static readonly Histogram<long> ExecutionPeakMemoryBytes =
        Meter.CreateHistogram<long>("etlsql.engine.execution.peak_memory_bytes");
    private static readonly Histogram<double> ExecutionCpuTimeSeconds =
        Meter.CreateHistogram<double>("etlsql.engine.execution.cpu_time_seconds");
    private static readonly Histogram<long> ExecutionSpillBytes =
        Meter.CreateHistogram<long>("etlsql.engine.execution.spill_bytes");
    private static readonly Histogram<long> ExecutionSpillReadBytes =
        Meter.CreateHistogram<long>("etlsql.engine.execution.spill_read_bytes");

    public static Activity? StartExecutionActivity(string scriptHash, string? jobName)
    {
        var activity = ActivitySource.StartActivity("engine.execution", ActivityKind.Internal);
        if (activity is null)
            return null;

        activity.SetTag(ObservabilityConventions.Tags.Environment, Environment.GetEnvironmentVariable("ETLSQL_ENV") ?? "default");
        activity.SetTag(ObservabilityConventions.Tags.Node, Environment.MachineName);
        activity.SetTag(ObservabilityConventions.Tags.Component, "engine");
        activity.SetTag(ObservabilityConventions.Tags.ExecutionMode, "engine");
        activity.SetTag(ObservabilityConventions.Tags.WorkloadKind, string.IsNullOrWhiteSpace(jobName) ? "script" : "job");
        activity.SetTag(ObservabilityConventions.Tags.ScriptHash, scriptHash);
        if (!string.IsNullOrWhiteSpace(jobName))
            activity.SetTag(ObservabilityConventions.Tags.JobId, jobName);
        return activity;
    }

    public static void CompleteExecutionActivity(Activity? activity, string status, string workloadKind,
        long durationMs, long rowsProcessed, long peakMemoryBytes, double cpuTimeSeconds,
        long spillBytes, long spillReadBytes)
    {
        if (activity is not null)
        {
            activity.SetTag(ObservabilityConventions.Tags.Status, status);
            activity.SetTag(ObservabilityConventions.Tags.RowsProcessed, rowsProcessed);
            activity.SetTag(ObservabilityConventions.Tags.PeakMemoryBytes, peakMemoryBytes);
            activity.SetTag(ObservabilityConventions.Tags.CpuTimeSeconds, cpuTimeSeconds);
            activity.SetTag(ObservabilityConventions.Tags.SpillBytes, spillBytes);
            activity.SetTag(ObservabilityConventions.Tags.SpillReadBytes, spillReadBytes);
            activity.SetStatus(status is "success" ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
        }

        var tags = new TagList
        {
            { ObservabilityConventions.Tags.Environment, Environment.GetEnvironmentVariable("ETLSQL_ENV") ?? "default" },
            { ObservabilityConventions.Tags.Component, "engine" },
            { ObservabilityConventions.Tags.ExecutionMode, "engine" },
            { ObservabilityConventions.Tags.WorkloadKind, workloadKind },
            { ObservabilityConventions.Tags.Status, status }
        };

        ExecutionCompletedCounter.Add(1, tags);
        ExecutionDurationMs.Record(Math.Max(0, durationMs), tags);
        ExecutionRowsProcessed.Record(Math.Max(0, rowsProcessed), tags);
        ExecutionPeakMemoryBytes.Record(Math.Max(0, peakMemoryBytes), tags);
        ExecutionCpuTimeSeconds.Record(Math.Max(0, cpuTimeSeconds), tags);
        ExecutionSpillBytes.Record(Math.Max(0, spillBytes), tags);
        ExecutionSpillReadBytes.Record(Math.Max(0, spillReadBytes), tags);
    }
}
