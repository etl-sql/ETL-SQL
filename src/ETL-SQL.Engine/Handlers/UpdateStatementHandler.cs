using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Quality;
using ETL_SQL.Data;
using ETL_SQL.Engine.Services;

namespace ETL_SQL.Engine.Handlers;
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
        if (context.VarContext.TryGetView(connName, out _))
            throw new ExecutionException($"View {connName} is read-only and cannot be used as an UPDATE target.");
        if (!context.Connections.TryGetValue(connName, out var connection)) throw new ExecutionException($"Unknown connection: {connName}");
        _logger.Debug("Connection resolved as {ConnectionType}", connection.GetType().Name);

        var dataQualityStatusAssignment = GetDataQualityStatusAssignment(stmt);
        bool touchesDataQualityColumns = dataQualityStatusAssignment != null
            || stmt.Assignments.Any(a => DataQualityColumns.IsDataQualityColumn(a.ColumnName));
        ValidateDataQualityAssignments(stmt);

        if (connection is IDatabaseSource sqlConn && context.IsSqlPushdown(connName) && !touchesDataQualityColumns)
        {
            _logger.Debug("Strategy: Remote SQL UPDATE");
            var allParams = new Dictionary<string, object?>();
            int paramIdx = 0;

            string CompileAndMerge(Expression e)
            {
                var compiled = context.CompileExpression(e, sqlConn.Dialect);
                string sqlPart = compiled.Sql;
                foreach (var p in compiled.Parameters.OrderByDescending(x => x.Key.Length))
                {
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
                    if (batch.RowsAffected >= 0) context.Telemetry.RowsProcessed += batch.RowsAffected;
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

            if (connection is AppendOnlyColumnDataSource columnar && stmt.Output == null && !touchesDataQualityColumns)
            {
                var nativeUpdated = await columnar.UpdateWhereAsync(
                    stmt.WhereClause,
                    context.CaseSensitiveComparison,
                    async row =>
                    {
                        foreach (var assignment in stmt.Assignments)
                            row[assignment.ColumnName] = await context.EvaluateValue(assignment.Value, row);
                        return row;
                    },
                    context.CancellationToken);
                if (nativeUpdated.HasValue)
                {
                    context.IncrementOperationCount(
                        OperationType.EngineInternal,
                        count: nativeUpdated.Value > int.MaxValue ? int.MaxValue : (int)nativeUpdated.Value);
                    context.Telemetry.RowsProcessed += nativeUpdated.Value;
                    if (context.IsVerbose)
                        _logger.WriteLine($"Finished appending {nativeUpdated.Value} update deltas in {connName}");
                    return;
                }
            }

            connection = await TempTableStorageRouter.EnsureMutableAsync(context, connName, connection, "UPDATE");

            // 1. Prepare temp storage to avoid reading/writing to the same file simultaneously
            var tempStore = new InMemoryDataSource();

            // 2. Read batches from source, transform, and stream to temp
            int updatedCount = 0;
            var batches = connection.ReadBatches(context.EffectiveBatchSize);
            var rowInfos = new List<(Row? Before, Row? After, string? Action)>();

            async IAsyncEnumerable<DataTable> ProcessBatches()
            {
                await foreach (var batch in batches)
                {
                    foreach (var row in batch.Rows)
                    {
                        if (stmt.WhereClause == null || await context.EvaluateCondition(stmt.WhereClause, row))
                        {
                            var before = stmt.Output != null ? row.Clone() : null;
                            if (dataQualityStatusAssignment != null)
                            {
                                var nextStatus = await context.EvaluateValue(dataQualityStatusAssignment.Value, row);
                                ValidateDataQualityStatusTransition(row[DataQualityColumns.Status], nextStatus);
                            }

                            foreach (var a in stmt.Assignments)
                            {
                                row[a.ColumnName] = await context.EvaluateValue(a.Value, row);
                            }

                            if (stmt.Output != null)
                            {
                                rowInfos.Add((before, row.Clone(), "UPDATE"));
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
            var materialized = new List<DataTable>();
            await foreach (var b in processed) materialized.Add(b);

            await connection.WriteBatches(materialized.ToAsyncEnumerable());

            if (stmt.Output != null)
            {
                await OutputClauseHelper.ProcessAsync(stmt.Output, context, rowInfos);
            }

            context.IncrementOperationCount(OperationType.EngineInternal, count: updatedCount);
            context.Telemetry.RowsProcessed += updatedCount;

            if (context.IsVerbose) _logger.WriteLine($"Finished updating {updatedCount} rows in {connName}");
        }
    }

    private static Assignment? GetDataQualityStatusAssignment(UpdateStatement stmt) =>
        stmt.Assignments.FirstOrDefault(a =>
            a.ColumnName.Equals(DataQualityColumns.Status, StringComparison.OrdinalIgnoreCase));

    private static void ValidateDataQualityAssignments(UpdateStatement stmt)
    {
        foreach (var assignment in stmt.Assignments)
        {
            if (!DataQualityColumns.IsDataQualityColumn(assignment.ColumnName)) continue;
            if (assignment.ColumnName.Equals(DataQualityColumns.Status, StringComparison.OrdinalIgnoreCase)) continue;

            throw new ExecutionException(
                $"Data-quality evidence column '{assignment.ColumnName}' is immutable; only {DataQualityColumns.Status} may be updated for quarantine disposition.");
        }
    }

    private static void ValidateDataQualityStatusTransition(object? currentValue, object? nextValue)
    {
        var current = NormalizeStatus(currentValue);
        var next = NormalizeStatus(nextValue);

        if (!IsKnownDisposition(next))
            throw new ExecutionException(
                $"Invalid data-quality disposition '{next}'. Expected quarantined, released, replaying, replayed, or discarded.");

        if (current.Equals(DataQualityColumns.WarnedStatus, StringComparison.OrdinalIgnoreCase))
            throw new ExecutionException("Warn rows are immutable evidence; __dq_status cannot be changed.");

        if (current.Equals(DataQualityColumns.QuarantinedStatus, StringComparison.OrdinalIgnoreCase))
        {
            if (next.Equals(DataQualityColumns.QuarantinedStatus, StringComparison.OrdinalIgnoreCase)
                || next.Equals(DataQualityColumns.ReleasedStatus, StringComparison.OrdinalIgnoreCase)
                || next.Equals(DataQualityColumns.DiscardedStatus, StringComparison.OrdinalIgnoreCase))
                return;
        }
        else if (current.Equals(DataQualityColumns.ReleasedStatus, StringComparison.OrdinalIgnoreCase))
        {
            if (next.Equals(DataQualityColumns.ReleasedStatus, StringComparison.OrdinalIgnoreCase)
                || next.Equals(DataQualityColumns.ReplayingStatus, StringComparison.OrdinalIgnoreCase)
                || next.Equals(DataQualityColumns.ReplayedStatus, StringComparison.OrdinalIgnoreCase)
                || next.Equals(DataQualityColumns.DiscardedStatus, StringComparison.OrdinalIgnoreCase))
                return;
        }
        else if (current.Equals(DataQualityColumns.ReplayingStatus, StringComparison.OrdinalIgnoreCase))
        {
            if (next.Equals(DataQualityColumns.ReplayingStatus, StringComparison.OrdinalIgnoreCase)
                || next.Equals(DataQualityColumns.ReleasedStatus, StringComparison.OrdinalIgnoreCase)
                || next.Equals(DataQualityColumns.ReplayedStatus, StringComparison.OrdinalIgnoreCase))
                return;
        }
        else if ((current.Equals(DataQualityColumns.ReplayedStatus, StringComparison.OrdinalIgnoreCase)
                  || current.Equals(DataQualityColumns.DiscardedStatus, StringComparison.OrdinalIgnoreCase))
                 && next.Equals(current, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new ExecutionException($"Invalid data-quality disposition transition: {current} -> {next}.");
    }

    private static bool IsKnownDisposition(string status) =>
        status.Equals(DataQualityColumns.QuarantinedStatus, StringComparison.OrdinalIgnoreCase)
        || status.Equals(DataQualityColumns.ReleasedStatus, StringComparison.OrdinalIgnoreCase)
        || status.Equals(DataQualityColumns.ReplayingStatus, StringComparison.OrdinalIgnoreCase)
        || status.Equals(DataQualityColumns.ReplayedStatus, StringComparison.OrdinalIgnoreCase)
        || status.Equals(DataQualityColumns.DiscardedStatus, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeStatus(object? value) =>
        value switch
        {
            null or DBNull => string.Empty,
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty,
            _ => value.ToString()?.Trim() ?? string.Empty
        };
}

