using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the SHOW TABLES statement, listing tables in a data source.
    /// </summary>
    public class ShowTablesStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ShowTablesStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ShowTablesStatement)statement;

            var table = new DataTable();
            table.AddColumn("TableName");
            table.AddColumn("Type");

            IEnumerable<IDataSource> sourcesToQuery;
            if (stmt.ConnectionName != null)
            {
                var conn = context.Connections.FirstOrDefault(c => c.Key.Equals(stmt.ConnectionName, StringComparison.OrdinalIgnoreCase)).Value;
                if (conn == null)
                {
                    var available = string.Join(", ", context.Connections.Keys);
                    throw new ExecutionException($"Connection '{stmt.ConnectionName}' not found in the current session. Available: [{available}]");
                }
                sourcesToQuery = new[] { conn };
            }
            else
            {
                sourcesToQuery = context.Connections.Values;
            }

            foreach (var source in sourcesToQuery)
            {
                var tables = await source.GetTablesAsync();
                foreach (var t in tables)
                {
                    var row = new Row();
                    row["TableName"] = t;
                    row["Type"] = source.GetType().Name;
                    await table.AddRowAsync(row);
                }
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
                if (table.Rows.Count == 0)
                {
                    context.Log("0 rows returned.", ConsoleColor.Cyan);
                }
                context.LastResult = table;
                context.LastResultSets.Add(table);
            }
        }
    }
}
