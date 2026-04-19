using ETL_SQL.Data;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Common.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the execution of DELETE statements, supporting both remote SQL pushdown and in-memory deletions with OUTPUT clause support.
    /// </summary>
    public class DeleteStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(DeleteStatement);
 
        /// <summary>Executes the DELETE statement against the target data source.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (DeleteStatement)statement;

            string connName = stmt.TargetTable.ConnectionName ?? stmt.TargetTable.TableName;
            _logger.Debug("Deleting from {ConnName}", connName);
            if (!context.Connections.TryGetValue(connName, out var connection)) throw new ExecutionException($"Unknown: {connName}");
            _logger.Debug("Connection resolved as {ConnectionType}", connection.GetType().Name);
            if (connection is IDatabaseSource sqlConn)
            {
                _logger.Debug("Strategy: Remote SQL DELETE");
                var sql = $"DELETE FROM {context.GetSqlTableName(stmt.TargetTable, sqlConn.Dialect)}";
                CompiledSql? compiledWhere = null;
                if (stmt.WhereClause != null) 
                {
                    compiledWhere = context.CompileExpression(stmt.WhereClause, sqlConn.Dialect);
                    if (compiledWhere != null)
                        sql += $"\nWHERE {compiledWhere.Sql}";
                }
                
                if (context.IsWhatIf)
                {
                    var whatIfSql = $"DELETE FROM {context.GetSqlTableName(stmt.TargetTable, sqlConn.Dialect)}";
                    if (stmt.WhereClause != null && compiledWhere != null) 
                        whatIfSql += $"\nWHERE {compiledWhere.ToEscapedSql(sqlConn.Dialect)}";
                    _logger.WriteLine($"WHAT IF: Would execute remote SQL delete on {connName}:\n{whatIfSql}", ConsoleColor.Yellow);
                }
                else
                {
                    await foreach (var _ in sqlConn.ExecuteRawSql(sql, compiledWhere?.Parameters.Values)) { }
                }
                context.RowsProcessed = 0; // Unknown for remote SQL
            }
            else if (connection is InMemoryDataSource memConn)
            {
                if (context.IsWhatIf)
                {
                    _logger.WriteLine($"WHAT IF: Would delete rows from in-memory table {connName}.", ConsoleColor.Yellow);
                    context.RowsProcessed = 0;
                }
                else
                {
                    var deletedRows = await memConn.DeleteRows(async row => stmt.WhereClause == null || await context.EvaluateCondition(stmt.WhereClause, row));
                    context.RowsProcessed = deletedRows.Count;

                    if (stmt.Output != null)
                    {
                        var outputRows = new List<Row>();
                        foreach (var deletedRow in deletedRows)
                        {
                            var contextRow = new Row();
                            foreach (var col in deletedRow.Columns)
                            {
                                contextRow[$"DELETED.{col.Key}"] = col.Value;
                                if (!contextRow.HasColumn(col.Key)) contextRow[col.Key] = col.Value;
                            }

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
