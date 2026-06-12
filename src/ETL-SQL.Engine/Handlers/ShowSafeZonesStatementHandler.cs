using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Services;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// SEC-5: Handles the SHOW SAFE ZONES statement.
    /// Lists all approved security safe zones from SecurityService as a DataTable.
    /// </summary>
    public class ShowSafeZonesStatementHandler(SecurityService securityService) : IStatementHandler
    {
        private readonly SecurityService _securityService = securityService;

        public Type SupportedStatementType => typeof(ShowSafeZonesStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ShowSafeZonesStatement)statement;

            var table = new DataTable();
            table.AddColumn("Path");
            table.AddColumn("IsSystemPath");
            table.AddColumn("Resolution");

            foreach (var zone in _securityService.ApprovedSafeZones.OrderBy(z => z))
            {
                var row = new Row();
                row["Path"] = zone;
                row["IsSystemPath"] = _securityService.IsSystemPath(zone);
                row["Resolution"] = "Authorized";
                await table.AddRowAsync(row);
            }

            if (stmt.IntoTable != null)
            {
                await WriteToTempTable(stmt.IntoTable, table, context);
            }
            else
            {
                context.LastResult = table;
                context.LastResultSets.Add(table);
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
