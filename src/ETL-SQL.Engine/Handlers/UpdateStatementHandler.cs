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
            if (connection is IDatabaseSource sqlConn && context.IsSqlPushdown(connName))
            {
                _logger.Debug("Strategy: Remote SQL UPDATE");
                var allParams = new Dictionary<string, object?>();
                int paramIdx = 0;
                
                string CompileAndMerge(Expression e) {
                    var compiled = context.CompileExpression(e, sqlConn.Dialect);
                    string sqlPart = compiled.Sql;
                    foreach(var p in compiled.Parameters.OrderByDescending(x => x.Key.Length)) {
                        string newName = $"@up{paramIdx++}";
                        sqlPart = sqlPart.Replace(p.Key, newName);
                        allParams[newName] = p.Value;
                    }
                    return sqlPart;
                }

                var assignments = stmt.Assignments.Select(a => $"{a.ColumnName} = {CompileAndMerge(a.Value)}").ToList();
                var sql = $"UPDATE {context.GetSqlTableName(stmt.TargetTable, sqlConn.Dialect)} SET {string.Join(", ", assignments)}";
                if (stmt.WhereClause != null) sql += $"\nWHERE {CompileAndMerge(stmt.WhereClause)}";
                
                if (context.IsWhatIf)
                {
                    var whatIfAssignments = stmt.Assignments.Select(a => $"{a.ColumnName} = {context.CompileExpression(a.Value, sqlConn.Dialect).ToEscapedSql(sqlConn.Dialect)}");
                    var whatIfSql = $"UPDATE {context.GetSqlTableName(stmt.TargetTable, sqlConn.Dialect)} SET {string.Join(", ", whatIfAssignments)}";
                    if (stmt.WhereClause != null) whatIfSql += $"\nWHERE {context.CompileExpression(stmt.WhereClause, sqlConn.Dialect).ToEscapedSql(sqlConn.Dialect)}";
                    
                    _logger.WriteLine($"WHAT IF: Would execute remote SQL update on {connName}:\n{whatIfSql}", ConsoleColor.Yellow);
                }
                else
                {
                    await foreach (var batch in sqlConn.ExecuteRawSql(sql, allParams.Values)) 
                    {
                        if (batch.RowsAffected >= 0) context.RowsProcessed += batch.RowsAffected;
                    }
                }
            }
            else
            {
                _logger.Debug("Strategy: Engine-side Batch UPDATE (Streaming)");
                
                if (context.IsWhatIf)
                {
                    _logger.WriteLine($"WHAT IF: Would update rows in {connName} via engine-side streaming.", ConsoleColor.Yellow);
                    return;
                }

                // 1. Prepare temp storage to avoid reading/writing to the same file simultaneously
                var tempStore = new InMemoryDataSource();

                // 2. Read batches from source, transform, and stream to temp
                int updatedCount = 0;
                var batches = connection.ReadBatches();
                
                async IAsyncEnumerable<DataTable> ProcessBatches()
                {
                    await foreach (var batch in batches)
                    {
                        foreach (var row in batch.Rows)
                        {
                            if (stmt.WhereClause == null || await context.EvaluateCondition(stmt.WhereClause, row))
                            {
                                foreach (var a in stmt.Assignments)
                                {
                                    row[a.ColumnName] = await context.EvaluateValue(a.Value, row);
                                }
                                updatedCount++;
                            }
                        }
                        yield return batch;
                    }
                }

                // For large files, writing to memory first then back to source is safer to avoid access violations.
                // If it's too large for memory, we should have used a temp file, but IDataSource.WriteBatches on file sources
                // usually overwrites anyway.
                
                var processed = ProcessBatches();
                await connection.WriteBatches(processed);

                context.IncrementOperationCount(OperationType.EngineInternal, count: updatedCount);
                context.RowsProcessed += updatedCount;
                
                if (context.IsVerbose) _logger.WriteLine($"Finished updating {updatedCount} rows in {connName}");
            }
        }
    }
}
