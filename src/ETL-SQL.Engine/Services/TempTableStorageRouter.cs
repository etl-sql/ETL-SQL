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

        var rowStore = new InMemoryDataSource
        {
            ExecutionContext = context,
            Validator = context as IDataValidator,
            MaxInMemoryBatches = context.MaxInMemoryBatches
        };
        rowStore.SetSchema(columnar.LogicalSchema.Values, columnar.TableConstraints);

        try
        {
            await rowStore.WriteBatches(columnar.ReadBatches(context.EffectiveBatchSize));
            if (!context.Connections.TryGetValue(connectionName, out var current) || !ReferenceEquals(current, source))
                throw new ExecutionException($"Temporary table {connectionName} changed while its storage was being downgraded.");
        }
        catch
        {
            await rowStore.DisposeAsync();
            throw;
        }

        if (context.TranCount > 0)
        {
            if (context is not Evaluator evaluator)
            {
                await rowStore.DisposeAsync();
                throw new ExecutionException(
                    $"{operation} cannot transactionally downgrade temporary table {connectionName} in this execution context.");
            }
            evaluator.ReplaceDataSourceForTransaction(connectionName, columnar, rowStore);
        }
        else
        {
            context.Connections[connectionName] = rowStore;
            await columnar.DisposeAsync();
        }
        return rowStore;
    }
}
