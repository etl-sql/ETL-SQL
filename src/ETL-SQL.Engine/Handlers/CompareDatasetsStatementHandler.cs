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

        // Load baseline data into key map
        var baselineMap = new Dictionary<string, Row>(StringComparer.Ordinal);
        await foreach (var batch in baselineDs.ReadBatches())
        {
            foreach (var row in batch.Rows)
            {
                string key = GetCompositeKey(row, stmt.KeyColumns);
                baselineMap[key] = row;
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
        var diffColumns = new List<string>(stmt.KeyColumns) { "_change_type", "_changed_columns" };

        var matchedBaselineKeys = new HashSet<string>(StringComparer.Ordinal);
        int totalDiffs = 0;

        // Process Source stream against Baseline
        await foreach (var sourceBatch in sourceDs.ReadBatches())
        {
            foreach (var sRow in sourceBatch.Rows)
            {
                string key = GetCompositeKey(sRow, stmt.KeyColumns);

                if (!baselineMap.TryGetValue(key, out var bRow))
                {
                    // INSERT
                    await AddDiffRowAsync(diffBatch, diffColumns, sRow, stmt.KeyColumns, "INSERT", "");
                    totalDiffs++;
                }
                else
                {
                    matchedBaselineKeys.Add(key);
                    var changedCols = GetChangedColumns(sRow, bRow, stmt.KeyColumns, stmt.ExcludeColumns);

                    if (changedCols.Count > 0)
                    {
                        // UPDATE
                        await AddDiffRowAsync(diffBatch, diffColumns, sRow, stmt.KeyColumns, "UPDATE", string.Join(", ", changedCols));
                        totalDiffs++;
                    }
                    else
                    {
                        // UNCHANGED (omitted by default unless required)
                    }
                }

                if (diffBatch.Rows.Count >= context.EffectiveBatchSize)
                {
                    await targetDs.WriteBatches(new[] { diffBatch }.ToAsyncEnumerable(), append: true);
                    diffBatch = new DataTable();
                }
            }
        }

        // Process DELETES (Keys in baseline but missing in source)
        foreach (var kvp in baselineMap)
        {
            if (!matchedBaselineKeys.Contains(kvp.Key))
            {
                await AddDiffRowAsync(diffBatch, diffColumns, kvp.Value, stmt.KeyColumns, "DELETE", "");
                totalDiffs++;

                if (diffBatch.Rows.Count >= context.EffectiveBatchSize)
                {
                    await targetDs.WriteBatches(new[] { diffBatch }.ToAsyncEnumerable(), append: true);
                    diffBatch = new DataTable();
                }
            }
        }

        if (diffBatch.Rows.Count > 0)
        {
            await targetDs.WriteBatches(new[] { diffBatch }.ToAsyncEnumerable(), append: true);
        }

        context.Telemetry.RowsProcessed += totalDiffs;
        _logger.Info("COMPARE DATASETS complete: {DiffCount} diff rows staged into {Target}", totalDiffs, stmt.TargetTable.TableName);
    }

    private static string GetCompositeKey(Row row, List<string> keyCols)
    {
        return string.Join("||", keyCols.Select(c => row[c]?.ToString() ?? "\0"));
    }

    private static List<string> GetChangedColumns(Row sRow, Row bRow, List<string> keyCols, List<string>? excludeCols)
    {
        var changed = new List<string>();
        var keysSet = new HashSet<string>(keyCols, StringComparer.OrdinalIgnoreCase);
        var excludeSet = excludeCols != null ? new HashSet<string>(excludeCols, StringComparer.OrdinalIgnoreCase) : null;
        var sCols = sRow.Columns;
        var bCols = bRow.Columns;

        foreach (var col in sCols.Keys)
        {
            if (keysSet.Contains(col)) continue;
            if (excludeSet != null && excludeSet.Contains(col)) continue;

            object? sVal = sCols[col];
            object? bVal = bCols.TryGetValue(col, out var val) ? val : null;

            if (!EvaluationUtils.IsSoftEqual(sVal, bVal))
            {
                changed.Add(col);
            }
        }

        return changed;
    }

    private static async Task AddDiffRowAsync(DataTable diffBatch, List<string> diffCols, Row sampleRow, List<string> keyCols, string changeType, string changedColsStr)
    {
        var sampleCols = sampleRow.Columns;
        if (diffBatch.ColumnNames.Count == 0)
        {
            // Add key columns + payload columns + metadata columns
            var allCols = new List<string>(diffCols);
            foreach (var col in sampleCols.Keys)
            {
                if (!allCols.Contains(col)) allCols.Add(col);
            }
            diffBatch.SetColumns(allCols);
        }

        var row = diffBatch.NewRow();
        foreach (var keyCol in keyCols)
        {
            row[keyCol] = sampleRow[keyCol];
        }
        row["_change_type"] = changeType;
        row["_changed_columns"] = changedColsStr;

        foreach (var kvp in sampleCols)
        {
            if (!keyCols.Contains(kvp.Key, StringComparer.OrdinalIgnoreCase))
            {
                row[kvp.Key] = kvp.Value;
            }
        }

        await diffBatch.AddRowAsync(row);
    }
}
