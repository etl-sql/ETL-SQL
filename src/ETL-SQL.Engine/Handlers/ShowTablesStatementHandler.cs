using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Core.Common.Exceptions;

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
                if (!context.Connections.TryGetValue(stmt.ConnectionName, out var source))
                    throw new ExecutionException($"Connection '{stmt.ConnectionName}' not found.");
                sourcesToQuery = new[] { source };
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
                    table.AddRow(row);
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
                context.LastResult = table;
            }
        }
    }
}
