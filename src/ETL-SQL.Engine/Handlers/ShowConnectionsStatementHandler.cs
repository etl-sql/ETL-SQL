using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Engine;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the SHOW CONNECTIONS statement, listing all active data sources.
/// </summary>
public class ShowConnectionsStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(ShowConnectionsStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ShowConnectionsStatement)statement;

        var table = new DataTable();
        table.AddColumn("Name");
        table.AddColumn("Type");
        table.AddColumn("Details");

        foreach (var conn in context.Connections)
        {
            var row = new Row();
            row["Name"] = conn.Key;
            row["Type"] = conn.Value.GetType().Name;
            row["Details"] = conn.Value.ToString();
            await table.AddRowAsync(row);
        }

        if (stmt.IntoTable != null)
        {
            await WriteToTempTable(stmt.IntoTable, table, context);
        }
        else
        {
            if (table.Rows.Count == 0)
            {
                context.Log("0 rows returned.", ConsoleColor.Cyan);
            }
            else
            {
                if (!context.RedirectOutput)
                {
                    ResultFormatter.PrintTable(table);
                }
            }

            context.LastResult = table;
            context.LastResultSets.Add(table);
            context.OnResultSet?.Invoke(table);
        }
    }

    private async Task WriteToTempTable(string tableName, DataTable table, IExecutionContext context)
    {
        if (!context.Connections.ContainsKey(tableName))
        {
            context.Connections[tableName] = new InMemoryDataSource();
        }
        var destination = await context.ResolveDataSourceAsync(new TableReference(tableName));
        await destination.WriteBatches(new[] { table }.ToAsyncEnumerable());
    }
}
