using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Engine;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the SHOW CONNECTION config statement, listing all redacted options for a connection.
    /// </summary>
    public class ShowConnectionConfigStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ShowConnectionConfigStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ShowConnectionConfigStatement)statement;
            
            if (!context.Connections.TryGetValue(stmt.ConnectionName, out var dataSource))
            {
                throw new ExecutionException($"Connection '{stmt.ConnectionName}' not found.", null, stmt.Line, stmt.Column);
            }

            var table = new DataTable();
            table.AddColumn("Option");
            table.AddColumn("Value");

            var config = dataSource.GetConfig();
            foreach (var kvp in config.OrderBy(k => k.Key))
            {
                var row = new Row();
                row["Option"] = kvp.Key;
                row["Value"] = kvp.Value;
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
}
