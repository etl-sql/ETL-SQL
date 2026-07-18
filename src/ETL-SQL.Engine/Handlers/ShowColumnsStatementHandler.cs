using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the SHOW COLUMNS statement, listing columns for a specific table.
/// </summary>
public class ShowColumnsStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(ShowColumnsStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ShowColumnsStatement)statement;

        var source = await context.ResolveDataSourceAsync(stmt.Table);
        if (source == null)
            throw new ExecutionException($"Table '{stmt.Table.ToSql()}' not found.");

        var columns = await source.GetColumnsAsync(context.CancellationToken);

        var table = new DataTable();
        table.AddColumn("ColumnName");
        table.AddColumn("DataType");

        foreach (var col in columns)
        {
            var row = new Row();
            row["ColumnName"] = col;
            // DataType info might not be available in all IDataSource implementations yet
            row["DataType"] = "UNKNOWN";
            await table.AddRowAsync(row);
        }

        if (stmt.IntoTable != null)
        {
            if (!context.Connections.ContainsKey(stmt.IntoTable))
            {
                context.Connections[stmt.IntoTable] = new InMemoryDataSource();
            }
            var destination = await context.ResolveDataSourceAsync(new TableReference(stmt.IntoTable));
            await destination.WriteBatches(new[] { table }.ToAsyncEnumerable());
        }
        else
        {
            context.LastResult = table;
            context.LastResultSets.Add(table);
        }
    }
}
