using System.Threading.Tasks;
using System.Linq;
using ETL_SQL.Data;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the EXECUTE ON {connection} { ... } statement, pushing an entire block of statements to a remote connection for translation and execution.
    /// </summary>
    public class ExecuteRemoteBlockStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ExecuteRemoteBlockStatement);
        /// <summary>Executes the remote block, translating each inner statement to the target dialect.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ExecuteRemoteBlockStatement)statement;
            

            var connNameObj = await context.EvaluateValue(stmt.ConnectionName, new Row());
            string connName = connNameObj?.ToString() ?? "";
            Logger.Verbose($"Executing remote block on {connName}");

            if (context.Connections.TryGetValue(connName, out var source) && source is IDatabaseSource db)
            {
                context.Log($"Executing remote block on {connName}...");
                
                foreach (var innerStmt in stmt.Body.Statements)
                {
                    string sql = context.CompileQuery(innerStmt, db.Dialect);
                    if (string.IsNullOrWhiteSpace(sql)) continue;

                    context.Log($"Remote SQL: {sql}");
                    context.LastResultSets.Clear();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var batches = db.ExecuteRawSql(sql);
                    
                    var currentKey = -1;
                    DataTable? currentSet = null;

                    await foreach (var batch in batches)
                    {
                        if (batch.ResultSetIndex != currentKey)
                        {
                            if (currentSet != null) context.LastResultSets.Add(currentSet);
                            currentSet = new DataTable { ResultSetIndex = batch.ResultSetIndex };
                            currentKey = batch.ResultSetIndex;
                        }
                        
                        if (currentSet!.ColumnNames.Count == 0) currentSet.SetColumns(batch.ColumnNames);
                        foreach (var r in batch.Rows) currentSet.AddRow(r);
                    }
                    if (currentSet != null) context.LastResultSets.Add(currentSet);

                    sw.Stop();
                    if (context.LastResultSets.Count > 0)
                    {
                        context.LastResult = context.LastResultSets[^1];
                        foreach(var rs in context.LastResultSets) {
                            rs.ExecutionTimeMs = sw.ElapsedMilliseconds / context.LastResultSets.Count; // Approximate
                            rs.TotalRowsMatched = rs.Rows.Count;
                        }
                    }
                }
            }
            else
            {
                throw new System.Exception($"Connection '{connName}' not found or does not support remote block execution.");
            }
        }
    }
}



