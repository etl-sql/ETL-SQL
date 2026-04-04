using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the EXEC statement for dynamic SQL execution, either locally or on a remote connection.
    /// </summary>
    public class ExecStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ExecStatement);
        /// <summary>Executes the dynamic SQL statement, handling both local and remote execution paths.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ExecStatement)statement;
            
            var sqlObj = await context.EvaluateValue(stmt.SqlExpression, new Row());
            if (sqlObj == null) return;
            string sql = sqlObj.ToString() ?? "";

            if (stmt.ConnectionName != null)
            {
                var connNameObj = await context.EvaluateValue(stmt.ConnectionName, new Row());
                string connName = connNameObj?.ToString() ?? "";
                
                if (context.Connections.TryGetValue(connName, out var source) && source is IDatabaseSource db)
                {
                    context.Log($"Executing remote SQL on {connName}...");
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
                        foreach (var rs in context.LastResultSets)
                        {
                            rs.ExecutionTimeMs = sw.ElapsedMilliseconds / context.LastResultSets.Count;
                            rs.TotalRowsMatched = rs.Rows.Count;
                        }
                    }
                }
                else
                {
                    throw new System.Exception($"Connection '{connName}' not found or does not support remote execution.");
                }
            }
            else
            {
                var parser = new Parser(new Lexer(sql).Tokenize());
                var script = parser.Parse();
                await context.Evaluate(script);
            }
        }
    }
}
