using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Engine.Handlers;

/// <summary>
/// Handles the SHOW LOCKS statement, listing all active database/job throttle slots.
/// </summary>
public class ShowLocksStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(ShowLocksStatement);
    private readonly IConfiguration _configuration;

    public ShowLocksStatementHandler(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ShowLocksStatement)statement;

        var table = new DataTable();
        table.AddColumn("Id");
        table.AddColumn("ProcessId");
        table.AddColumn("JobName");
        table.AddColumn("AcquiredAt");
        table.AddColumn("MachineName");

        var dbPath = _configuration["Orchestrator:DatabasePath"];
        var connectionString = $"Data Source={dbPath ?? GetDefaultDbPath()}";

        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        // Ensure ThrottleSlots table exists in case it hasn't been accessed yet
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ThrottleSlots (
                    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    ProcessId   INTEGER NOT NULL,
                    JobName     TEXT    NOT NULL,
                    AcquiredAt  TEXT    NOT NULL,
                    MachineName TEXT    DEFAULT ''
                );";
            await cmd.ExecuteNonQueryAsync();

            try
            {
                cmd.CommandText = "ALTER TABLE ThrottleSlots ADD COLUMN MachineName TEXT DEFAULT '';";
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
            {
                // Column already exists, ignore
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, ProcessId, JobName, AcquiredAt, MachineName FROM ThrottleSlots ORDER BY AcquiredAt DESC;";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Row();
                row["Id"] = reader.GetInt64(0);
                row["ProcessId"] = reader.GetInt32(1);
                row["JobName"] = reader.GetString(2);
                row["AcquiredAt"] = reader.GetString(3);
                row["MachineName"] = reader.IsDBNull(4) ? "" : reader.GetString(4);
                await table.AddRowAsync(row);
            }
        }

        if (stmt.IntoTable != null)
        {
            await WriteToTempTable(stmt.IntoTable, table, context);
        }
        else
        {
            if (table.Rows.Count == 0)
            {
                context.Log("0 rows returned (no active locks/throttle slots).", ConsoleColor.Cyan);
            }
            context.LastResult = table;
            context.LastResultSets.Add(table);
        }
    }

    private static string GetDefaultDbPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ETL-SQL");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "etlsql.db");
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
