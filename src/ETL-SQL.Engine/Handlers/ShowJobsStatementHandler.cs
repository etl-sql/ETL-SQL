using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the SHOW JOBS statement, listing all active scheduled tasks.
    /// </summary>
    public class ShowJobsStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ShowJobsStatement);
        private readonly IJobHistoryStore _store;

        public ShowJobsStatementHandler(IJobHistoryStore store)
        {
            _store = store;
        }

        /// <summary>Executes the SHOW JOBS statement, querying the job store and returning definitions as a table.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ShowJobsStatement)statement;

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

            var jobs = await _store.GetAllJobsAsync();
            
            var table = new DataTable();
            table.AddColumn("Name");
            table.AddColumn("Schedule");
            table.AddColumn("LastRun");
            table.AddColumn("NextRun");
            table.AddColumn("Script");
            table.AddColumn("Enable");

            foreach (var job in jobs)
            {
                var row = new Row();
                row["Name"] = job.Name;
                row["Schedule"] = $"EVERY {job.Interval} {job.Unit}" + (job.AtTime != null ? $" AT {job.AtTime}" : "");
                row["LastRun"] = job.LastRun;
                row["NextRun"] = job.NextRun;
                row["Script"] = job.Script;
                row["Enable"] = job.IsEnabled ? 1 : 0;
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
                if (table.Rows.Count == 0)
                {
                    context.Log("0 rows returned.", ConsoleColor.Cyan);
                }
                context.LastResult = table;
                context.LastResultSets.Add(table);
                context.OnResultSet?.Invoke(table);
            }
        }
    }
}
