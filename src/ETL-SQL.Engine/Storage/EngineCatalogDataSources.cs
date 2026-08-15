using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
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
        var varNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in _context.VarContext.Variables.Keys) varNames.Add(name);
        foreach (var name in _context.VarContext.CurrentVariables.Keys) varNames.Add(name);

        foreach (var name in varNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var val = _context.VarContext.GetVariable(name);

            VariableMetadata? metadata = null;
            if (_context.VarContext.CurrentMetadata.TryGetValue(name, out var m)) metadata = m;
            else _context.VarContext.VariableMetadata.TryGetValue(name, out metadata);

            var isSensitive = metadata?.IsSensitive == true || metadata?.IsSecret == true;
            var value = isSensitive && !_context.ShowPassword ? "*******" : val;

            var isLocal = _context.VarContext.CurrentVariables != _context.VarContext.Variables && _context.VarContext.CurrentVariables.ContainsKey(name);
            var scope = isLocal ? "Local" : "Global";

            rows.Add(new Row
            {
                ["variable_name"] = name,
                // Rendered, not the raw value. Variables are heterogeneous by nature, so emitting
                // them raw made this a column holding a number in one row and a string in the next
                // — which any columnar materialization of the view (SELECT ... INTO, a spill) then
                // fails on. The column is documented as text and already carries "*******" for a
                // masked value; data_type is what reports the original type.
                ["value"] = RenderCatalogValue(value),
                ["data_type"] = GetDataTypeName(value),
                ["scope"] = scope,
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

    /// <summary>
    /// Renders a variable's value for the catalog's text <c>value</c> column. Invariant formatting
    /// so the view reads the same regardless of the host's culture — a decimal must not come back
    /// with a comma separator on one machine and a point on another.
    /// </summary>
    private static string? RenderCatalogValue(object? value) => value switch
    {
        null => null,
        string text => text,
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

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
        "plan_rejected", "plan_degraded", "plan_fallback_summary",
        // What the data-quality rules on this statement cost. The run-level tallies in
        // eng.data_quality_status say what the rules found; these say what they cost here, which is
        // the question when a load has slowed and rules are what changed.
        "dq_rows_validated", "dq_rows_quarantined", "dq_rows_warned", "dq_validation_ms",
        // Large-dataset execution. These counters existed since the spill/partition work but never
        // reached the profile, so the surface an operator profiles with described the pre-spill
        // engine: it reported bytes written and never bytes read back, partitions and never passes.
        "spill_read_bytes", "spill_extents", "partition_passes",
        "aggregate_groups", "aggregate_expansion_ratio", "sort_spills", "cpu_time_ms"
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
                ["plan_fallback_summary"] = planFallbackSummary,
                ["dq_rows_validated"] = metric.DataQualityRowsValidated,
                ["dq_rows_quarantined"] = metric.DataQualityRowsQuarantined,
                ["dq_rows_warned"] = metric.DataQualityRowsWarned,
                ["dq_validation_ms"] = metric.DataQualityValidationMs,
                ["spill_read_bytes"] = metric.SpillReadBytes,
                ["spill_extents"] = metric.SpillExtentCount,
                ["partition_passes"] = metric.PartitionPassCount,
                ["aggregate_groups"] = metric.AggregateGroupsCount,
                ["aggregate_expansion_ratio"] = metric.AggregateExpansionRatio,
                ["sort_spills"] = metric.SortSpillCount,
                ["cpu_time_ms"] = metric.CpuTimeMs
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
    private static readonly string[] Columns = ["id", "job_name", "start_time", "end_time", "status", "rows_processed", "rows_warned", "rows_quarantined", "failed_rule_counts", "peak_ram_mb", "cpu_time_s", "error_message"];

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
            foreach (var entry in await (string.IsNullOrWhiteSpace(_jobNameFilter)
                ? _store.GetHistoryAsync(limit: Math.Max(batchSize, 1000))
                : _store.GetHistoryForNameAsync(null, _jobNameFilter, Math.Max(batchSize, 1000))))
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
                    ["rows_warned"] = entry.RowsWarned,
                    ["rows_quarantined"] = entry.RowsQuarantined,
                    ["failed_rule_counts"] = entry.DataQualityFailures,
                    ["peak_ram_mb"] = entry.PeakMemoryBytes / (1024.0 * 1024.0),
                    ["cpu_time_s"] = entry.CpuTimeSeconds,
                    ["error_message"] = entry.ErrorMessage == null ? null : SecretRedactor.Redact(entry.ErrorMessage)
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

public sealed class DataQualityStatusDataSource : IDataSource
{
    private readonly IExecutionContext _context;
    private readonly IJobHistoryStore? _store;
    private static readonly string[] Columns = ["run_id", "job_name", "start_time", "end_time", "status", "rows_processed", "rows_warned", "rows_quarantined", "warn_percent", "quarantine_percent", "failed_rule_count", "freshest_value_utc", "freshness_state", "error_summary", "source"];

    public DataQualityStatusDataSource(IExecutionContext context, IJobHistoryStore? store)
    {
        _context = context;
        _store = store;
    }

    public string Path => "eng.data_quality_status";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";
    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => ReadBatches(batchSize, CancellationToken.None);

    public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = new List<Row>();
        var currentMetrics = _context.DataQuality.ColumnMetrics;
        var freshest = currentMetrics.Where(m => m.MaxTimestampUtc.HasValue)
            .Select(m => m.MaxTimestampUtc!.Value).DefaultIfEmpty().Max();
        DateTimeOffset? currentFreshest = freshest == default ? null : freshest;
        rows.Add(BuildRow(new JobDataQualityStatus(
            _context.SessionId ?? "current", _context.JobName, _context.DataQuality.RunStartedAtUtc.UtcDateTime, null, "RUNNING",
            _context.Telemetry.RowsProcessed, _context.DataQuality.RowsWarned, _context.DataQuality.RowsQuarantined,
            _context.DataQuality.FailureMetrics.Count, currentFreshest,
            currentFreshest.HasValue ? "OBSERVED" : "NOT_TRACKED", null), "CURRENT_RUN"));

        if (_store != null)
        {
            foreach (var status in await _store.GetDataQualityStatusesAsync(Math.Max(batchSize, 1000)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                rows.Add(BuildRow(status, "ORCHESTRATOR"));
                if (rows.Count >= batchSize)
                {
                    yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
                    rows = [];
                }
            }
        }
        yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
    }

    private static Row BuildRow(JobDataQualityStatus status, string source)
    {
        var denominator = status.RowsProcessed <= 0 ? 0d : status.RowsProcessed;
        return new Row
        {
            ["run_id"] = status.RunId,
            ["job_name"] = status.JobName,
            ["start_time"] = status.StartTime,
            ["end_time"] = status.EndTime,
            ["status"] = status.Status,
            ["rows_processed"] = status.RowsProcessed,
            ["rows_warned"] = status.RowsWarned,
            ["rows_quarantined"] = status.RowsQuarantined,
            ["warn_percent"] = denominator == 0 ? 0d : status.RowsWarned * 100d / denominator,
            ["quarantine_percent"] = denominator == 0 ? 0d : status.RowsQuarantined * 100d / denominator,
            ["failed_rule_count"] = status.FailedRuleCount,
            ["freshest_value_utc"] = status.FreshestValueUtc,
            ["freshness_state"] = status.FreshnessState,
            ["error_summary"] = status.ErrorSummary,
            ["source"] = source
        };
    }

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => WriteBatches(batches, append, CancellationToken.None);
    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) => throw new NotSupportedException("eng.data_quality_status is read-only.");
    public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)Columns);
    public object? Snapshot() => null;
    public void Restore(object? snapshot) { }
    public IDataSource WithTable(string tableName) => this;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// <c>eng.job_statement_metrics</c> — the run flight recorder as an engine catalog table.
///
/// <para>Solo has no Portal, so the durable statement timeline has to be readable here or the
/// smallest deployment profile silently loses a capability Team gains. Same reason
/// <c>eng.job_history</c> and <c>eng.data_quality_failures</c> exist.</para>
///
/// <para>Live session rows come first with <c>source = 'CURRENT_RUN'</c>, matching
/// <c>eng.profile</c>; persisted rows follow as <c>'HISTORY'</c>. Column names match
/// <c>eng.profile</c> so one query shape reads either.</para>
/// </summary>
public sealed class JobStatementMetricsDataSource : IDataSource
{
    private readonly IExecutionContext _context;
    private readonly IJobHistoryStore? _store;
    private static readonly string[] Columns =
    [
        "run_id", "job_name", "start_time", "end_time", "status", "ordinal", "statement",
        "duration_ms", "rows_processed", "cpu_time_ms", "spilled_bytes", "spill_read_bytes",
        "partitions", "queue_wait_ms", "lock_wait_ms", "index_used",
        "dq_rows_validated", "dq_rows_quarantined", "dq_rows_warned", "dq_validation_ms",
        "failed", "source"
    ];

    public JobStatementMetricsDataSource(IExecutionContext context, IJobHistoryStore? store)
    {
        _context = context;
        _store = store;
    }

    public string Path => "eng.job_statement_metrics";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";
    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => ReadBatches(batchSize, CancellationToken.None);

    public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = new List<Row>();

        // The live session, so a Solo author reading their own run sees it without a store.
        var live = _context.Telemetry?.ProfileMetrics;
        if (live is not null)
        {
            for (var i = 0; i < live.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                rows.Add(ToRow(
                    _context.SessionId ?? "current", _context.JobName, null, null, "RUNNING", i,
                    ETL_SQL.Core.Profiling.StatementMetricsPayload.From(live[i]), "CURRENT_RUN"));
            }
        }

        if (_store != null)
        {
            foreach (var entry in await _store.GetStatementMetricsAsync(Math.Max(batchSize, 1000)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                rows.Add(ToRow(entry.RunId.ToString(), entry.JobName, entry.StartTime, entry.EndTime,
                    entry.Status, entry.Ordinal, entry.Statement, "HISTORY"));
            }
        }

        var table = new DataTable();
        table.SetColumns(Columns);
        foreach (var row in rows) await table.AddRowAsync(row);
        yield return table;
    }

    private static Row ToRow(
        string runId, string? jobName, DateTime? startTime, DateTime? endTime, string status,
        int ordinal, ETL_SQL.Core.Profiling.StatementMetricsPayload statement, string source) => new()
        {
            ["run_id"] = runId,
            ["job_name"] = jobName,
            ["start_time"] = startTime,
            ["end_time"] = endTime,
            ["status"] = status,
            ["ordinal"] = (decimal)ordinal,
            ["statement"] = statement.Statement,
            ["duration_ms"] = (decimal)statement.DurationMs,
            ["rows_processed"] = (decimal)statement.RowsProcessed,
            ["cpu_time_ms"] = (decimal)statement.CpuTimeMs,
            ["spilled_bytes"] = (decimal)statement.SpilledBytes,
            ["spill_read_bytes"] = (decimal)statement.SpillReadBytes,
            ["partitions"] = (decimal)statement.Partitions,
            ["queue_wait_ms"] = (decimal)statement.QueueWaitMs,
            ["lock_wait_ms"] = (decimal)statement.LockWaitMs,
            ["index_used"] = statement.IndexUsed,
            ["dq_rows_validated"] = (decimal)statement.DataQualityRowsValidated,
            ["dq_rows_quarantined"] = (decimal)statement.DataQualityRowsQuarantined,
            ["dq_rows_warned"] = (decimal)statement.DataQualityRowsWarned,
            ["dq_validation_ms"] = (decimal)statement.DataQualityValidationMs,
            ["failed"] = statement.Failed,
            ["source"] = source
        };

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) =>
        throw new NotSupportedException("eng.job_statement_metrics is read-only.");
    public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult<IEnumerable<string>>(Columns);
    public object? Snapshot() => null;
    public void Restore(object? snapshot) { }
    public IDataSource WithTable(string tableName) => this;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class DataQualityFailuresDataSource : IDataSource
{
    private readonly IExecutionContext _context;
    private readonly IJobHistoryStore? _store;
    private static readonly string[] Columns = ["run_id", "job_name", "start_time", "end_time", "status", "target_table", "column_name", "rule", "action", "failure_count", "owner", "source"];

    public DataQualityFailuresDataSource(IExecutionContext context, IJobHistoryStore? store)
    {
        _context = context;
        _store = store;
    }

    public string Path => "eng.data_quality_failures";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";
    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => ReadBatches(batchSize, CancellationToken.None);
    public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = _context.DataQuality.FailureMetrics.Select(f => new Row
        {
            ["run_id"] = _context.SessionId ?? "current",
            ["job_name"] = _context.JobName,
            ["start_time"] = _context.DataQuality.RunStartedAtUtc,
            ["end_time"] = null,
            ["status"] = "RUNNING",
            ["target_table"] = f.TargetTable,
            ["column_name"] = f.ColumnName,
            ["rule"] = f.Rule,
            ["action"] = f.Action,
            ["failure_count"] = f.FailureCount,
            ["owner"] = f.Owner,
            ["source"] = "CURRENT_RUN"
        }).ToList();

        if (_store != null)
        {
            foreach (var f in await _store.GetDataQualityFailuresAsync(Math.Max(batchSize, 1000)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                rows.Add(new Row
                {
                    ["run_id"] = f.RunId.ToString(),
                    ["job_name"] = f.JobName,
                    ["start_time"] = f.StartTime,
                    ["end_time"] = f.EndTime,
                    ["status"] = f.Status,
                    ["target_table"] = f.TargetTable,
                    ["column_name"] = f.ColumnName,
                    ["rule"] = f.Rule,
                    ["action"] = f.Action,
                    ["failure_count"] = f.FailureCount,
                    ["owner"] = f.Owner,
                    ["source"] = "ORCHESTRATOR"
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
    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) => throw new NotSupportedException("eng.data_quality_failures is read-only.");
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
            foreach (var entry in await _store.GetJobStatesAsync(limit: Math.Max(batchSize, 1000)))
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
    private static readonly string[] Columns = ["id", "process_id", "job_name", "acquired_at", "machine_name"];

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
                row["id"] = reader.GetInt64(0);
                row["process_id"] = reader.GetInt32(1);
                row["job_name"] = reader.GetString(2);
                row["acquired_at"] = reader.GetString(3);
                row["machine_name"] = reader.IsDBNull(4) ? "" : reader.GetString(4);
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
    private static readonly string[] Columns = ["session_id", "created", "last_modified", "size_mb", "temp_tables", "variables", "last_script", "user", "machine"];

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
            row["session_id"] = sess.SessionId;
            row["created"] = sess.CreatedAt;
            row["last_modified"] = sess.LastModifiedAt;
            row["size_mb"] = sess.SizeMB.HasValue ? (decimal)sess.SizeMB.Value : null;
            row["temp_tables"] = (decimal)sess.TempTableCount;
            row["variables"] = (decimal)sess.VariableCount;
            row["last_script"] = sess.LastScriptSource ?? "";
            row["user"] = sess.OwnerUser ?? "";
            row["machine"] = sess.OwnerMachine ?? "";
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
    private static readonly string[] Columns = ["id", "run_at", "job_name", "target_table", "target_column", "source_tables", "operation", "tags", "source_file", "line"];

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
                row["id"] = e.Id;
                row["run_at"] = e.RunAt;
                row["job_name"] = e.JobName;
                row["target_table"] = e.TargetTable;
                row["target_column"] = e.TargetColumn;
                row["source_tables"] = string.Join(", ", e.SourceTables);
                row["operation"] = e.Operation;
                row["tags"] = System.Text.Json.JsonSerializer.Serialize(e.Tags);
                row["source_file"] = e.SourceFile;
                row["line"] = (decimal)e.Line;
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
    private static readonly string[] Columns = ["target_table", "target_column", "missing_tags", "present_tags", "run_at", "job_name", "script_path"];

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
                row["target_table"] = e.TargetTable;
                row["target_column"] = e.TargetColumn;
                row["missing_tags"] = string.Join(", ", e.MissingTags.Select(t => "@" + t));
                row["present_tags"] = System.Text.Json.JsonSerializer.Serialize(e.PresentTags);
                row["run_at"] = e.RunAt;
                row["job_name"] = e.JobName;
                row["script_path"] = e.ScriptPath;
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
        "id", "run_at", "job_name", "target_table", "target_column", "source_tables", "operation",
        "protection_tags", "protection_reason", "owner", "steward", "contact", "domain",
        "classification", "quality", "tags", "source_file", "line"
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
                row["id"] = e.Id;
                row["run_at"] = e.RunAt;
                row["job_name"] = e.JobName;
                row["target_table"] = e.TargetTable;
                row["target_column"] = e.TargetColumn;
                row["source_tables"] = string.Join(", ", e.SourceTables);
                row["operation"] = e.Operation;
                row["protection_tags"] = string.Join(", ", e.ProtectionTags);
                row["protection_reason"] = e.ProtectionReason;
                row["owner"] = e.Owner;
                row["steward"] = e.Steward;
                row["contact"] = e.Contact;
                row["domain"] = e.Domain;
                row["classification"] = e.Classification;
                row["quality"] = e.Quality;
                row["tags"] = System.Text.Json.JsonSerializer.Serialize(e.Tags);
                row["source_file"] = e.SourceFile;
                row["line"] = (decimal)e.Line;
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
        "id", "run_at", "job_name", "target_table", "target_column", "source_tables", "source_columns",
        "suggested_tag", "suggested_value", "confidence", "evidence_kind", "evidence", "reason",
        "existing_tags", "source_file", "line"
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
                row["id"] = e.Id;
                row["run_at"] = e.RunAt;
                row["job_name"] = e.JobName;
                row["target_table"] = e.TargetTable;
                row["target_column"] = e.TargetColumn;
                row["source_tables"] = string.Join(", ", e.SourceTables);
                row["source_columns"] = string.Join(", ", e.SourceColumns);
                row["suggested_tag"] = e.SuggestedTag;
                row["suggested_value"] = e.SuggestedValue;
                row["confidence"] = e.Confidence.ToString();
                row["evidence_kind"] = e.EvidenceKind;
                row["evidence"] = e.Evidence;
                row["reason"] = e.Reason;
                row["existing_tags"] = System.Text.Json.JsonSerializer.Serialize(e.ExistingTags);
                row["source_file"] = e.SourceFile;
                row["line"] = (decimal)e.Line;
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
        "target_table", "target_column", "rule_tag", "rule", "action", "source_file", "line"
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
                    row["target_table"] = entry.TargetTable;
                    row["target_column"] = entry.TargetColumn;
                    row["rule_tag"] = "@" + binding.ExpectKey;
                    row["rule"] = rule.Text;
                    row["action"] = binding.Action.ToString().ToUpperInvariant() + (binding.ActionExplicit ? "" : " (default)");
                    row["source_file"] = entry.SourceFile;
                    row["line"] = (decimal)entry.Line;
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

public sealed class StewardshipScoreDataSource : IDataSource
{
    private readonly Evaluator _evaluator;
    private readonly ILineageCatalogStore? _catalog;
    private readonly Microsoft.Extensions.Configuration.IConfiguration? _config;
    private static readonly string[] Columns = ["scope_type", "scope_name", "component", "numerator", "denominator", "percentage", "asset_count", "column_count", "weight", "evaluated_at_utc", "definition_version"];

    public StewardshipScoreDataSource(Evaluator evaluator, ILineageCatalogStore? catalog, Microsoft.Extensions.Configuration.IConfiguration? config)
    {
        _evaluator = evaluator;
        _catalog = catalog;
        _config = config;
    }

    public string Path => "eng.stewardship_score";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";
    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => ReadBatches(batchSize, CancellationToken.None);
    public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var evaluation = await EvaluateAsync(cancellationToken);
        var rows = evaluation.Scores.Select(s => new Row
        {
            ["scope_type"] = s.ScopeType,
            ["scope_name"] = s.ScopeName,
            ["component"] = s.Component,
            ["numerator"] = s.Numerator,
            ["denominator"] = s.Denominator,
            ["percentage"] = s.Percentage,
            ["asset_count"] = s.AssetCount,
            ["column_count"] = s.ColumnCount,
            ["weight"] = s.Weight,
            ["evaluated_at_utc"] = s.EvaluatedAtUtc,
            ["definition_version"] = s.DefinitionVersion
        });
        yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
    }

    internal async Task<StewardshipEvaluation> EvaluateAsync(CancellationToken cancellationToken)
    {
        // Current-session lineage is first so it wins deterministic de-duplication; durable
        // history is already ordered newest-first by the catalog store.
        var assets = StewardshipScoring.FromCurrent(
            _evaluator.LineageTracker.GetFullLineage(), _evaluator.JobName, _evaluator.CurrentScriptPath).ToList();
        if (_catalog != null)
        {
            var limit = int.TryParse(_config?["Engine:DefaultHistoryLimit"], out var value) ? value : 1000;
            cancellationToken.ThrowIfCancellationRequested();
            assets.AddRange(StewardshipScoring.FromHistory(await _catalog.GetRecentLineageAsync(Math.Max(limit * 20, 1000))));
        }
        return StewardshipScoring.Evaluate(assets, LoadPolicy(_evaluator.CurrentScriptPath));
    }

    internal static WorkspacePolicyDocument? LoadPolicy(string? scriptPath)
    {
        var start = string.IsNullOrWhiteSpace(scriptPath) ? Environment.CurrentDirectory : System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(scriptPath))!;
        var path = WorkspacePolicyLoader.Find(start);
        if (path == null) return null;
        var result = WorkspacePolicyLoader.Load(path);
        if (!result.IsValid)
            throw new ExecutionException($"Workspace policy '{path}' is invalid: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}");
        return result.Policy;
    }

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => throw new NotSupportedException("eng.stewardship_score is read-only.");
    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) => throw new NotSupportedException("eng.stewardship_score is read-only.");
    public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)Columns);
    public object? Snapshot() => null;
    public void Restore(object? snapshot) { }
    public IDataSource WithTable(string tableName) => this;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class StewardshipGapsDataSource : IDataSource
{
    private readonly StewardshipScoreDataSource _scores;
    private static readonly string[] Columns = ["scope_type", "scope_name", "component", "target_table", "target_column", "requirement", "source_file", "line", "evaluated_at_utc", "definition_version"];

    public StewardshipGapsDataSource(Evaluator evaluator, ILineageCatalogStore? catalog, Microsoft.Extensions.Configuration.IConfiguration? config) =>
        _scores = new StewardshipScoreDataSource(evaluator, catalog, config);
    public string Path => "eng.stewardship_gaps";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";
    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => ReadBatches(batchSize, CancellationToken.None);
    public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var evaluation = await _scores.EvaluateAsync(cancellationToken);
        var rows = evaluation.Gaps.Select(g => new Row
        {
            ["scope_type"] = g.ScopeType,
            ["scope_name"] = g.ScopeName,
            ["component"] = g.Component,
            ["target_table"] = g.TargetTable,
            ["target_column"] = g.TargetColumn,
            ["requirement"] = g.Requirement,
            ["source_file"] = g.SourceFile,
            ["line"] = g.Line,
            ["evaluated_at_utc"] = g.EvaluatedAtUtc,
            ["definition_version"] = g.DefinitionVersion
        });
        yield return await EngineCatalogTableBuilder.BuildAsync(Columns, rows);
    }
    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => throw new NotSupportedException("eng.stewardship_gaps is read-only.");
    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) => throw new NotSupportedException("eng.stewardship_gaps is read-only.");
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
