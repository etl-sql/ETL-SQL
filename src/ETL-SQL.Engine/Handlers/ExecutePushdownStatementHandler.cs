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
    public class ExecutePushdownStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(ExecutePushdownStatement);


        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ExecutePushdownStatement)statement;
            var evaluator = (Evaluator)context;

            if (string.IsNullOrWhiteSpace(stmt.SqlText))
            {
                _logger.WriteLine("Pushdown SQL text is empty, skipping execution.", ConsoleColor.Yellow);
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

            _logger.WriteLine($"Pushing down native SQL to {connectionName}...", ConsoleColor.Cyan);

            string sqlToExecute = stmt.SqlText;
            
            // If the user included the connection prefix, strip it (e.g. m.dbo.Employee -> dbo.Employee)
            // This is necessary because some users write fully-qualified ETL-SQL names even in pushdown blocks.
            string prefix = connectionName + ".";
            if (sqlToExecute.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                sqlToExecute = sqlToExecute.Substring(prefix.Length);
            }
            else
            {
                // Simple cleanup for common cases (e.g. FROM m.table)
                sqlToExecute = sqlToExecute.Replace(" " + prefix, " ", StringComparison.OrdinalIgnoreCase)
                                         .Replace("(" + prefix, "(", StringComparison.OrdinalIgnoreCase);
            }

            if (context.IsWhatIf)
            {
                _logger.WriteLine($"WHAT IF: Would execute native SQL on {connectionName}:\n{sqlToExecute}", ConsoleColor.Yellow);
                return;
            }

            var resultMap = new Dictionary<int, DataTable>();
            await foreach (var batch in databaseSource.ExecuteRawSql(sqlToExecute, parameters))
            {
                if (!resultMap.TryGetValue(batch.ResultSetIndex, out var resultTable))
                {
                    resultTable = new DataTable { ResultSetIndex = batch.ResultSetIndex };
                    resultTable.SetColumns(batch.ColumnNames);
                    resultMap[batch.ResultSetIndex] = resultTable;
                }
                
                foreach (var row in batch.Rows)
                {
                    await resultTable.AddRowAsync(row);
                }
            }

            var allResultSets = resultMap.Values.OrderBy(r => r.ResultSetIndex).ToList();
            if (allResultSets.Count > 0)
            {
                evaluator.LastResult = allResultSets.Last();
                evaluator.LastResultSets.Clear();
                evaluator.LastResultSets.AddRange(allResultSets);

                if (stmt.IntoTable != null)
                {
                    var lastResult = allResultSets.Last();

                    await LoadIntoTable(stmt.IntoTable, new List<DataTable> { lastResult }, evaluator);
                    RecordLineage(stmt, new List<DataTable> { lastResult }, evaluator);
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
                _logger.WriteLine($"Loaded {totalRows} rows into {tableName}.", ConsoleColor.Green);
                context.RowsProcessed += totalRows;
            }
            else
            {
                throw new ExecutionException($"Target table '{tableName}' not found for INTO clause.");
            }
        }

        private void RecordLineage(ExecutePushdownStatement stmt, List<DataTable> results, Evaluator context)
        {
            if (stmt.IntoTable == null || results.Count == 0) return;

            string target = (stmt.IntoTable.ConnectionName != null ? stmt.IntoTable.ConnectionName + "." + stmt.IntoTable.TableName : stmt.IntoTable.TableName);
            var sources = stmt.GetSourceTables().ToList();
            var lastBatch = results.Last();

            // Record table-level lineage
            context.LineageTracker.Record(target, sources, "EXECUTE PUSHDOWN (ACTUAL)", line: stmt.Line, column: stmt.Column);

            // Record column-level lineage from the actual result set
            foreach (var colName in lastBatch.ColumnNames)
            {
                context.LineageTracker.Record(target, sources, "EXECUTE PUSHDOWN COLUMN (ACTUAL)", targetColumn: colName, line: stmt.Line, column: stmt.Column);
            }
        }
    }
}
