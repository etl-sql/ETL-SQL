using System;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;

/// <summary>
/// Handles SHOW HOST METRICS [nodeId] [INTO #t] — the host-utilization time series for capacity
/// planning (memory load %, CPU %, and free disk on the state/spill volumes). Returns samples from
/// the last 24 hours, newest first. See Docs/Design/HostUtilizationAndCapacityPlanning.md.
/// </summary>
public class ShowHostMetricsStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(ShowHostMetricsStatement);
    private readonly IHostMetricsStore _store;

    public ShowHostMetricsStatementHandler(IHostMetricsStore store)
    {
        _store = store;
    }

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ShowHostMetricsStatement)statement;

        var samples = await _store.GetHostMetricsAsync(stmt.NodeId, DateTime.UtcNow.AddDays(-1));

        var table = new DataTable();
        table.AddColumn("NodeId");
        table.AddColumn("CapturedAt");
        table.AddColumn("MemoryLoadPercent");
        table.AddColumn("ProcessCpuPercent");
        table.AddColumn("HostCpuPercent");
        table.AddColumn("StateDiskFreeMB");
        table.AddColumn("SpillDiskFreeMB");

        foreach (var s in samples)
        {
            var row = new Row();
            row["NodeId"] = s.NodeId;
            row["CapturedAt"] = s.CapturedAt;
            row["MemoryLoadPercent"] = s.MemoryLoadPercent;
            row["ProcessCpuPercent"] = s.ProcessCpuPercent;
            row["HostCpuPercent"] = s.HostCpuPercent;
            row["StateDiskFreeMB"] = s.StateDiskFreeBytes / (1024.0 * 1024.0);
            row["SpillDiskFreeMB"] = s.SpillDiskFreeBytes / (1024.0 * 1024.0);
            await table.AddRowAsync(row);
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
            context.LastResultSets.Add(table);
            context.OnResultSet?.Invoke(table);
        }
    }
}
