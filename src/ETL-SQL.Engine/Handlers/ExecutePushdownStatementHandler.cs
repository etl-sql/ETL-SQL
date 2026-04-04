using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles native SQL pushdown execution.
    /// Captures raw SQL between BEGIN and END and executes it directly on the remote system.
    /// </summary>
    public class ExecutePushdownStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ExecutePushdownStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ExecutePushdownStatement)statement;
            var evaluator = (Evaluator)context;

            if (string.IsNullOrWhiteSpace(stmt.SqlText))
            {
                Logger.WriteLine("Pushdown SQL text is empty, skipping execution.", ConsoleColor.Yellow);
                evaluator.LastResult = null;
                evaluator.LastResultSets.Clear();
                return;
            }

            var connectionNameObj = await context.EvaluateValue(stmt.ConnectionName, new Row());
            string connectionName = connectionNameObj?.ToString() ?? throw new ExecutionException("Connection name evaluated to null.");

            if (!evaluator.Connections.TryGetValue(connectionName, out var dataSource))
                throw new ExecutionException($"Connection not found: {connectionName}");

            if (dataSource is not IDatabaseSource databaseSource)
                throw new ExecutionException($"Connection '{connectionName}' does not support native SQL pushdown.");

            var parameters = new List<object?>();
            if (stmt.Parameters != null && stmt.Parameters.Count > 0)
            {
                foreach (var paramExpr in stmt.Parameters)
                {
                    parameters.Add(await context.EvaluateValue(paramExpr, new Row()));
                }
            }

            Logger.WriteLine($"Pushing down native SQL to {connectionName}...", ConsoleColor.Cyan);

            var results = new List<DataTable>();
            await foreach (var batch in databaseSource.ExecuteRawSql(stmt.SqlText, parameters))
            {
                results.Add(batch);
            }

            if (results.Count > 0)
            {
                evaluator.LastResult = results.Last();
                evaluator.LastResultSets.Clear();
                evaluator.LastResultSets.AddRange(results);

                if (stmt.IntoTable != null)
                {
                    await LoadIntoTable(stmt.IntoTable, results, evaluator);
                }
            }
            else
            {
                evaluator.LastResult = null;
                evaluator.LastResultSets.Clear();
            }
        }

        private async Task LoadIntoTable(TableReference target, List<DataTable> results, Evaluator context)
        {
            string tableName = target.TableName;
            
            // If it's a temp table and doesn't exist, create it from the first batch's schema
            if (tableName.StartsWith("#") && !context.Connections.ContainsKey(tableName))
            {
                var firstBatch = results.FirstOrDefault();
                if (firstBatch != null)
                {
                    var mem = new InMemoryDataSource();
                    var columns = firstBatch.ColumnNames.Select(c => new ColumnDefinition(c, "ANY", false));
                    mem.SetSchema(columns);
                    context.Connections[tableName] = mem;
                    Logger.Verbose($"Created temporary table {tableName} with {firstBatch.ColumnNames.Count} columns.");
                }
            }

            if (context.Connections.TryGetValue(tableName, out var targetSource))
            {
                // Simple truncate and load for EXECUTE ... INTO
                await targetSource.TruncateAsync();
                
                async IAsyncEnumerable<DataTable> GetBatches()
                {
                    foreach (var b in results) yield return b;
                    await Task.CompletedTask;
                }
                
                await targetSource.WriteBatches(GetBatches());
                
                int totalRows = results.Sum(r => r.Rows.Count);
                Logger.WriteLine($"Loaded {totalRows} rows into {tableName}.", ConsoleColor.Green);
                context.RowsProcessed += totalRows;
            }
            else
            {
                throw new ExecutionException($"Target table '{tableName}' not found for INTO clause.");
            }
        }
    }
}
