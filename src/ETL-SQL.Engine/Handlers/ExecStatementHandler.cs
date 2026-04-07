using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Common;

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
                    var parameters = new List<object?>();
                    foreach (var paramExpr in stmt.Parameters)
                    {
                        parameters.Add(await context.EvaluateValue(paramExpr, new Row()));
                    }

                    context.Log($"Executing remote SQL on {connName}...");
                    if (context.IsWhatIf)
                    {
                        Logger.WriteLine($"WHAT IF: Would execute remote SQL on {connName}:\n{sql}", ConsoleColor.Yellow);
                        return;
                    }

                    context.LastResultSets.Clear();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var batches = db.ExecuteRawSql(sql, parameters);
                    
                    var results = new List<DataTable>();
                    await foreach (var batch in batches)
                    {
                        results.Add(batch);
                    }
                    
                    sw.Stop();
                    if (results.Count > 0)
                    {
                        context.LastResult = results[^1];
                        context.LastResultSets.Clear();
                        context.LastResultSets.AddRange(results);
                        
                        foreach (var rs in context.LastResultSets)
                        {
                            rs.ExecutionTimeMs = sw.ElapsedMilliseconds / context.LastResultSets.Count;
                            rs.TotalRowsMatched = rs.Rows.Count;
                        }

                        if (stmt.IntoTable != null)
                        {
                            await LoadIntoTable(stmt.IntoTable, results, context);
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

        private async Task LoadIntoTable(TableReference target, List<DataTable> results, IExecutionContext context)
        {
            string tableName = target.TableName;
            
            if (tableName.StartsWith("#") && !context.Connections.ContainsKey(tableName))
            {
                var firstBatch = results.FirstOrDefault();
                if (firstBatch != null)
                {
                    var mem = new InMemoryDataSource();
                    var columns = firstBatch.ColumnNames.Select(c => new ColumnDefinition(c, "ANY", false));
                    mem.SetSchema(columns);
                    context.Connections[tableName] = mem;
                }
            }

            if (context.Connections.TryGetValue(tableName, out var targetSource))
            {
                await targetSource.TruncateAsync();
                
                async IAsyncEnumerable<DataTable> GetBatches()
                {
                    foreach (var b in results) yield return b;
                    await Task.CompletedTask;
                }
                
                await targetSource.WriteBatches(GetBatches());
                int totalRows = results.Sum(r => r.Rows.Count);
                context.Log($"Loaded {totalRows} rows into {tableName}.");
            }
            else
            {
                throw new System.Exception($"Target table '{tableName}' not found for INTO clause.");
            }
        }
    }
}
