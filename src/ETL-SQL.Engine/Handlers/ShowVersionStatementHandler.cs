using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the SHOW VERSION statement, displaying engine version and metadata.
    /// </summary>
    public class ShowVersionStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ShowVersionStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ShowVersionStatement)statement;
            
            var table = new DataTable();
            table.AddColumn("Component");
            table.AddColumn("Version");
            table.AddColumn("Metadata");

            var row = new Row();
            row["Component"] = "ETL-SQL Engine";
            row["Version"] = LanguageMetadata.EngineVersion;
            row["Metadata"] = ".NET 10.0; AmericanSuperstar (c) 2026";
            await table.AddRowAsync(row);

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
