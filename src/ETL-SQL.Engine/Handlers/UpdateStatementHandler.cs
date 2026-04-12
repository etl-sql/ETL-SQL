using ETL_SQL.Data;
using ETL_SQL.Core.Common.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the execution of UPDATE statements, supporting both remote SQL pushdown and in-memory updates with OUTPUT clause support.
    /// </summary>
    public class UpdateStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(UpdateStatement);
 
        /// <summary>Executes the UPDATE statement against the target data source.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (UpdateStatement)statement;

            string connName = stmt.TargetTable.ConnectionName ?? stmt.TargetTable.TableName;
            _logger.Debug("Updating {ConnName}", connName);
            if (!context.Connections.TryGetValue(connName, out var connection)) throw new ExecutionException($"Unknown connection: {connName}");
            _logger.Debug("Connection resolved as {ConnectionType}", connection.GetType().Name);
            if (connection is IDatabaseSource sqlConn)
            {
                _logger.Debug("Strategy: Remote SQL UPDATE");
                var assignments = stmt.Assignments.Select(a => $"{a.ColumnName} = {context.CompileExpression(a.Value, sqlConn.Dialect)}");
                var sql = $"UPDATE {context.GetSqlTableName(stmt.TargetTable)} SET {string.Join(", ", assignments)}";
                if (stmt.WhereClause != null) sql += $"\nWHERE {context.CompileExpression(stmt.WhereClause, sqlConn.Dialect)}";
                
                if (context.IsWhatIf)
                {
                    _logger.WriteLine($"WHAT IF: Would execute remote SQL update on {connName}:\n{sql}", ConsoleColor.Yellow);
                }
                else
                {
                    await foreach (var _ in sqlConn.ExecuteRawSql(sql)) { }
                }
                // Note: Reporting count for remote SQL is 0 for now as ExecuteRawSql doesn't return it
                context.RowsProcessed = 0; 
            }
            else if (connection is InMemoryDataSource memConn)
            {
                if (context.IsWhatIf)
                {
                    _logger.WriteLine($"WHAT IF: Would update rows in in-memory table {connName}.", ConsoleColor.Yellow);
                    // For in-memory what-if, we don't even call UpdateRows to avoid any side effects in the handler
                    // but we could call it if we wanted to show the count.
                    // For now, let's keep it simple.
                    context.RowsProcessed = 0;
                }
                else
                {
                    var updatedRows = await memConn.UpdateRows(
                        async row => stmt.WhereClause == null || await context.EvaluateCondition(stmt.WhereClause, row),
                        async row => { 
                            foreach (var a in stmt.Assignments) 
                                row[a.ColumnName] = await context.EvaluateValue(a.Value, row); 
                        });
                    context.RowsProcessed = updatedRows.Count;

                    if (stmt.Output != null)
                    {
                        var outputRows = new List<Row>();
                        foreach (var (before, after) in updatedRows)
                        {
                            var contextRow = new Row();
                            foreach (var col in before.Columns) contextRow[$"DELETED.{col.Key}"] = col.Value;
                            foreach (var col in after.Columns) { contextRow[$"INSERTED.{col.Key}"] = col.Value; contextRow[col.Key] = col.Value; }

                            var outputRow = new Row();
                            foreach (var outCol in stmt.Output.Columns)
                            {
                                var val = await context.EvaluateValue(outCol.Expression, contextRow);
                                outputRow[outCol.Alias ?? outCol.ToSql()] = val;
                            }
                            outputRows.Add(outputRow);
                        }

                        if (outputRows.Count > 0)
                        {
                            var outputTable = new DataTable();
                            outputTable.SetColumns(outputRows[0].Columns.Keys);
                            foreach (var r in outputRows) await outputTable.AddRowAsync(r);

                            if (stmt.Output.IntoTable != null)
                            {
                                var intoDest = await context.ResolveDataSourceAsync(stmt.Output.IntoTable);
                                await intoDest.WriteBatches(new[] { outputTable }.ToAsyncEnumerable());
                            }
                            else
                            {
                                context.LastResult = outputTable;
                            }
                        }
                    }
                }
            }
        }
    }
}
