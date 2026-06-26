using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Engine.Services;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the SHOW SESSIONS statement, listing all managed session files.
/// </summary>
public class ShowSessionsStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(ShowSessionsStatement);
    private readonly ETL_SQL.Core.Execution.ISessionStateManager _sessionManager;

    public ShowSessionsStatementHandler(ETL_SQL.Core.Execution.ISessionStateManager sessionManager)
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
        table.AddColumn("User");
        table.AddColumn("Machine");

        var sessions = _sessionManager.GetSessions().OrderByDescending(s => s.LastModifiedAt);

        foreach (var sess in sessions)
        {
            var row = new Row();
            row["SessionId"] = sess.SessionId;
            row["Created"] = sess.CreatedAt;
            row["LastModified"] = sess.LastModifiedAt;
            row["Size_MB"] = sess.SizeMB.HasValue ? (decimal)sess.SizeMB.Value : null;
            row["TempTables"] = sess.TempTableCount;
            row["Variables"] = sess.VariableCount;
            row["LastScript"] = sess.LastScriptSource ?? "";
            row["User"] = sess.OwnerUser ?? "";
            row["Machine"] = sess.OwnerMachine ?? "";
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
