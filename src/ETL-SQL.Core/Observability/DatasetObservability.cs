using System.Diagnostics;
using System.Diagnostics.Metrics;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Core.Observability;

public static class DatasetObservability
{
    public const string ActivitySourceName = "ETL-SQL.Datasets";
    public const string MeterName = "ETL-SQL.Datasets";
    public const string OperationTag = "etlsql.operation";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> OperationCompletedCounter =
        Meter.CreateCounter<long>("etlsql.dataset.operation.completed");
    private static readonly Histogram<double> OperationDurationMs =
        Meter.CreateHistogram<double>("etlsql.dataset.operation.duration_ms");

    public static Activity? StartOperation(string operation)
    {
        var activity = ActivitySource.StartActivity("dataset.operation", ActivityKind.Internal);
        if (activity is null)
            return null;

        activity.SetTag(ObservabilityConventions.Tags.Environment, Environment.GetEnvironmentVariable("ETLSQL_ENV") ?? "default");
        activity.SetTag(ObservabilityConventions.Tags.Node, Environment.MachineName);
        activity.SetTag(ObservabilityConventions.Tags.Component, "dataset");
        activity.SetTag(OperationTag, operation);
        return activity;
    }

    public static void CompleteOperation(Activity? activity, string operation, string status, long durationMs,
        int? datasetId = null)
    {
        if (activity is not null)
        {
            activity.SetTag(ObservabilityConventions.Tags.Status, status);
            if (datasetId is int id && id > 0)
                activity.SetTag(ObservabilityConventions.Tags.DatasetId, id);
            activity.SetStatus(status is "success" ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
        }

        var tags = new TagList
        {
            { ObservabilityConventions.Tags.Environment, Environment.GetEnvironmentVariable("ETLSQL_ENV") ?? "default" },
            { ObservabilityConventions.Tags.Node, Environment.MachineName },
            { ObservabilityConventions.Tags.Component, "dataset" },
            { OperationTag, operation },
            { ObservabilityConventions.Tags.Status, status }
        };

        OperationCompletedCounter.Add(1, tags);
        OperationDurationMs.Record(Math.Max(0, durationMs), tags);
    }

    public static IDatasetRegistry Instrument(IDatasetRegistry registry) =>
        registry is InstrumentedDatasetRegistry ? registry : new InstrumentedDatasetRegistry(registry);
}
