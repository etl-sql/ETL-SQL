using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Services;

/// <summary>Moves append-only temporary tables to the established mutable row store when required.</summary>
internal static class TempTableStorageRouter
{
    public static async Task<IDataSource> EnsureMutableAsync(
        IExecutionContext context,
        string connectionName,
        IDataSource source,
        string operation)
    {
        if (source is not AppendOnlyColumnDataSource columnar)
            return source;

        if (context.TranCount > 0)
        {
            throw new ExecutionException(
                $"{operation} cannot downgrade columnar temporary table {connectionName} inside an active transaction. " +
                "Create the table with a row-store-only schema or perform the operation outside the transaction.");
        }

        var rowStore = new InMemoryDataSource
        {
            ExecutionContext = context,
            Validator = context as IDataValidator,
            MaxInMemoryBatches = context.MaxInMemoryBatches
        };
        rowStore.SetSchema(columnar.LogicalSchema.Values, columnar.TableConstraints);

        try
        {
            await rowStore.WriteBatches(columnar.ReadBatches(context.BatchSize));
            if (!context.Connections.TryGetValue(connectionName, out var current) || !ReferenceEquals(current, source))
                throw new ExecutionException($"Temporary table {connectionName} changed while its storage was being downgraded.");
        }
        catch
        {
            await rowStore.DisposeAsync();
            throw;
        }

        context.Connections[connectionName] = rowStore;
        await columnar.DisposeAsync();
        return rowStore;
    }
}
