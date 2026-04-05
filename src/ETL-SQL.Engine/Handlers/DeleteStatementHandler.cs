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
    /// Handles the execution of DELETE statements, supporting both remote SQL pushdown and in-memory deletions with OUTPUT clause support.
    /// </summary>
    public class DeleteStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(DeleteStatement);
        /// <summary>Executes the DELETE statement against the target data source.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (DeleteStatement)statement;

            string connName = stmt.TargetTable.ConnectionName ?? stmt.TargetTable.TableName;
            Logger.Verbose($"Deleting from {connName}");
            if (!context.Connections.TryGetValue(connName, out var connection)) throw new ExecutionException($"Unknown: {connName}");
            Logger.Verbose($"Connection resolved as {connection.GetType().Name}");
            if (connection is IDatabaseSource sqlConn)
            {
                Logger.Verbose("Strategy: Remote SQL DELETE");
                var sql = $"DELETE FROM {context.GetSqlTableName(stmt.TargetTable)}";
                if (stmt.WhereClause != null) sql += $"\nWHERE {context.CompileExpression(stmt.WhereClause, sqlConn.Dialect)}";
                await foreach(var _ in sqlConn.ExecuteRawSql(sql)){}
                context.RowsProcessed = 0; // Unknown for remote SQL
            }
            else if (connection is InMemoryDataSource memConn)
            {
                var deletedRows = await memConn.DeleteRows(async row => stmt.WhereClause == null || await context.EvaluateCondition(stmt.WhereClause, row));
                context.RowsProcessed = deletedRows.Count;

                if (stmt.Output != null)
                {
                    var outputTable = new DataTable();
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
                        outputTable.SetColumns(outputRows[0].Columns.Keys);
                        foreach (var r in outputRows) outputTable.AddRow(r);

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
