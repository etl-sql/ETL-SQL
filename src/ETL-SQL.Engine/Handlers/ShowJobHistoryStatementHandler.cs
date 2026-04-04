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
            var history = await _store.GetHistoryAsync(stmt.JobName);
            
            var table = new DataTable();
            table.AddColumn("Id");
            table.AddColumn("JobName");
            table.AddColumn("StartTime");
            table.AddColumn("EndTime");
            table.AddColumn("Status");
            table.AddColumn("RowsProcessed");
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
                row["ErrorMessage"] = entry.ErrorMessage;
                table.AddRow(row);
            }

            context.LastResult = table;
        }
    }
}



