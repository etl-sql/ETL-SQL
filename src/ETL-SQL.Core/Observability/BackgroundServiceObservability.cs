using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ETL_SQL.Core.Observability;

public static class BackgroundServiceObservability
{
    public const string ActivitySourceName = "ETL-SQL.BackgroundServices";
    public const string MeterName = "ETL-SQL.BackgroundServices";
    public const string OperationTag = "etlsql.operation";
    public const string ServiceNameTag = ObservabilityConventions.Tags.ServiceName;

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> RunCompletedCounter =
        Meter.CreateCounter<long>("etlsql.background_service.run.completed");
    private static readonly Histogram<double> RunDurationMs =
        Meter.CreateHistogram<double>("etlsql.background_service.run.duration_ms");
    private static readonly Histogram<long> RunAttempts =
        Meter.CreateHistogram<long>("etlsql.background_service.run.attempts");

    public static Activity? StartRun(string component, string serviceName, string operation)
    {
        var activity = ActivitySource.StartActivity("background_service.run", ActivityKind.Internal);
        if (activity is null)
            return null;

        activity.SetTag(ObservabilityConventions.Tags.Environment, Environment.GetEnvironmentVariable("ETLSQL_ENV") ?? "default");
        activity.SetTag(ObservabilityConventions.Tags.Node, Environment.MachineName);
        activity.SetTag(ObservabilityConventions.Tags.Component, component);
        activity.SetTag(ObservabilityConventions.Tags.WorkloadKind, "background");
        activity.SetTag(ServiceNameTag, serviceName);
        activity.SetTag(OperationTag, operation);
        return activity;
    }

    public static void CompleteRun(Activity? activity, string component, string serviceName, string operation,
        string status, long durationMs, long attempts = 0)
    {
        if (activity is not null)
        {
            activity.SetTag(ObservabilityConventions.Tags.Status, status);
            activity.SetTag("etlsql.attempts", Math.Max(0, attempts));
            activity.SetStatus(status is "failed" or "failure" ? ActivityStatusCode.Error : ActivityStatusCode.Ok);
        }

        var tags = new TagList
        {
            { ObservabilityConventions.Tags.Environment, Environment.GetEnvironmentVariable("ETLSQL_ENV") ?? "default" },
            { ObservabilityConventions.Tags.Node, Environment.MachineName },
            { ObservabilityConventions.Tags.Component, component },
            { ObservabilityConventions.Tags.WorkloadKind, "background" },
            { ServiceNameTag, serviceName },
            { OperationTag, operation },
            { ObservabilityConventions.Tags.Status, status }
        };

        RunCompletedCounter.Add(1, tags);
        RunDurationMs.Record(Math.Max(0, durationMs), tags);
        RunAttempts.Record(Math.Max(0, attempts), tags);
    }
}
