namespace ETL_SQL.Core.Observability;

/// <summary>
/// Shared low-cardinality observability names for metrics, traces, and scrape labels. These constants
/// intentionally avoid free-form names, paths, SQL text, parameter values, connection strings, and
/// other high-cardinality or sensitive fields.
/// </summary>
public static class ObservabilityConventions
{
    public static class Tags
    {
        public const string Environment = "etlsql.environment";
        public const string Node = "etlsql.node";
        public const string Component = "etlsql.component";
        public const string JobId = "etlsql.job.id";
        public const string ReportId = "etlsql.report.id";
        public const string DatasetId = "etlsql.dataset.id";
        public const string ConnectorType = "etlsql.connector.type";
        public const string ServiceName = "etlsql.service.name";
        public const string ExecutionMode = "etlsql.execution.mode";
        public const string WorkloadKind = "etlsql.workload.kind";
        public const string Status = "etlsql.status";
        public const string PolicyVersion = "etlsql.policy.version";
        public const string PolicyHash = "etlsql.policy.hash";
        public const string RowsProcessed = "etlsql.rows_processed";
        public const string PeakMemoryBytes = "etlsql.peak_memory_bytes";
        public const string CpuTimeSeconds = "etlsql.cpu_time_seconds";
        public const string QueueWaitMs = "etlsql.queue_wait_ms";
        public const string JobAttempt = "etlsql.job.attempt";
        public const string SpillBytes = "etlsql.spill_bytes";
        public const string SpillReadBytes = "etlsql.spill_read_bytes";
        public const string ScriptHash = "etlsql.script_hash";
        public const string CorrelationId = "etlsql.correlation_id";
    }

    public static string PrometheusLabel(string tag) =>
        tag.StartsWith("etlsql.", StringComparison.Ordinal)
            ? tag["etlsql.".Length..].Replace('.', '_')
            : tag.Replace('.', '_');
}
