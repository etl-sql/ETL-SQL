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
