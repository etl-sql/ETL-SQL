using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the SHOW JOB HISTORY statement, providing an audit trail of job executions.
    /// </summary>
    public class ShowJobHistoryStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ShowJobHistoryStatement);
        private readonly IJobHistoryStore _store;

        public ShowJobHistoryStatementHandler(IJobHistoryStore store)
        {
            _store = store;
        }

        /// <summary>Executes the SHOW JOB HISTORY statement, retrieving and formatting execution logs.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ShowJobHistoryStatement)statement;

            if (stmt.At != null)
            {
                // Robust multi-pass lookup
                IDataSource? conn = null;

                // 1. Exact match
                if (context.Connections.TryGetValue(stmt.At, out conn)) { }
                // 2. Case-insensitive match
                else
                {
                    conn = context.Connections.FirstOrDefault(c => c.Key.Equals(stmt.At, StringComparison.OrdinalIgnoreCase)).Value;
                }

                if (conn == null)
                {
                    var available = string.Join(", ", context.Connections.Keys);
                    throw new ETL_SQL.Core.Common.Exceptions.ExecutionException($"Connection '{stmt.At}' not found in current session. Registered connections: [{available}]");
                }

                if (conn is not IPortalAdminConnection adminConn)
                    throw new ETL_SQL.Core.Common.Exceptions.ExecutionException($"Connection '{stmt.At}' (Type: {conn.ConnectorType}) does not support orchestrator operations.");

                await adminConn.ExecuteAdminStatementAsync(stmt, context);
                return;
            }

            var history = await _store.GetHistoryAsync(stmt.JobName);

            var table = new DataTable();
            table.AddColumn("Id");
            table.AddColumn("JobName");
            table.AddColumn("StartTime");
            table.AddColumn("EndTime");
            table.AddColumn("Status");
            table.AddColumn("RowsProcessed");
            table.AddColumn("PeakRAM_MB");
            table.AddColumn("CPUTime_s");
            table.AddColumn("ErrorMessage");

            foreach (var entry in history)
            {
                var row = new Row();
                row["Id"] = entry.Id;
                row["JobName"] = entry.JobName;
                row["StartTime"] = entry.StartTime;
                row["EndTime"] = entry.EndTime;
                row["Status"] = entry.Status;
                row["RowsProcessed"] = entry.RowsProcessed;
                row["PeakRAM_MB"] = entry.PeakMemoryBytes / (1024.0 * 1024.0);
                row["CPUTime_s"] = entry.CpuTimeSeconds;
                row["ErrorMessage"] = entry.ErrorMessage;
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
}
