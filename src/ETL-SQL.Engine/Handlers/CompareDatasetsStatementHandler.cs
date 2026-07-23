using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;

/// <summary>
/// Handles the COMPARE DATASETS statement, performing Change Data Capture (CDC) / reconciliation between two datasets.
/// </summary>
public class CompareDatasetsStatementHandler(ILogger logger) : IStatementHandler
{
    private readonly ILogger _logger = logger;

    public Type SupportedStatementType => typeof(CompareDatasetsStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (CompareDatasetsStatement)statement;
        _logger.Debug("Executing COMPARE DATASETS {Source} WITH {Baseline} INTO {Target}", stmt.SourceTable.TableName, stmt.BaselineTable.TableName, stmt.TargetTable.TableName);

        var sourceDs = await context.ResolveDataSourceAsync(stmt.SourceTable);
        if (sourceDs == null) throw new ExecutionException($"Could not resolve source dataset: {stmt.SourceTable.TableName}");

        var baselineDs = await context.ResolveDataSourceAsync(stmt.BaselineTable);
        if (baselineDs == null) throw new ExecutionException($"Could not resolve baseline dataset: {stmt.BaselineTable.TableName}");

        var sourceColumns = (await sourceDs.GetColumnsAsync(context.CancellationToken)).ToList();
        var baselineColumns = (await baselineDs.GetColumnsAsync(context.CancellationToken)).ToList();
        ValidateColumns(stmt, sourceColumns, baselineColumns);

        var compareColumns = sourceColumns
            .Where(c => !stmt.KeyColumns.Contains(c, StringComparer.OrdinalIgnoreCase))
            .Where(c => stmt.ExcludeColumns == null || !stmt.ExcludeColumns.Contains(c, StringComparer.OrdinalIgnoreCase))
            .Where(c => baselineColumns.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var outputColumns = new List<string>(stmt.KeyColumns) { "_change_type", "_changed_columns" };
        foreach (var col in compareColumns)
        {
            outputColumns.Add($"{col}_old");
            outputColumns.Add($"{col}_new");
        }

        long retainedBytes = 0;
        long operatorBudget = context.OperatorMemoryGrantMB > 0
            ? (long)context.OperatorMemoryGrantMB * 1024L * 1024L
            : 0L;

        // Load baseline data into a bounded key map. A future external merge path can replace this,
        // but this guard prevents a large CDC request from growing until the process OOMs.
        var baselineMap = new Dictionary<string, Row>(StringComparer.Ordinal);
        using var baselineLease = context.MemoryArbiter.AcquireLease();
        await foreach (var batch in baselineDs.ReadBatches(context.EffectiveBatchSize, context.CancellationToken))
        {
            foreach (var row in batch.Rows)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                string key = GetCompositeKey(row, stmt.KeyColumns);
                var retained = row.Clone();
                retainedBytes = checked(retainedBytes + EstimateRetainedBaselineBytes(key, retained));
                if ((operatorBudget > 0 && retainedBytes > operatorBudget) || baselineLease.RegisterAndCheckSpill(retainedBytes))
                {
                    throw new ExecutionException(
                        "COMPARE DATASETS exceeded its bounded memory grant while indexing the baseline dataset. " +
                        "Increase Engine:OperatorMemoryGrantMB, reduce the comparison scope, or compare smaller partitions.");
                }

                baselineMap[key] = retained;
            }
        }

        // Prepare Target #diff dataset
        if (stmt.TargetTable.TableName.StartsWith("#") && !context.Connections.ContainsKey(stmt.TargetTable.TableName))
        {
            context.Connections[stmt.TargetTable.TableName] = new InMemoryDataSource
            {
                Validator = context as IDataValidator,
                ExecutionContext = context,
                MaxInMemoryBatches = context.MaxInMemoryBatches
            };
        }

        var targetDs = await context.ResolveDataSourceAsync(stmt.TargetTable);
        if (targetDs == null) throw new ExecutionException($"Could not resolve target diff dataset: {stmt.TargetTable.TableName}");

        await targetDs.TruncateAsync();

        var diffBatch = new DataTable();
        diffBatch.SetColumns(outputColumns);

        var matchedBaselineKeys = new HashSet<string>(StringComparer.Ordinal);
        int totalDiffs = 0;

        // Process Source stream against Baseline
        await foreach (var sourceBatch in sourceDs.ReadBatches(context.EffectiveBatchSize, context.CancellationToken))
        {
            foreach (var sRow in sourceBatch.Rows)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                string key = GetCompositeKey(sRow, stmt.KeyColumns);

                if (!baselineMap.TryGetValue(key, out var bRow))
                {
                    // INSERT
                    await AddDiffRowAsync(diffBatch, stmt.KeyColumns, compareColumns, null, sRow, "INSERT", "");
                    totalDiffs++;
                }
                else
                {
                    matchedBaselineKeys.Add(key);
                    var changedCols = GetChangedColumns(sRow, bRow, compareColumns);

                    if (changedCols.Count > 0)
                    {
                        // UPDATE
                        await AddDiffRowAsync(diffBatch, stmt.KeyColumns, compareColumns, bRow, sRow, "UPDATE", string.Join(", ", changedCols));
                        totalDiffs++;
                    }
                    else
                    {
                        // UNCHANGED (omitted by default unless required)
                    }
                }

                if (diffBatch.Rows.Count >= context.EffectiveBatchSize)
                {
                    await targetDs.WriteBatches(new[] { diffBatch }.ToAsyncEnumerable(), append: true, context.CancellationToken);
                    diffBatch = new DataTable();
                    diffBatch.SetColumns(outputColumns);
                }
            }
        }

        // Process DELETES (Keys in baseline but missing in source)
        foreach (var kvp in baselineMap)
        {
            if (!matchedBaselineKeys.Contains(kvp.Key))
            {
                await AddDiffRowAsync(diffBatch, stmt.KeyColumns, compareColumns, kvp.Value, null, "DELETE", "");
                totalDiffs++;

                if (diffBatch.Rows.Count >= context.EffectiveBatchSize)
                {
                    await targetDs.WriteBatches(new[] { diffBatch }.ToAsyncEnumerable(), append: true, context.CancellationToken);
                    diffBatch = new DataTable();
                    diffBatch.SetColumns(outputColumns);
                }
            }
        }

        if (diffBatch.Rows.Count > 0)
        {
            await targetDs.WriteBatches(new[] { diffBatch }.ToAsyncEnumerable(), append: true, context.CancellationToken);
        }

        context.Telemetry.RowsProcessed += totalDiffs;
        _logger.Info("COMPARE DATASETS complete: {DiffCount} diff rows staged into {Target}", totalDiffs, stmt.TargetTable.TableName);
    }

    private static string GetCompositeKey(Row row, List<string> keyCols)
    {
        return string.Join("||", keyCols.Select(c => row[c]?.ToString() ?? "\0"));
    }

    private static void ValidateColumns(CompareDatasetsStatement stmt, IReadOnlyCollection<string> sourceColumns, IReadOnlyCollection<string> baselineColumns)
    {
        foreach (var keyColumn in stmt.KeyColumns)
        {
            if (!sourceColumns.Contains(keyColumn, StringComparer.OrdinalIgnoreCase))
                throw new ExecutionException($"COMPARE DATASETS key column '{keyColumn}' was not found in {stmt.SourceTable.TableName}.");
            if (!baselineColumns.Contains(keyColumn, StringComparer.OrdinalIgnoreCase))
                throw new ExecutionException($"COMPARE DATASETS key column '{keyColumn}' was not found in {stmt.BaselineTable.TableName}.");
        }
    }

    private static List<string> GetChangedColumns(Row sRow, Row bRow, IReadOnlyList<string> compareColumns)
    {
        var changed = new List<string>();
        foreach (var col in compareColumns)
        {
            if (!EvaluationUtils.IsSoftEqual(sRow[col], bRow[col]))
                changed.Add(col);
        }

        return changed;
    }

    private static async Task AddDiffRowAsync(
        DataTable diffBatch,
        IReadOnlyList<string> keyCols,
        IReadOnlyList<string> compareColumns,
        Row? oldRow,
        Row? newRow,
        string changeType,
        string changedColsStr)
    {
        var row = diffBatch.NewRow();
        var keySource = newRow ?? oldRow ?? throw new InvalidOperationException("A diff row requires an old or new row.");
        foreach (var keyCol in keyCols)
        {
            row[keyCol] = keySource[keyCol];
        }
        row["_change_type"] = changeType;
        row["_changed_columns"] = changedColsStr;

        foreach (var col in compareColumns)
        {
            row[$"{col}_old"] = oldRow?[col];
            row[$"{col}_new"] = newRow?[col];
        }

        await diffBatch.AddRowAsync(row);
    }

    private static long EstimateRetainedBaselineBytes(string key, Row row) =>
        checked(128L + Row.EstimateValueBytes(key) + row.EstimateHeapBytes());
}
