using System.Diagnostics;
using System.Diagnostics.Metrics;
using ETL_SQL.Data;

namespace ETL_SQL.Core.Observability;

internal static class ConnectorObservability
{
    public const string ActivitySourceName = "ETL-SQL.Connectors";
    public const string MeterName = "ETL-SQL.Connectors";
    public const string OperationTag = "etlsql.operation";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> OperationCompletedCounter =
        Meter.CreateCounter<long>("etlsql.connector.operation.completed");
    private static readonly Histogram<double> OperationDurationMs =
        Meter.CreateHistogram<double>("etlsql.connector.operation.duration_ms");

    public static Activity? StartOperation(string connectorType, string operation)
    {
        var activity = ActivitySource.StartActivity("connector.operation", ActivityKind.Client);
        if (activity is null)
            return null;

        activity.SetTag(ObservabilityConventions.Tags.Environment, Environment.GetEnvironmentVariable("ETLSQL_ENV") ?? "default");
        activity.SetTag(ObservabilityConventions.Tags.Component, "connector");
        activity.SetTag(ObservabilityConventions.Tags.ConnectorType, connectorType);
        activity.SetTag(OperationTag, operation);
        return activity;
    }

    public static void CompleteOperation(Activity? activity, string connectorType, string operation,
        string status, long durationMs)
    {
        if (activity is not null)
        {
            activity.SetTag(ObservabilityConventions.Tags.Status, status);
            activity.SetStatus(status is "success" ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
        }

        var tags = new TagList
        {
            { ObservabilityConventions.Tags.Environment, Environment.GetEnvironmentVariable("ETLSQL_ENV") ?? "default" },
            { ObservabilityConventions.Tags.Component, "connector" },
            { ObservabilityConventions.Tags.ConnectorType, connectorType },
            { OperationTag, operation },
            { ObservabilityConventions.Tags.Status, status }
        };

        OperationCompletedCounter.Add(1, tags);
        OperationDurationMs.Record(Math.Max(0, durationMs), tags);
    }

    public static InstrumentedConnector Instrument(IConnector connector) =>
        connector is InstrumentedConnector instrumented ? instrumented : new InstrumentedConnector(connector);
}
