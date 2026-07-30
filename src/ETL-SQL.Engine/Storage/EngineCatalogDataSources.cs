using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Planning;
using ETL_SQL.Data;
using ETL_SQL.Services;

namespace ETL_SQL.Engine.Storage;

public sealed class ConnectionsDataSource : IDataSource
{
    private readonly IExecutionContext _context;
    private static readonly string[] Columns = ["connection_name", "connector_type", "details"];

    public ConnectionsDataSource(IExecutionContext context)
    {
        _context = context;
    }

    public string Path => "eng.connections";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";

    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
        ReadBatches(batchSize, CancellationToken.None);

    public async IAsyncEnumerable<DataTable> ReadBatches(
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = new List<Row>();
        foreach (var connection in _context.Connections.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(new Row
            {
                ["connection_name"] = connection.Key,
                ["connector_type"] = connection.Value.GetType().Name,
                ["details"] = connection.Value.ToString()
            });

            if (rows.Count >= batchSize)
            {
                yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
                rows = [];
            }
        }

        if (rows.Count > 0)
            yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
    }

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) =>
        WriteBatches(batches, append, CancellationToken.None);

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) =>
        throw new NotSupportedException("eng.connections is read-only.");

    public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)Columns);
    public object? Snapshot() => null;
    public void Restore(object? snapshot) { }
    public IDataSource WithTable(string tableName) => this;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class TablesDataSource : IDataSource
{
    private readonly IExecutionContext _context;
    private static readonly string[] Columns = ["connection_name", "table_name", "connector_type"];

    public TablesDataSource(IExecutionContext context)
    {
        _context = context;
    }

    public string Path => "eng.tables";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";

    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
        ReadBatches(batchSize, CancellationToken.None);

    public async IAsyncEnumerable<DataTable> ReadBatches(
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = new List<Row>();
        foreach (var connection in _context.Connections.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tables = await connection.Value.GetTablesAsync(cancellationToken);
            foreach (var table in tables.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                rows.Add(new Row
                {
                    ["connection_name"] = connection.Key,
                    ["table_name"] = table,
                    ["connector_type"] = connection.Value.GetType().Name
                });

                if (rows.Count >= batchSize)
                {
                    yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
                    rows = [];
                }
            }
        }

        if (rows.Count > 0)
            yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
    }

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) =>
        WriteBatches(batches, append, CancellationToken.None);

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) =>
        throw new NotSupportedException("eng.tables is read-only.");

    public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)Columns);
    public object? Snapshot() => null;
    public void Restore(object? snapshot) { }
    public IDataSource WithTable(string tableName) => this;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class ViewsDataSource : IDataSource
{
    private readonly IExecutionContext _context;
    private static readonly string[] Columns = ["view_name", "query"];

    public ViewsDataSource(IExecutionContext context)
    {
        _context = context;
    }

    public string Path => "eng.views";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";

    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
        ReadBatches(batchSize, CancellationToken.None);

    public async IAsyncEnumerable<DataTable> ReadBatches(
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = new List<Row>();
        foreach (var view in _context.GetViews().OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(new Row
            {
                ["view_name"] = view.Key,
                ["query"] = view.Value.Query.ToSql()
            });

            if (rows.Count >= batchSize)
            {
                yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
                rows = [];
            }
        }

        if (rows.Count > 0)
            yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
    }

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) =>
        WriteBatches(batches, append, CancellationToken.None);

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) =>
        throw new NotSupportedException("eng.views is read-only.");

    public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)Columns);
    public object? Snapshot() => null;
    public void Restore(object? snapshot) { }
    public IDataSource WithTable(string tableName) => this;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class VariablesDataSource : IDataSource
{
    private readonly IExecutionContext _context;
    private static readonly string[] Columns = ["variable_name", "value", "data_type", "scope", "is_sensitive"];

    public VariablesDataSource(IExecutionContext context)
    {
        _context = context;
    }

    public string Path => "eng.variables";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";

    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
        ReadBatches(batchSize, CancellationToken.None);

    public async IAsyncEnumerable<DataTable> ReadBatches(
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = new List<Row>();
        foreach (var variable in _context.VarContext.Variables.OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            _context.VarContext.VariableMetadata.TryGetValue(variable.Key, out var metadata);
            var isSensitive = metadata?.IsSensitive == true || metadata?.IsSecret == true;
            var value = isSensitive && !_context.ShowPassword ? "*******" : variable.Value;

            rows.Add(new Row
            {
                ["variable_name"] = variable.Key,
                ["value"] = value,
                ["data_type"] = GetDataTypeName(value),
                ["scope"] = "Global",
                ["is_sensitive"] = isSensitive
            });

            if (rows.Count >= batchSize)
            {
                yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
                rows = [];
            }
        }

        if (rows.Count > 0)
            yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
    }

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) =>
        WriteBatches(batches, append, CancellationToken.None);

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) =>
        throw new NotSupportedException("eng.variables is read-only.");

    public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)Columns);
    public object? Snapshot() => null;
    public void Restore(object? snapshot) { }
    public IDataSource WithTable(string tableName) => this;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static string GetDataTypeName(object? value)
    {
        if (value == null) return "NULL";
        if (value is int || value is long) return "INT";
        if (value is bool) return "BOOLEAN";
        if (value is double || value is decimal) return "DECIMAL";
        if (value is DateTime) return "DATETIME";
        if (value is string) return "STRING";
        if (value is IEnumerable<object>) return "LIST";
        return value.GetType().Name.ToUpperInvariant();
    }
}

public sealed class VersionDataSource : IDataSource
{
    private static readonly string[] Columns = ["component", "version", "metadata"];

    public string Path => "eng.version";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";

    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
        ReadBatches(batchSize, CancellationToken.None);

    public async IAsyncEnumerable<DataTable> ReadBatches(
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return await EngineCatalogTableBuilder.BuildAsync(Columns,
        [
            new Row
            {
                ["component"] = "ETL-SQL Engine",
                ["version"] = LanguageMetadata.EngineVersion,
                ["metadata"] = ".NET 10.0; Charles Clemens (c) 2026"
            }
        ]);
    }

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) =>
        WriteBatches(batches, append, CancellationToken.None);

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) =>
        throw new NotSupportedException("eng.version is read-only.");

    public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)Columns);
    public object? Snapshot() => null;
    public void Restore(object? snapshot) { }
    public IDataSource WithTable(string tableName) => this;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class SafeZonesDataSource : IDataSource
{
    private readonly SecurityService _securityService;
    private static readonly string[] Columns = ["path", "is_system_path", "resolution"];

    public SafeZonesDataSource(SecurityService securityService)
    {
        _securityService = securityService;
    }

    public string Path => "eng.safe_zones";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";

    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
        ReadBatches(batchSize, CancellationToken.None);

    public async IAsyncEnumerable<DataTable> ReadBatches(
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = new List<Row>();
        foreach (var zone in _securityService.ApprovedSafeZones.OrderBy(z => z, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(new Row
            {
                ["path"] = zone,
                ["is_system_path"] = _securityService.IsSystemPath(zone),
                ["resolution"] = "Authorized"
            });

            if (rows.Count >= batchSize)
            {
                yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
                rows = [];
            }
        }

        if (rows.Count > 0)
            yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
    }

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) =>
        WriteBatches(batches, append, CancellationToken.None);

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) =>
        throw new NotSupportedException("eng.safe_zones is read-only.");

    public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)Columns);
    public object? Snapshot() => null;
    public void Restore(object? snapshot) { }
    public IDataSource WithTable(string tableName) => this;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class ProfileDataSource : IDataSource
{
    private readonly IExecutionContext _context;
    private static readonly string[] Columns =
    [
        "timestamp", "statement", "rows_processed", "index_used", "duration_ms", "memory_kb",
        "spilled_bytes", "subquery_hits", "subquery_misses", "subquery_spilled_bytes", "partitions",
        "queue_wait_ms", "lock_wait_ms", "plan_decisions", "plan_accepted", "plan_fallbacks",
        "plan_rejected", "plan_degraded", "plan_fallback_summary"
    ];

    public ProfileDataSource(IExecutionContext context)
    {
        _context = context;
    }

    public string Path => "eng.profile";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";

    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
        ReadBatches(batchSize, CancellationToken.None);

    public async IAsyncEnumerable<DataTable> ReadBatches(
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var planDecisions = _context.Telemetry.PlanDecisions;
        var planDecisionCount = planDecisions.Count;
        var planAccepted = planDecisions.Count(d => d.Outcome == PlanDecisionOutcome.Accepted);
        var planFallbacks = planDecisions.Count(d => d.Outcome == PlanDecisionOutcome.Fallback);
        var planRejected = planDecisions.Count(d => d.Outcome == PlanDecisionOutcome.Rejected);
        var planDegraded = planDecisions.Count(d => d.Outcome == PlanDecisionOutcome.Degraded);
        var planFallbackSummary = PlanDecisionSummary.FormatFallbackSummary(planDecisions);

        var rows = new List<Row>();
        foreach (var metric in _context.Telemetry.ProfileMetrics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(new Row
            {
                ["timestamp"] = metric.Timestamp,
                ["statement"] = metric.Sql,
                ["rows_processed"] = metric.RowsProcessed,
                ["index_used"] = metric.IndexName ?? "--",
                ["duration_ms"] = metric.DurationMs,
                ["memory_kb"] = metric.MemoryDeltaBytes / 1024.0,
                ["spilled_bytes"] = metric.SpilledBytes,
                ["subquery_hits"] = metric.SubqueryCacheHits,
                ["subquery_misses"] = metric.SubqueryCacheMisses,
                ["subquery_spilled_bytes"] = metric.SubquerySpilledBytes,
                ["partitions"] = metric.PartitionsCount,
                ["queue_wait_ms"] = metric.QueueWaitMs,
                ["lock_wait_ms"] = metric.LockWaitMs,
                ["plan_decisions"] = planDecisionCount,
                ["plan_accepted"] = planAccepted,
                ["plan_fallbacks"] = planFallbacks,
                ["plan_rejected"] = planRejected,
                ["plan_degraded"] = planDegraded,
                ["plan_fallback_summary"] = planFallbackSummary
            });

            if (rows.Count >= batchSize)
            {
                yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
                rows = [];
            }
        }

        if (rows.Count > 0)
            yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
    }

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) =>
        WriteBatches(batches, append, CancellationToken.None);

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) =>
        throw new NotSupportedException("eng.profile is read-only.");

    public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)Columns);
    public object? Snapshot() => null;
    public void Restore(object? snapshot) { }
    public IDataSource WithTable(string tableName) => this;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class ConnectionConfigDataSource : IDataSource
{
    private readonly IExecutionContext _context;
    private static readonly string[] Columns = ["connection_name", "option", "value"];

    public ConnectionConfigDataSource(IExecutionContext context)
    {
        _context = context;
    }

    public string Path => "eng.connection_config";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";

    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
        ReadBatches(batchSize, CancellationToken.None);

    public async IAsyncEnumerable<DataTable> ReadBatches(
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = new List<Row>();
        foreach (var connection in _context.Connections.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var option in connection.Value.GetConfig().OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                rows.Add(new Row
                {
                    ["connection_name"] = connection.Key,
                    ["option"] = option.Key,
                    ["value"] = option.Value
                });

                if (rows.Count >= batchSize)
                {
                    yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
                    rows = [];
                }
            }
        }

        if (rows.Count > 0)
            yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
    }

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) =>
        WriteBatches(batches, append, CancellationToken.None);

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) =>
        throw new NotSupportedException("eng.connection_config is read-only.");

    public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)Columns);
    public object? Snapshot() => null;
    public void Restore(object? snapshot) { }
    public IDataSource WithTable(string tableName) => this;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class JobsDataSource : IDataSource
{
    private readonly IJobHistoryStore? _store;
    private static readonly string[] Columns = ["name", "schedule", "last_run", "next_run", "script", "enabled"];

    public JobsDataSource(IJobHistoryStore? store) => _store = store;

    public string Path => "eng.jobs";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";

    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => ReadBatches(batchSize, CancellationToken.None);

    public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = new List<Row>();
        if (_store != null)
        {
            foreach (var job in (await _store.GetAllJobsAsync()).OrderBy(j => j.Name, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                rows.Add(new Row
                {
                    ["name"] = job.Name,
                    ["schedule"] = $"EVERY {job.Interval} {job.Unit}" + (job.AtTime != null ? $" AT {job.AtTime}" : ""),
                    ["last_run"] = job.LastRun,
                    ["next_run"] = job.NextRun,
                    ["script"] = job.Script,
                    ["enabled"] = job.IsEnabled ? 1 : 0
                });

                if (rows.Count >= batchSize)
                {
                    yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
                    rows = [];
                }
            }
        }

        yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
    }

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => WriteBatches(batches, append, CancellationToken.None);
    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) => throw new NotSupportedException("eng.jobs is read-only.");
    public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)Columns);
    public object? Snapshot() => null;
    public void Restore(object? snapshot) { }
    public IDataSource WithTable(string tableName) => this;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class JobHistoryDataSource : IDataSource
{
    private readonly IJobHistoryStore? _store;
    private readonly string? _jobNameFilter;
    private static readonly string[] Columns = ["id", "job_name", "start_time", "end_time", "status", "rows_processed", "peak_ram_mb", "cpu_time_s", "error_message"];

    public JobHistoryDataSource(IJobHistoryStore? store, string? jobNameFilter = null)
    {
        _store = store;
        _jobNameFilter = jobNameFilter;
    }

    public string Path => "eng.job_history";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";

    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => ReadBatches(batchSize, CancellationToken.None);

    public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = new List<Row>();
        if (_store != null)
        {
            foreach (var entry in await _store.GetHistoryAsync(_jobNameFilter, Math.Max(batchSize, 1000)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                rows.Add(new Row
                {
                    ["id"] = entry.Id,
                    ["job_name"] = entry.JobName,
                    ["start_time"] = entry.StartTime,
                    ["end_time"] = entry.EndTime,
                    ["status"] = entry.Status,
                    ["rows_processed"] = entry.RowsProcessed,
                    ["peak_ram_mb"] = entry.PeakMemoryBytes / (1024.0 * 1024.0),
                    ["cpu_time_s"] = entry.CpuTimeSeconds,
                    ["error_message"] = entry.ErrorMessage
                });

                if (rows.Count >= batchSize)
                {
                    yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
                    rows = [];
                }
            }
        }

        yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
    }

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => WriteBatches(batches, append, CancellationToken.None);
    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) => throw new NotSupportedException("eng.job_history is read-only.");
    public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)Columns);
    public object? Snapshot() => null;
    public void Restore(object? snapshot) { }
    public IDataSource WithTable(string tableName) => this;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class JobStateDataSource : IDataSource
{
    private readonly IJobHistoryStore? _store;
    private static readonly string[] Columns = ["job_name", "state_key", "state_value", "updated_at"];

    public JobStateDataSource(IJobHistoryStore? store) => _store = store;

    public string Path => "eng.job_state";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";

    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => ReadBatches(batchSize, CancellationToken.None);

    public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = new List<Row>();
        if (_store != null)
        {
            foreach (var entry in await _store.GetJobStatesAsync(null, Math.Max(batchSize, 1000)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                rows.Add(new Row
                {
                    ["job_name"] = entry.JobName,
                    ["state_key"] = entry.StateKey,
                    ["state_value"] = entry.StateValue,
                    ["updated_at"] = entry.UpdatedAt
                });

                if (rows.Count >= batchSize)
                {
                    yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
                    rows = [];
                }
            }
        }

        yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
    }

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => WriteBatches(batches, append, CancellationToken.None);
    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) => throw new NotSupportedException("eng.job_state is read-only.");
    public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)Columns);
    public object? Snapshot() => null;
    public void Restore(object? snapshot) { }
    public IDataSource WithTable(string tableName) => this;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class HostMetricsDataSource : IDataSource
{
    private readonly IHostMetricsStore? _store;
    private static readonly string[] Columns = ["node_id", "captured_at", "memory_load_percent", "process_cpu_percent", "host_cpu_percent", "state_disk_free_mb", "spill_disk_free_mb"];

    public HostMetricsDataSource(IHostMetricsStore? store) => _store = store;

    public string Path => "eng.host_metrics";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";

    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => ReadBatches(batchSize, CancellationToken.None);

    public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = new List<Row>();
        if (_store != null)
        {
            foreach (var sample in await _store.GetHostMetricsAsync(null, DateTime.UtcNow.AddDays(-1), Math.Max(batchSize, 1000)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                rows.Add(new Row
                {
                    ["node_id"] = sample.NodeId,
                    ["captured_at"] = sample.CapturedAt,
                    ["memory_load_percent"] = sample.MemoryLoadPercent,
                    ["process_cpu_percent"] = sample.ProcessCpuPercent,
                    ["host_cpu_percent"] = sample.HostCpuPercent,
                    ["state_disk_free_mb"] = sample.StateDiskFreeBytes / (1024.0 * 1024.0),
                    ["spill_disk_free_mb"] = sample.SpillDiskFreeBytes / (1024.0 * 1024.0)
                });

                if (rows.Count >= batchSize)
                {
                    yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
                    rows = [];
                }
            }
        }

        yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
    }

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => WriteBatches(batches, append, CancellationToken.None);
    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) => throw new NotSupportedException("eng.host_metrics is read-only.");
    public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)Columns);
    public object? Snapshot() => null;
    public void Restore(object? snapshot) { }
    public IDataSource WithTable(string tableName) => this;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class BundlesDataSource : IDataSource
{
    private readonly IBundleStore? _store;
    private static readonly string[] Columns = ["bundle_name", "version", "entry_path", "content_hash", "published_at", "publisher", "description"];

    public BundlesDataSource(IBundleStore? store) => _store = store;

    public string Path => "eng.bundles";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";

    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => ReadBatches(batchSize, CancellationToken.None);

    public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = new List<Row>();
        if (_store != null)
        {
            foreach (var bundle in await _store.GetBundlesAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddBundleVersionRow(rows, bundle);
                if (rows.Count >= batchSize)
                {
                    yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
                    rows = [];
                }
            }
        }

        yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
    }

    internal static void AddBundleVersionRow(List<Row> rows, BundleVersionInfo version)
    {
        rows.Add(new Row
        {
            ["bundle_name"] = version.BundleName,
            ["version"] = version.Version,
            ["entry_path"] = version.EntryPath,
            ["content_hash"] = version.ContentHash,
            ["published_at"] = version.PublishedAt,
            ["publisher"] = version.Publisher,
            ["description"] = version.Description
        });
    }

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => WriteBatches(batches, append, CancellationToken.None);
    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) => throw new NotSupportedException("eng.bundles is read-only.");
    public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)Columns);
    public object? Snapshot() => null;
    public void Restore(object? snapshot) { }
    public IDataSource WithTable(string tableName) => this;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class BundleFilesDataSource : IDataSource
{
    private readonly IBundleStore? _store;
    private readonly string? _filterBundle;
    private readonly int? _filterVersion;
    private static readonly string[] Columns = ["bundle_name", "version", "virtual_path", "content_hash", "size_bytes", "content_type"];

    public BundleFilesDataSource(IBundleStore? store, string? filterBundle = null, int? filterVersion = null)
    {
        _store = store;
        _filterBundle = filterBundle;
        _filterVersion = filterVersion;
    }

    public string Path => "eng.bundle_files";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";

    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => ReadBatches(batchSize, CancellationToken.None);

    public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = new List<Row>();
        if (_store != null)
        {
            if (_filterBundle != null)
            {
                if (_filterVersion.HasValue)
                {
                    foreach (var file in await _store.GetFilesAsync(_filterBundle, _filterVersion.Value))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        rows.Add(new Row
                        {
                            ["bundle_name"] = file.BundleName,
                            ["version"] = file.Version,
                            ["virtual_path"] = file.VirtualPath,
                            ["content_hash"] = file.ContentHash,
                            ["size_bytes"] = file.SizeBytes,
                            ["content_type"] = file.ContentType
                        });

                        if (rows.Count >= batchSize)
                        {
                            yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
                            rows = [];
                        }
                    }
                }
                else
                {
                    foreach (var version in await _store.GetVersionsAsync(_filterBundle))
                    foreach (var file in await _store.GetFilesAsync(version.BundleName, version.Version))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        rows.Add(new Row
                        {
                            ["bundle_name"] = file.BundleName,
                            ["version"] = file.Version,
                            ["virtual_path"] = file.VirtualPath,
                            ["content_hash"] = file.ContentHash,
                            ["size_bytes"] = file.SizeBytes,
                            ["content_type"] = file.ContentType
                        });

                        if (rows.Count >= batchSize)
                        {
                            yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
                            rows = [];
                        }
                    }
                }
            }
            else
            {
                foreach (var bundle in await _store.GetBundlesAsync())
                foreach (var version in await _store.GetVersionsAsync(bundle.BundleName))
                foreach (var file in await _store.GetFilesAsync(version.BundleName, version.Version))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    rows.Add(new Row
                    {
                        ["bundle_name"] = file.BundleName,
                        ["version"] = file.Version,
                        ["virtual_path"] = file.VirtualPath,
                        ["content_hash"] = file.ContentHash,
                        ["size_bytes"] = file.SizeBytes,
                        ["content_type"] = file.ContentType
                    });

                    if (rows.Count >= batchSize)
                    {
                        yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
                        rows = [];
                    }
                }
            }
        }

        yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
    }

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => WriteBatches(batches, append, CancellationToken.None);
    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) => throw new NotSupportedException("eng.bundle_files is read-only.");
    public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)Columns);
    public object? Snapshot() => null;
    public void Restore(object? snapshot) { }
    public IDataSource WithTable(string tableName) => this;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class BundleDependenciesDataSource : IDataSource
{
    private readonly IBundleStore? _store;
    private readonly string? _filterBundle;
    private readonly int? _filterVersion;
    private static readonly string[] Columns = ["bundle_name", "version", "from_path", "to_path"];

    public BundleDependenciesDataSource(IBundleStore? store, string? filterBundle = null, int? filterVersion = null)
    {
        _store = store;
        _filterBundle = filterBundle;
        _filterVersion = filterVersion;
    }

    public string Path => "eng.bundle_dependencies";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";

    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => ReadBatches(batchSize, CancellationToken.None);

    public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = new List<Row>();
        if (_store != null)
        {
            if (_filterBundle != null)
            {
                if (_filterVersion.HasValue)
                {
                    foreach (var dependency in await _store.GetDependenciesAsync(_filterBundle, _filterVersion.Value))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        rows.Add(new Row
                        {
                            ["bundle_name"] = dependency.BundleName,
                            ["version"] = dependency.Version,
                            ["from_path"] = dependency.FromPath,
                            ["to_path"] = dependency.ToPath
                        });

                        if (rows.Count >= batchSize)
                        {
                            yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
                            rows = [];
                        }
                    }
                }
                else
                {
                    foreach (var version in await _store.GetVersionsAsync(_filterBundle))
                    foreach (var dependency in await _store.GetDependenciesAsync(version.BundleName, version.Version))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        rows.Add(new Row
                        {
                            ["bundle_name"] = dependency.BundleName,
                            ["version"] = dependency.Version,
                            ["from_path"] = dependency.FromPath,
                            ["to_path"] = dependency.ToPath
                        });

                        if (rows.Count >= batchSize)
                        {
                            yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
                            rows = [];
                        }
                    }
                }
            }
            else
            {
                foreach (var bundle in await _store.GetBundlesAsync())
                foreach (var version in await _store.GetVersionsAsync(bundle.BundleName))
                foreach (var dependency in await _store.GetDependenciesAsync(version.BundleName, version.Version))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    rows.Add(new Row
                    {
                        ["bundle_name"] = dependency.BundleName,
                        ["version"] = dependency.Version,
                        ["from_path"] = dependency.FromPath,
                        ["to_path"] = dependency.ToPath
                    });

                    if (rows.Count >= batchSize)
                    {
                        yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
                        rows = [];
                    }
                }
            }
        }

        yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
    }

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => WriteBatches(batches, append, CancellationToken.None);
    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) => throw new NotSupportedException("eng.bundle_dependencies is read-only.");
    public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)Columns);
    public object? Snapshot() => null;
    public void Restore(object? snapshot) { }
    public IDataSource WithTable(string tableName) => this;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class LocksDataSource : IDataSource
{
    private readonly Microsoft.Extensions.Configuration.IConfiguration? _configuration;
    private static readonly string[] Columns = ["Id", "ProcessId", "JobName", "AcquiredAt", "MachineName"];

    public LocksDataSource(Microsoft.Extensions.Configuration.IConfiguration? configuration) => _configuration = configuration;

    public string Path => "eng.locks";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";

    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => ReadBatches(batchSize, CancellationToken.None);

    public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = new List<Row>();
        var dbPath = _configuration?["Orchestrator:DatabasePath"] ?? GetDefaultDbPath();
        var connectionString = $"Data Source={dbPath}";

        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ThrottleSlots (
                    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    ProcessId   INTEGER NOT NULL,
                    JobName     TEXT    NOT NULL,
                    AcquiredAt  TEXT    NOT NULL,
                    MachineName TEXT    DEFAULT ''
                );";
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            try
            {
                cmd.CommandText = "ALTER TABLE ThrottleSlots ADD COLUMN MachineName TEXT DEFAULT '';";
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 1)
            {
                // Column already exists, ignore
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, ProcessId, JobName, AcquiredAt, MachineName FROM ThrottleSlots ORDER BY AcquiredAt DESC;";
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = new Row();
                row["Id"] = reader.GetInt64(0);
                row["ProcessId"] = reader.GetInt32(1);
                row["JobName"] = reader.GetString(2);
                row["AcquiredAt"] = reader.GetString(3);
                row["MachineName"] = reader.IsDBNull(4) ? "" : reader.GetString(4);
                rows.Add(row);

                if (rows.Count >= batchSize)
                {
                    yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
                    rows = [];
                }
            }
        }

        yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
    }

    private static string GetDefaultDbPath()
    {
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ETL-SQL");
        System.IO.Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, "etlsql.db");
    }

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => WriteBatches(batches, append, CancellationToken.None);
    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) => throw new NotSupportedException("eng.locks is read-only.");
    public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)Columns);
    public object? Snapshot() => null;
    public void Restore(object? snapshot) { }
    public IDataSource WithTable(string tableName) => this;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class SessionsDataSource : IDataSource
{
    private readonly ETL_SQL.Core.Execution.ISessionStateManager _sessionManager;
    private static readonly string[] Columns = ["SessionId", "Created", "LastModified", "Size_MB", "TempTables", "Variables", "LastScript", "User", "Machine"];

    public SessionsDataSource(ETL_SQL.Core.Execution.ISessionStateManager sessionManager) => _sessionManager = sessionManager;

    public string Path => "eng.sessions";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";

    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => ReadBatches(batchSize, CancellationToken.None);

    public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = new List<Row>();
        var sessions = _sessionManager.GetSessions().OrderByDescending(s => s.LastModifiedAt);

        foreach (var sess in sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = new Row();
            row["SessionId"] = sess.SessionId;
            row["Created"] = sess.CreatedAt;
            row["LastModified"] = sess.LastModifiedAt;
            row["Size_MB"] = sess.SizeMB.HasValue ? (decimal)sess.SizeMB.Value : null;
            row["TempTables"] = (decimal)sess.TempTableCount;
            row["Variables"] = (decimal)sess.VariableCount;
            row["LastScript"] = sess.LastScriptSource ?? "";
            row["User"] = sess.OwnerUser ?? "";
            row["Machine"] = sess.OwnerMachine ?? "";
            rows.Add(row);

            if (rows.Count >= batchSize)
            {
                yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
                rows = [];
            }
        }

        yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
    }

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => WriteBatches(batches, append, CancellationToken.None);
    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) => throw new NotSupportedException("eng.sessions is read-only.");
    public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)Columns);
    public object? Snapshot() => null;
    public void Restore(object? snapshot) { }
    public IDataSource WithTable(string tableName) => this;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class LineageHistoryDataSource : IDataSource
{
    private readonly ILineageCatalogStore? _catalog;
    private readonly Microsoft.Extensions.Configuration.IConfiguration? _config;
    private static readonly string[] Columns = ["Id", "RunAt", "JobName", "TargetTable", "TargetColumn", "SourceTables", "Operation", "Tags", "SourceFile", "Line"];

    public LineageHistoryDataSource(ILineageCatalogStore? catalog, Microsoft.Extensions.Configuration.IConfiguration? config)
    {
        _catalog = catalog;
        _config = config;
    }

    public string Path => "eng.lineage_history";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";

    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => ReadBatches(batchSize, CancellationToken.None);

    public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = new List<Row>();
        if (_catalog != null)
        {
            int defaultLimit = 1000;
            if (_config != null && int.TryParse(_config["Engine:DefaultHistoryLimit"], out var val))
                defaultLimit = val;
            var entries = await _catalog.GetRecentLineageAsync(defaultLimit);
            foreach (var e in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = new Row();
                row["Id"] = e.Id;
                row["RunAt"] = e.RunAt;
                row["JobName"] = e.JobName;
                row["TargetTable"] = e.TargetTable;
                row["TargetColumn"] = e.TargetColumn;
                row["SourceTables"] = string.Join(", ", e.SourceTables);
                row["Operation"] = e.Operation;
                row["Tags"] = System.Text.Json.JsonSerializer.Serialize(e.Tags);
                row["SourceFile"] = e.SourceFile;
                row["Line"] = (decimal)e.Line;
                rows.Add(row);

                if (rows.Count >= batchSize)
                {
                    yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
                    rows = [];
                }
            }
        }

        yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
    }

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => WriteBatches(batches, append, CancellationToken.None);
    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) => throw new NotSupportedException("eng.lineage_history is read-only.");
    public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)Columns);
    public object? Snapshot() => null;
    public void Restore(object? snapshot) { }
    public IDataSource WithTable(string tableName) => this;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class MissingTagsDataSource : IDataSource
{
    private readonly ILineageCatalogStore? _catalog;
    private readonly Microsoft.Extensions.Configuration.IConfiguration? _config;
    private static readonly string[] Columns = ["TargetTable", "TargetColumn", "MissingTags", "PresentTags", "RunAt", "JobName", "ScriptPath"];

    public MissingTagsDataSource(ILineageCatalogStore? catalog, Microsoft.Extensions.Configuration.IConfiguration? config)
    {
        _catalog = catalog;
        _config = config;
    }

    public string Path => "eng.missing_tags";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";

    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => ReadBatches(batchSize, CancellationToken.None);

    public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = new List<Row>();
        if (_catalog != null)
        {
            int defaultLimit = 1000;
            if (_config != null && int.TryParse(_config["Engine:DefaultHistoryLimit"], out var val))
                defaultLimit = val;
            var entries = await _catalog.GetMissingMetadataAsync(
                StewardshipTagCatalog.RequiredStewardshipTags.ToArray(),
                defaultLimit);
            foreach (var e in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = new Row();
                row["TargetTable"] = e.TargetTable;
                row["TargetColumn"] = e.TargetColumn;
                row["MissingTags"] = string.Join(", ", e.MissingTags.Select(t => "@" + t));
                row["PresentTags"] = System.Text.Json.JsonSerializer.Serialize(e.PresentTags);
                row["RunAt"] = e.RunAt;
                row["JobName"] = e.JobName;
                row["ScriptPath"] = e.ScriptPath;
                rows.Add(row);

                if (rows.Count >= batchSize)
                {
                    yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
                    rows = [];
                }
            }
        }

        yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
    }

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => WriteBatches(batches, append, CancellationToken.None);
    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) => throw new NotSupportedException("eng.missing_tags is read-only.");
    public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)Columns);
    public object? Snapshot() => null;
    public void Restore(object? snapshot) { }
    public IDataSource WithTable(string tableName) => this;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class ProtectedDataDataSource : IDataSource
{
    private readonly ILineageCatalogStore? _catalog;
    private readonly Microsoft.Extensions.Configuration.IConfiguration? _config;
    private static readonly string[] Columns = [
        "Id", "RunAt", "JobName", "TargetTable", "TargetColumn", "SourceTables", "Operation",
        "ProtectionTags", "ProtectionReason", "Owner", "Steward", "Contact", "Domain",
        "Classification", "Quality", "Tags", "SourceFile", "Line"
    ];

    public ProtectedDataDataSource(ILineageCatalogStore? catalog, Microsoft.Extensions.Configuration.IConfiguration? config)
    {
        _catalog = catalog;
        _config = config;
    }

    public string Path => "eng.protected_data";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";

    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => ReadBatches(batchSize, CancellationToken.None);

    public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = new List<Row>();
        if (_catalog != null)
        {
            int defaultLimit = 1000;
            if (_config != null && int.TryParse(_config["Engine:DefaultHistoryLimit"], out var val))
                defaultLimit = val;
            var scanLimit = Math.Max(defaultLimit * 20, 1000);
            var recent = await _catalog.GetRecentLineageAsync(scanLimit);
            var entries = LineageProtectedData.FromHistory(recent).Take(defaultLimit);
            foreach (var e in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = new Row();
                row["Id"] = e.Id;
                row["RunAt"] = e.RunAt;
                row["JobName"] = e.JobName;
                row["TargetTable"] = e.TargetTable;
                row["TargetColumn"] = e.TargetColumn;
                row["SourceTables"] = string.Join(", ", e.SourceTables);
                row["Operation"] = e.Operation;
                row["ProtectionTags"] = string.Join(", ", e.ProtectionTags);
                row["ProtectionReason"] = e.ProtectionReason;
                row["Owner"] = e.Owner;
                row["Steward"] = e.Steward;
                row["Contact"] = e.Contact;
                row["Domain"] = e.Domain;
                row["Classification"] = e.Classification;
                row["Quality"] = e.Quality;
                row["Tags"] = System.Text.Json.JsonSerializer.Serialize(e.Tags);
                row["SourceFile"] = e.SourceFile;
                row["Line"] = (decimal)e.Line;
                rows.Add(row);

                if (rows.Count >= batchSize)
                {
                    yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
                    rows = [];
                }
            }
        }

        yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
    }

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => WriteBatches(batches, append, CancellationToken.None);
    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) => throw new NotSupportedException("eng.protected_data is read-only.");
    public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)Columns);
    public object? Snapshot() => null;
    public void Restore(object? snapshot) { }
    public IDataSource WithTable(string tableName) => this;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class ProtectedDataSuggestionsDataSource : IDataSource
{
    private readonly ILineageCatalogStore? _catalog;
    private readonly Microsoft.Extensions.Configuration.IConfiguration? _config;
    private static readonly string[] Columns = [
        "Id", "RunAt", "JobName", "TargetTable", "TargetColumn", "SourceTables", "SourceColumns",
        "SuggestedTag", "SuggestedValue", "Confidence", "EvidenceKind", "Evidence", "Reason",
        "ExistingTags", "SourceFile", "Line"
    ];

    public ProtectedDataSuggestionsDataSource(ILineageCatalogStore? catalog, Microsoft.Extensions.Configuration.IConfiguration? config)
    {
        _catalog = catalog;
        _config = config;
    }

    public string Path => "eng.protected_data_suggestions";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";

    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => ReadBatches(batchSize, CancellationToken.None);

    public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = new List<Row>();
        if (_catalog != null)
        {
            int defaultLimit = 1000;
            if (_config != null && int.TryParse(_config["Engine:DefaultHistoryLimit"], out var val))
                defaultLimit = val;
            var scanLimit = Math.Max(defaultLimit * 20, 1000);
            var recent = await _catalog.GetRecentLineageAsync(scanLimit);
            var entries = LineageProtectedData.SuggestFromHistory(recent).Take(defaultLimit);
            foreach (var e in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = new Row();
                row["Id"] = e.Id;
                row["RunAt"] = e.RunAt;
                row["JobName"] = e.JobName;
                row["TargetTable"] = e.TargetTable;
                row["TargetColumn"] = e.TargetColumn;
                row["SourceTables"] = string.Join(", ", e.SourceTables);
                row["SourceColumns"] = string.Join(", ", e.SourceColumns);
                row["SuggestedTag"] = e.SuggestedTag;
                row["SuggestedValue"] = e.SuggestedValue;
                row["Confidence"] = e.Confidence.ToString();
                row["EvidenceKind"] = e.EvidenceKind;
                row["Evidence"] = e.Evidence;
                row["Reason"] = e.Reason;
                row["ExistingTags"] = System.Text.Json.JsonSerializer.Serialize(e.ExistingTags);
                row["SourceFile"] = e.SourceFile;
                row["Line"] = (decimal)e.Line;
                rows.Add(row);

                if (rows.Count >= batchSize)
                {
                    yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
                    rows = [];
                }
            }
        }

        yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
    }

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => WriteBatches(batches, append, CancellationToken.None);
    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) => throw new NotSupportedException("eng.protected_data_suggestions is read-only.");
    public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)Columns);
    public object? Snapshot() => null;
    public void Restore(object? snapshot) { }
    public IDataSource WithTable(string tableName) => this;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class DataQualityRulesDataSource : IDataSource
{
    private readonly Evaluator _evaluator;
    private static readonly string[] Columns = [
        "TargetTable", "TargetColumn", "RuleTag", "Rule", "Action", "SourceFile", "Line"
    ];

    public DataQualityRulesDataSource(Evaluator evaluator) => _evaluator = evaluator;

    public string Path => "eng.data_quality_rules";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";

    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => ReadBatches(batchSize, CancellationToken.None);

    public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = new List<Row>();
        var entries = _evaluator.LineageTracker.GetFullLineage()
            .Where(e => ETL_SQL.Core.Quality.ColumnRuleParser.HasRuleTags(e.Metadata))
            .OrderBy(e => e.TargetTable, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.TargetColumn ?? "", StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ETL_SQL.Core.Quality.ColumnRuleBinding> bindings;
            try
            {
                bindings = ETL_SQL.Core.Quality.ColumnRuleParser.ParseBindings(entry.Metadata);
            }
            catch (ETL_SQL.Core.Quality.ColumnRuleParseException)
            {
                continue;
            }

            foreach (var binding in bindings)
            {
                foreach (var rule in binding.Rules)
                {
                    var key = $"{entry.TargetTable}|{entry.TargetColumn}|{binding.ExpectKey}|{rule.Text}";
                    if (!seen.Add(key)) continue;

                    var row = new Row();
                    row["TargetTable"] = entry.TargetTable;
                    row["TargetColumn"] = entry.TargetColumn;
                    row["RuleTag"] = "@" + binding.ExpectKey;
                    row["Rule"] = rule.Text;
                    row["Action"] = binding.Action.ToString().ToUpperInvariant() + (binding.ActionExplicit ? "" : " (default)");
                    row["SourceFile"] = entry.SourceFile;
                    row["Line"] = (decimal)entry.Line;
                    rows.Add(row);

                    if (rows.Count >= batchSize)
                    {
                        yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
                        rows = [];
                    }
                }
            }
        }

        yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
    }

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => WriteBatches(batches, append, CancellationToken.None);
    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) => throw new NotSupportedException("eng.data_quality_rules is read-only.");
    public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)Columns);
    public object? Snapshot() => null;
    public void Restore(object? snapshot) { }
    public IDataSource WithTable(string tableName) => this;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal static class EngineCatalogTableBuilder
{
    public static async Task<DataTable> BuildAsync(IEnumerable<string> columns, IEnumerable<Row> rows)
    {
        var table = new DataTable();
        table.SetColumns(columns);
        foreach (var row in rows)
            await table.AddRowAsync(row);
        return table;
    }
}
