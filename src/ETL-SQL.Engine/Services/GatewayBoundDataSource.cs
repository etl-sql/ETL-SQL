using System.Runtime.CompilerServices;
using System.Text.Json;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Services;

/// <summary>IDataSource facade whose only transport is an authorized typed Gateway operation.</summary>
internal sealed class GatewayBoundDataSource(
    IGatewayOperationRouter router,
    ExecutionIdentity identity,
    GatewayResourceBinding binding,
    string connectorType,
    string? tableName = null) : IDataSource
{
    public string Path => $"GATEWAY:{binding.GatewayId}/{binding.ResourceId}";
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => connectorType;

    public async IAsyncEnumerable<DataTable> ReadBatches(
        int batchSize = 10_000,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var table = RequireTable();
        var bounds = GatewayOperationBounds.Default with
        {
            MaxRows = Math.Max(1, batchSize),
            BufferedBatchLimit = 1
        };
        var result = await router.ExecuteAsync(
            identity, binding, GatewayOperationClass.Read, GatewayOperationEffect.ReadOnly, bounds,
            JsonSerializer.Serialize(new { Table = table }), null, cancellationToken).ConfigureAwait(false);
        var output = new DataTable();
        output.SetColumns(result.Columns);
        foreach (var values in result.Rows)
            await output.AddRowAsync(new Row(output.Schema, values.Cast<object?>().ToArray())).ConfigureAwait(false);
        output.IsCapped = result.Truncated;
        yield return output;
    }

    IAsyncEnumerable<DataTable> IDataSource.ReadBatches(int batchSize) => ReadBatches(batchSize);

    public async Task WriteBatches(
        IAsyncEnumerable<DataTable> batches, bool append = false, CancellationToken cancellationToken = default)
    {
        var table = RequireTable();
        await foreach (var batch in batches.WithCancellation(cancellationToken))
        {
            var columns = batch.ColumnNames;
            var rows = batch.Rows.Select(row =>
                (IReadOnlyList<string?>)columns.Select(column => Convert.ToString(row[column])).ToArray()).ToList();
            await router.ExecuteAsync(
                identity, binding, GatewayOperationClass.Write, GatewayOperationEffect.Mutating,
                GatewayOperationBounds.Default,
                JsonSerializer.Serialize(new { Table = table, Columns = columns, Rows = rows, Append = append }),
                null, cancellationToken).ConfigureAwait(false);
        }
    }

    Task IDataSource.WriteBatches(IAsyncEnumerable<DataTable> batches, bool append) =>
        WriteBatches(batches, append);

    public async Task<IEnumerable<string>> GetColumnsAsync(CancellationToken cancellationToken)
    {
        var result = await router.ExecuteAsync(
            identity, binding, GatewayOperationClass.Read, GatewayOperationEffect.ReadOnly,
            GatewayOperationBounds.Default with { MaxRows = 1 },
            JsonSerializer.Serialize(new { Table = RequireTable() }), null, cancellationToken).ConfigureAwait(false);
        return result.Columns;
    }

    public Task<IEnumerable<string>> GetColumnsAsync() => GetColumnsAsync(CancellationToken.None);
    public IDataSource WithTable(string table) => new GatewayBoundDataSource(router, identity, binding, connectorType, table);
    public object? Snapshot() => null;
    public void Restore(object? snapshot) { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private string RequireTable()
    {
        if (string.IsNullOrWhiteSpace(tableName)
            || !tableName.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
            throw new InvalidOperationException("A Gateway-bound operation requires a valid table name.");
        return tableName;
    }
}
