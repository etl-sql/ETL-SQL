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
            var jobs = await _store.GetActiveJobsAsync();
            
            var table = new DataTable();
            table.AddColumn("Name");
            table.AddColumn("Schedule");
            table.AddColumn("LastRun");
            table.AddColumn("NextRun");
            table.AddColumn("Script");

            foreach (var job in jobs)
            {
                var row = new Row();
                row["Name"] = job.Name;
                row["Schedule"] = $"EVERY {job.Interval} {job.Unit}" + (job.AtTime != null ? $" AT {job.AtTime}" : "");
                row["LastRun"] = job.LastRun;
                row["NextRun"] = job.NextRun;
                row["Script"] = job.Script;
                table.AddRow(row);
            }

            context.LastResult = table;
        }
    }
}



