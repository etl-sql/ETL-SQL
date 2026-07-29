using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Storage;

/// <summary>
/// Virtual data source that exposes current session column metadata through eng.columns.
/// </summary>
public class ColumnsDataSource : IDataSource
{
    private readonly IDictionary<string, IDataSource> _connections;
    private readonly ILineageTracker _tracker;
    private readonly List<string> _columns = new()
    {
        "table_name", "connection_name", "column_name", "data_type", "is_nullable", "tags"
    };

    public ColumnsDataSource(IDictionary<string, IDataSource> connections, ILineageTracker tracker)
    {
        _connections = connections;
        _tracker = tracker;
    }

    public string Path => "eng.columns";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "ENG";

    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
        ReadBatches(batchSize, CancellationToken.None);

    public async IAsyncEnumerable<DataTable> ReadBatches(
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rows = new List<Row>();

        foreach (var (name, source) in _connections.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var inMemory = source as InMemoryDataSource;
            if (inMemory != null || name.StartsWith("#") || name.StartsWith("&"))
            {
                await AddRows(rows, name, null, source, inMemory, cancellationToken);
                if (rows.Count >= batchSize)
                {
                    yield return await BuildBatch(rows);
                    rows = new List<Row>();
                }
                continue;
            }

            var tables = await source.GetTablesAsync(cancellationToken);
            foreach (var table in tables)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var scoped = source.WithTable(table);
                var tableName = $"{name}.{table}";
                await AddRows(rows, tableName, name, scoped, scoped as InMemoryDataSource, cancellationToken);
                if (rows.Count >= batchSize)
                {
                    yield return await BuildBatch(rows);
                    rows = new List<Row>();
                }
            }
        }

        if (rows.Count > 0)
            yield return await BuildBatch(rows);
    }

    private async Task AddRows(
        List<Row> rows,
        string tableName,
        string? connectionName,
        IDataSource source,
        InMemoryDataSource? inMemory,
        CancellationToken cancellationToken)
    {
        var sourceColumns = await source.GetColumnsAsync(cancellationToken);
        foreach (var column in sourceColumns)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(BuildRow(tableName, connectionName, column, inMemory));
        }
    }

    private Row BuildRow(string tableName, string? connectionName, string columnName, InMemoryDataSource? inMemory)
    {
        var row = new Row();
        row["table_name"] = tableName;
        row["connection_name"] = connectionName;
        row["column_name"] = columnName;

        var dataType = "UNKNOWN";
        var isNullable = "TRUE";
        if (inMemory != null && inMemory.Schema.TryGetValue(columnName, out var columnDefinition))
        {
            dataType = columnDefinition.DataType ?? "VARCHAR";
            isNullable = columnDefinition.IsNullable ? "TRUE" : "FALSE";
        }

        row["data_type"] = dataType;
        row["is_nullable"] = isNullable;
        row["tags"] = GetTags(tableName, columnName);
        return row;
    }

    private string GetTags(string tableName, string columnName)
    {
        var lineage = _tracker.GetFullLineage().FirstOrDefault(entry =>
            string.Equals(entry.TargetTable, tableName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.TargetColumn, columnName, StringComparison.OrdinalIgnoreCase));

        return lineage?.Metadata != null && lineage.Metadata.Count > 0
            ? JsonSerializer.Serialize(lineage.Metadata)
            : string.Empty;
    }

    private async Task<DataTable> BuildBatch(List<Row> rows)
    {
        var table = new DataTable();
        table.SetColumns(_columns);
        foreach (var row in rows)
            await table.AddRowAsync(row);
        return table;
    }

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) =>
        WriteBatches(batches, append, CancellationToken.None);

    public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) =>
        throw new NotSupportedException("eng.columns is read-only.");

    public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)_columns);
    public object? Snapshot() => null;
    public void Restore(object? snapshot) { }
    public IDataSource WithTable(string tableName) => this;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
