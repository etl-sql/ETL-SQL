using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

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
