using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the SHOW PROFILE statement, displaying captured performance metrics for previous operations.
/// </summary>
public class ShowProfileStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(ShowProfileStatement);

    /// <summary>Executes the SHOW PROFILE statement, rendering a detailed performance table.</summary>
    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ShowProfileStatement)statement;

        if (context.Telemetry.ProfileMetrics.Count == 0)
        {
            if (!context.RedirectOutput && stmt.IntoTable == null)
            {
                context.Log("No profiling data captured. Ensure SET PROFILE ON; is called before your logic.", ConsoleColor.Yellow);
            }
            return;
        }

        // Create a DataTable for potential INTO or redirect
        var dataTable = new DataTable();
        dataTable.AddColumn("Timestamp");
        dataTable.AddColumn("Statement");
        dataTable.AddColumn("RowsProcessed");
        dataTable.AddColumn("IndexUsed");
        dataTable.AddColumn("DurationMs");
        dataTable.AddColumn("MemoryKB");
        dataTable.AddColumn("SpilledBytes");
        dataTable.AddColumn("SubqHits");
        dataTable.AddColumn("SubqMisses");
        dataTable.AddColumn("SubqSpilled");
        dataTable.AddColumn("Partitions");
        dataTable.AddColumn("QueueWaitMs");
        dataTable.AddColumn("LockWaitMs");

        foreach (var m in context.Telemetry.ProfileMetrics)
        {
            var row = new Row();
            row["Timestamp"] = m.Timestamp;
            row["Statement"] = m.Sql;
            row["RowsProcessed"] = m.RowsProcessed;
            row["IndexUsed"] = m.IndexName ?? "--";
            row["DurationMs"] = m.DurationMs;
            row["MemoryKB"] = m.MemoryDeltaBytes / 1024.0;
            row["SpilledBytes"] = m.SpilledBytes;
            row["SubqHits"] = m.SubqueryCacheHits;
            row["SubqMisses"] = m.SubqueryCacheMisses;
            row["SubqSpilled"] = m.SubquerySpilledBytes;
            row["Partitions"] = m.PartitionsCount;
            row["QueueWaitMs"] = m.QueueWaitMs;
            row["LockWaitMs"] = m.LockWaitMs;
            await dataTable.AddRowAsync(row);
        }

        if (stmt.IntoTable != null)
        {
            if (!context.Connections.ContainsKey(stmt.IntoTable))
            {
                context.Connections[stmt.IntoTable] = new InMemoryDataSource();
            }
            var destination = await context.ResolveDataSourceAsync(new TableReference(stmt.IntoTable));
            await destination.WriteBatches(new[] { dataTable }.ToAsyncEnumerable());
        }
        else
        {
            context.LastResult = dataTable;
            context.LastResultSets.Add(dataTable);
        }

        await Task.CompletedTask;
    }
}
