using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Engine.Services;

namespace ETL_SQL.Engine.Handlers;
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
        if (context.VarContext.TryGetView(connName, out _))
            throw new ExecutionException($"View {connName} is read-only and cannot be used as a DELETE target.");
        if (!context.Connections.TryGetValue(connName, out var connection)) throw new ExecutionException($"Unknown: {connName}");
        _logger.Debug("Connection resolved as {ConnectionType}", connection.GetType().Name);
        if (connection is IDatabaseSource sqlConn && context.IsSqlPushdown(connName))
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
                await foreach (var batch in sqlConn.ExecuteRawSql(sql, compiledWhere?.Parameters.Values))
                {
                    if (batch.RowsAffected >= 0) context.Telemetry.RowsProcessed += batch.RowsAffected;
                }
            }
        }
        else
        {
            _logger.Debug("Strategy: Engine-side Batch DELETE (Streaming)");

            if (context.IsWhatIf)
            {
                _logger.WriteLine($"WHAT IF: Would delete rows in {connName} via engine-side streaming.", ConsoleColor.Yellow);
                return;
            }

            if (connection is AppendOnlyColumnDataSource columnar && stmt.Output == null)
            {
                var nativeDeleted = await columnar.DeleteWhereAsync(
                    stmt.WhereClause, context.CaseSensitiveComparison, context.CancellationToken);
                if (nativeDeleted.HasValue)
                {
                    context.IncrementOperationCount(
                        OperationType.EngineInternal,
                        count: nativeDeleted.Value > int.MaxValue ? int.MaxValue : (int)nativeDeleted.Value);
                    context.Telemetry.RowsProcessed += nativeDeleted.Value;
                    if (context.IsVerbose)
                        _logger.WriteLine($"Finished tombstoning {nativeDeleted.Value} rows in {connName}");
                    return;
                }
            }

            connection = await TempTableStorageRouter.EnsureMutableAsync(context, connName, connection, "DELETE");

            // 1. Read existing and filter
            int deletedCount = 0;
            var batches = connection.ReadBatches(context.EffectiveBatchSize);
            var rowInfos = new List<(Row? Before, Row? After, string? Action)>();

            async IAsyncEnumerable<DataTable> FilterBatches()
            {
                await foreach (var batch in batches)
                {
                    var survivingRows = new List<Row>();
                    foreach (var row in batch.Rows)
                    {
                        if (stmt.WhereClause == null || await context.EvaluateCondition(stmt.WhereClause, row))
                        {
                            if (stmt.Output != null)
                            {
                                rowInfos.Add((row.Clone(), null, "DELETE"));
                            }
                            deletedCount++;
                        }
                        else
                        {
                            survivingRows.Add(row);
                        }
                    }

                    var filteredBatch = new DataTable();
                    filteredBatch.SetColumns(batch.ColumnNames);
                    foreach (var row in survivingRows) await filteredBatch.AddRowAsync(row);
                    yield return filteredBatch;
                }
            }

            var filtered = FilterBatches();
            var materialized = new List<DataTable>();
            await foreach (var b in filtered) materialized.Add(b);

            await connection.WriteBatches(materialized.ToAsyncEnumerable());

            if (stmt.Output != null)
            {
                await OutputClauseHelper.ProcessAsync(stmt.Output, context, rowInfos);
            }

            context.IncrementOperationCount(OperationType.EngineInternal, count: deletedCount);
            context.Telemetry.RowsProcessed += deletedCount;

            if (context.IsVerbose) _logger.WriteLine($"Finished deleting {deletedCount} rows in {connName}");
        }
    }
}

