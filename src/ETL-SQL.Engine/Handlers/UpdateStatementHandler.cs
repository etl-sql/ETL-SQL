using ETL_SQL.Data;
using ETL_SQL.Core.Common.Exceptions;
using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the execution of UPDATE statements, supporting both remote SQL pushdown and in-memory updates with OUTPUT clause support.
    /// </summary>
    public class UpdateStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(UpdateStatement);
        /// <summary>Executes the UPDATE statement against the target data source.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (UpdateStatement)statement;
            

            string connName = stmt.TargetTable.ConnectionName ?? stmt.TargetTable.TableName;
            Logger.Verbose($"Updating {connName}");
            if (!context.Connections.TryGetValue(connName, out var connection)) throw new ExecutionException($"Unknown connection: {connName}");
            Logger.Verbose($"Connection resolved as {connection.GetType().Name}");
            if (connection is IDatabaseSource sqlConn)
            {
                Logger.Verbose("Strategy: Remote SQL UPDATE");
                var assignments = stmt.Assignments.Select(a => $"{a.ColumnName} = {context.CompileExpression(a.Value, sqlConn.Dialect)}");
                var sql = $"UPDATE {context.GetSqlTableName(stmt.TargetTable)} SET {string.Join(", ", assignments)}";
                if (stmt.WhereClause != null) sql += $"\nWHERE {context.CompileExpression(stmt.WhereClause, sqlConn.Dialect)}";
                await foreach(var _ in sqlConn.ExecuteRawSql(sql)){}
                // Note: Reporting count for remote SQL is 0 for now as ExecuteRawSql doesn't return it
                context.RowsProcessed = 0; 
            }
            else if (connection is InMemoryDataSource memConn)
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
                    var outputTable = new DataTable();
                    var outputRows = new List<Row>();

                    foreach (var (before, after) in updatedRows)
                    {
                        var contextRow = new Row();
                        foreach (var col in before.Columns)
                        {
                            contextRow.Columns[$"DELETED.{col.Key}"] = col.Value;
                        }
                        foreach (var col in after.Columns)
                        {
                            contextRow.Columns[$"INSERTED.{col.Key}"] = col.Value;
                            // For ambiguity, INSERTED takes precedence for unqualified names in UPDATE OUTPUT
                            contextRow.Columns[col.Key] = col.Value; 
                        }

                        var outputRow = new Row();
                        foreach (var outCol in stmt.Output.Columns)
                        {
                            var val = await context.EvaluateValue(outCol.Expression, contextRow);
                            outputRow.Columns[outCol.Alias ?? outCol.ToSql()] = val;
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



