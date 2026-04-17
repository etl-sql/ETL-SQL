using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Engine.Services;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the SHOW SESSIONS statement, listing all managed session files.
    /// </summary>
    public class ShowSessionsStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ShowSessionsStatement);
        private readonly SessionStateManager _sessionManager;

        public ShowSessionsStatementHandler(SessionStateManager sessionManager)
        {
            _sessionManager = sessionManager;
        }

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ShowSessionsStatement)statement;
            
            var table = new DataTable();
            table.AddColumn("SessionId");
            table.AddColumn("Created");
            table.AddColumn("LastModified");
            table.AddColumn("Size_MB");
            table.AddColumn("TempTables");
            table.AddColumn("Variables");
            table.AddColumn("LastScript");

            var sessions = _sessionManager.GetSessions().OrderByDescending(s => s.LastModifiedAt);

            foreach (var sess in sessions)
            {
                var row = new Row();
                row["SessionId"] = sess.SessionId;
                row["Created"] = sess.CreatedAt;
                row["LastModified"] = sess.LastModifiedAt;
                row["Size_MB"] = (decimal)sess.SizeMB;
                row["TempTables"] = sess.TempTableCount;
                row["Variables"] = sess.VariableCount;
                row["LastScript"] = sess.LastScriptSource ?? "";
                await table.AddRowAsync(row);
            }

            if (stmt.IntoTable != null)
            {
                await WriteToTempTable(stmt.IntoTable, table, context);
            }
            else
            {
                context.LastResult = table;
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
