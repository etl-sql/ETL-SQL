using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Services;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;

/// <summary>
/// Handles TRANSFORM statement and its supported algorithms (e.g. FILL_DATES, INTERPOLATE, DEDUPLICATE, PIVOT, TOP_N_OTHERS, PERIOD_COMPARISON, SHARE_OF_TOTAL, NORMALIZE, ROLLING_AGGREGATE).
/// </summary>
public class TransformStatementHandler(ILogger logger) : IStatementHandler
{
    private static readonly HashSet<string> SupportedAggregates = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUM", "AVG", "COUNT", "MIN", "MAX"
    };

    private readonly ILogger _logger = logger;

    public Type SupportedStatementType => typeof(TransformStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (TransformStatement)statement;

        switch (stmt.Algorithm.ToUpperInvariant())
        {
            case "FILL_DATES":
                await ExecuteFillDates(stmt, context);
                break;
            case "INTERPOLATE":
                await ExecuteInterpolate(stmt, context);
                break;
            case "DEDUPLICATE":
                await ExecuteDeduplicate(stmt, context);
                break;
            case "PIVOT":
                await ExecutePivot(stmt, context);
                break;
            case "UNPIVOT":
            case "MELT":
                await ExecuteUnpivot(stmt, context);
                break;
            case "CLEAN":
                await ExecuteClean(stmt, context);
                break;
            case "BIN":
            case "BUCKETIZE":
                await ExecuteBin(stmt, context);
                break;
            case "OUTLIERS":
                await ExecuteOutliers(stmt, context);
                break;
            case "TOP_N_OTHERS":
                await ExecuteTopNOthers(stmt, context);
                break;
            case "PERIOD_COMPARISON":
                await ExecutePeriodComparison(stmt, context);
                break;
            case "SHARE_OF_TOTAL":
                await ExecuteShareOfTotal(stmt, context);
                break;
            case "NORMALIZE":
                await ExecuteNormalize(stmt, context);
                break;
            case "ROLLING_AGGREGATE":
                await ExecuteRollingAggregate(stmt, context);
                break;
            default:
                throw new ExecutionException($"Unsupported table transformation algorithm: {stmt.Algorithm}");
        }
    }

    private sealed record StagedTransformSource(
        List<Row> Rows,
        List<string> Columns,
        Dictionary<string, ColumnDefinition> SourceSchema,
        string SourceName,
        IMemoryGrantLease Lease) : IDisposable
    {
        public void Dispose() => Lease.Dispose();
    }

    private static async Task<StagedTransformSource> StageSourceRows(
        TransformStatement stmt,
        IExecutionContext context,
        string algorithmName)
    {
        var sourceTable = stmt.SourceTable ?? stmt.TargetTable;
        var source = await context.ResolveReadSourceAsync(sourceTable)
            ?? throw new ExecutionException($"Could not resolve source dataset: {sourceTable.TableName}");

        var sourceSchema = await ResolveSourceSchemaAsync(source, sourceTable, context);
        var rows = new List<Row>();
        var columns = (await source.GetColumnsAsync(context.CancellationToken)).ToList();
        long retainedBytes = 0;
        long operatorBudget = context.OperatorMemoryGrantMB > 0
            ? (long)context.OperatorMemoryGrantMB * 1024L * 1024L
            : 0L;

        var lease = context.MemoryArbiter.AcquireLease();
        try
        {
            await foreach (var batch in source.ReadBatches(context.EffectiveBatchSize, context.CancellationToken))
            {
                foreach (var row in batch.Rows)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    var clone = row.Clone();
                    retainedBytes = checked(retainedBytes + clone.EstimateHeapBytes());
                    if ((operatorBudget > 0 && retainedBytes > operatorBudget) || lease.RegisterAndCheckSpill(retainedBytes))
                    {
                        throw new ExecutionException(
                            $"{algorithmName} exceeded its bounded memory grant while staging the source series. " +
                            "Increase Engine:OperatorMemoryGrantMB or reduce the data scope.");
                    }
                    rows.Add(clone);
                }
            }

            if (columns.Count == 0)
            {
                throw new ExecutionException($"Source dataset {sourceTable.TableName} has no columns.");
            }

            foreach (var col in columns)
            {
                sourceSchema.TryAdd(col, new ColumnDefinition(col, "UNKNOWN", false));
            }

            return new StagedTransformSource(rows, columns, sourceSchema, sourceTable.TableName, lease);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private static async Task<Dictionary<string, ColumnDefinition>> ResolveSourceSchemaAsync(
        IDataSource source,
        TableReference sourceTable,
        IExecutionContext context)
    {
        var schema = new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase);

        if (source is InMemoryDataSource inMemory)
        {
            foreach (var kv in inMemory.Schema)
            {
                schema[kv.Key] = kv.Value;
            }
        }
        else if (source is AppendOnlyColumnDataSource colStore)
        {
            foreach (var kv in colStore.LogicalSchema)
            {
                schema[kv.Key] = kv.Value;
            }
        }
        else if (source.GetCatalogProvider() is { } catalog)
        {
            var catCols = await catalog.GetColumnMetadataAsync(sourceTable.SchemaName ?? string.Empty, sourceTable.TableName, context.CancellationToken);
            foreach (var col in catCols)
            {
                schema[col.ColumnName] = new ColumnDefinition(col.ColumnName, col.DataType, false) { IsNullable = col.IsNullable };
            }
        }

        return schema;
    }

    private static List<ColumnDefinition> BuildTargetSchemaDefinitions(
        TransformStatement stmt,
        IReadOnlyList<string> columns,
        IReadOnlyDictionary<string, ColumnDefinition> sourceSchema,
        bool forceNullablePassThrough = false)
    {
        var sourceMeta = columns
            .Select(col => new ColumnMetadata(
                col,
                sourceSchema.TryGetValue(col, out var def) ? def.DataType : "UNKNOWN"))
            .ToList();

        var projected = TransformSchemaProjection.ProjectColumns(stmt, sourceMeta);
        var result = new List<ColumnDefinition>(projected.Count);
        foreach (var col in projected)
        {
            if (sourceSchema.TryGetValue(col.Name, out var srcDef) &&
                string.Equals(srcDef.DataType, col.DataType, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(CloneColumnDefinition(srcDef, forceNullablePassThrough ? true : srcDef.IsNullable));
            }
            else
            {
                result.Add(new ColumnDefinition(col.Name, col.DataType, false) { IsNullable = true });
            }
        }

        return result;
    }

    private static List<ColumnDefinition> CopySourceSchemaDefinitions(
        IReadOnlyList<string> columns,
        IReadOnlyDictionary<string, ColumnDefinition> sourceSchema,
        bool forceNullable = false)
    {
        var result = new List<ColumnDefinition>(columns.Count);
        foreach (var col in columns)
        {
            if (sourceSchema.TryGetValue(col, out var srcDef))
            {
                result.Add(CloneColumnDefinition(srcDef, forceNullable ? true : srcDef.IsNullable));
            }
            else
            {
                result.Add(new ColumnDefinition(col, "UNKNOWN", false) { IsNullable = true });
            }
        }
        return result;
    }

    private static ColumnDefinition CloneColumnDefinition(ColumnDefinition src, bool isNullable)
    {
        return new ColumnDefinition(src.ColumnName, src.DataType, false, src.DefaultExpression, src.Metadata)
        {
            IsNullable = isNullable
        };
    }

    private async Task ExecuteFillDates(TransformStatement stmt, IExecutionContext context)
    {
        ValidateAllowedOptions(stmt, "FILL_DATES", "DATE_COL", "GAPS_FILL", "BY_GROUP", "INTERVAL", "START_DATE", "END_DATE");
        var dateColumn = GetStringOption(stmt.Options, "DATE_COL", "FILL_DATES");
        var interval = stmt.Options.ContainsKey("INTERVAL")
            ? GetStringOption(stmt.Options, "INTERVAL", "FILL_DATES").ToUpperInvariant()
            : "DAY";
        ValidateFillInterval(interval);

        var gapFillValueExpr = stmt.Options.TryGetValue("GAPS_FILL", out var grp)
            ? grp
            : new LiteralExpression(0m, TokenType.NUMERIC);
        bool isForwardFill = gapFillValueExpr is IdentifierExpression gapId &&
                             string.Equals(gapId.Name, "FORWARD", StringComparison.OrdinalIgnoreCase);
        object? gapFillValue = null;
        if (!isForwardFill)
        {
            gapFillValue = await context.EvaluationContext.EvaluateValue(gapFillValueExpr, Row.Empty);
            if (gapFillValue is string gapStr && string.Equals(gapStr, "FORWARD", StringComparison.OrdinalIgnoreCase))
            {
                isForwardFill = true;
            }
        }

        DateTime? explicitStart = await EvaluateOptionalBucketDateAsync(stmt.Options, "START_DATE", interval, context);
        DateTime? explicitEnd = await EvaluateOptionalBucketDateAsync(stmt.Options, "END_DATE", interval, context);
        if (explicitStart.HasValue && explicitEnd.HasValue && explicitStart.Value > explicitEnd.Value)
        {
            throw new ExecutionException("FILL_DATES START_DATE cannot be after END_DATE.");
        }

        var groupColumns = GetOptionalColumnList(stmt.Options, "BY_GROUP");

        using var staged = await StageSourceRows(stmt, context, "FILL_DATES");
        var (rows, columns, sourceSchema, sourceName) = (staged.Rows, staged.Columns, staged.SourceSchema, staged.SourceName);
        ValidateColumnsExist(sourceName, columns, "FILL_DATES",
            ("DATE_COL", [dateColumn]),
            ("BY_GROUP", groupColumns));

        bool nullableOutputs = isForwardFill || GetValue(gapFillValue) == null;
        var targetSchemaDefs = BuildTargetSchemaDefinitions(stmt, columns, sourceSchema, forceNullablePassThrough: nullableOutputs);
        await using var sink = await TransformOutputSink.CreateAsync(stmt, context, "FILL_DATES", targetSchemaDefs, _logger);

        bool isHourly = string.Equals(interval, "HOUR", StringComparison.OrdinalIgnoreCase);

        foreach (var group in rows.GroupBy(row => BuildGroupKey(row, groupColumns)).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var timedRows = new List<(DateTime RawDate, DateTime Bucket, Row Row)>();
            var byBucket = new Dictionary<DateTime, List<Row>>();
            Row? template = null;

            foreach (var row in group)
            {
                if (!TryGetDateTime(row[dateColumn], out var dt))
                {
                    throw new ExecutionException($"FILL_DATES could not convert {dateColumn} value '{row[dateColumn]}' to a date.");
                }
                template ??= row;
                var bucket = NormalizeIntervalBucket(dt, interval);
                timedRows.Add((dt, bucket, row));
                if (!byBucket.TryGetValue(bucket, out var bucketRows))
                {
                    bucketRows = new List<Row>();
                    byBucket[bucket] = bucketRows;
                }
                bucketRows.Add(row);
            }

            if (byBucket.Count == 0 || template == null) continue;
            var minDate = explicitStart ?? byBucket.Keys.Min();
            var maxDate = explicitEnd ?? byBucket.Keys.Max();
            if (minDate > maxDate) continue;

            var orderedRows = timedRows
                .Where(item => item.Bucket >= minDate && item.Bucket <= maxDate)
                .OrderBy(item => item.Bucket)
                .ToList();
            int rowIndex = 0;
            Row? lastEmittedRow = null;

            // Bound calendar arithmetic before stepping, and stream the grid instead of retaining
            // every generated date. This also keeps wide hourly ranges cancellable.
            var lastStep = LastFillStep(minDate, maxDate, interval);
            for (int step = 0; step <= lastStep; step++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var stepDate = StepFromStart(minDate, interval, step);
                if (stepDate > maxDate) break;

                while (rowIndex < orderedRows.Count && orderedRows[rowIndex].Bucket < stepDate)
                {
                    var offRow = orderedRows[rowIndex++].Row.Clone();
                    await sink.WriteRowAsync(offRow);
                    lastEmittedRow = offRow;
                }

                if (byBucket.TryGetValue(stepDate, out var existingRows))
                {
                    foreach (var existing in existingRows)
                    {
                        var clone = existing.Clone();
                        await sink.WriteRowAsync(clone);
                        lastEmittedRow = clone;
                        rowIndex++;
                    }
                }
                else
                {
                    await sink.WriteRowAsync(
                        CreateGapRow(
                            sink.Schema,
                            columns,
                            dateColumn,
                            groupColumns,
                            template,
                            stepDate,
                            isForwardFill,
                            lastEmittedRow,
                            gapFillValue,
                            isHourly));
                }
            }

            while (rowIndex < orderedRows.Count)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                await sink.WriteRowAsync(orderedRows[rowIndex++].Row.Clone());
            }
        }

        await sink.CompleteAsync();
    }

    private async Task ExecuteInterpolate(TransformStatement stmt, IExecutionContext context)
    {
        ValidateAllowedOptions(stmt, "INTERPOLATE", "VALUE_COL", "VALUE_COLS", "ORDER_COL", "METHOD", "BY_GROUP");
        var valueCols = GetValueColumnList(stmt.Options, "INTERPOLATE");
        var orderCol = GetStringOption(stmt.Options, "ORDER_COL", "INTERPOLATE");
        var method = stmt.Options.ContainsKey("METHOD")
            ? GetStringOption(stmt.Options, "METHOD", "INTERPOLATE")
            : "LINEAR";
        var groupColumns = GetOptionalColumnList(stmt.Options, "BY_GROUP");

        bool isForward = string.Equals(method, "FORWARD", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(method, "FORWARD_FILL", StringComparison.OrdinalIgnoreCase);
        bool isBackward = string.Equals(method, "BACKWARD", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(method, "BACKWARD_FILL", StringComparison.OrdinalIgnoreCase);
        bool isLinear = string.Equals(method, "LINEAR", StringComparison.OrdinalIgnoreCase);
        if (!isForward && !isBackward && !isLinear)
        {
            throw new ExecutionException($"Unsupported INTERPOLATE method: {method}");
        }

        using var staged = await StageSourceRows(stmt, context, "INTERPOLATE");
        var (rows, columns, sourceSchema, sourceName) = (staged.Rows, staged.Columns, staged.SourceSchema, staged.SourceName);
        ValidateColumnsExist(sourceName, columns, "INTERPOLATE",
            ("VALUE_COLS", valueCols),
            ("ORDER_COL", [orderCol]),
            ("BY_GROUP", groupColumns));

        foreach (var numericCol in valueCols) ValidateNumericColumn(rows, numericCol, "INTERPOLATE");
        var targetSchemaDefs = BuildTargetSchemaDefinitions(stmt, columns, sourceSchema);
        await using var sink = await TransformOutputSink.CreateAsync(stmt, context, "INTERPOLATE", targetSchemaDefs, _logger);

        foreach (var group in rows.GroupBy(row => BuildGroupKey(row, groupColumns)))
        {
            var sortedGroup = group
                .Select(row => (Row: row, Order: GetOrderValue(row[orderCol], "INTERPOLATE", orderCol)))
                .OrderBy(item => item.Order)
                .ToList();
            int n = sortedGroup.Count;

            foreach (var valueCol in valueCols)
            {
                if (isForward)
                {
                    object? lastVal = null;
                    for (int i = 0; i < n; i++)
                    {
                        var val = GetValue(sortedGroup[i].Row[valueCol]);
                        if (val != null)
                        {
                            lastVal = val;
                        }
                        else if (lastVal != null)
                        {
                            sortedGroup[i].Row[valueCol] = lastVal;
                        }
                    }
                }
                else if (isBackward)
                {
                    object? nextVal = null;
                    for (int i = n - 1; i >= 0; i--)
                    {
                        var val = GetValue(sortedGroup[i].Row[valueCol]);
                        if (val != null)
                        {
                            nextVal = val;
                        }
                        else if (nextVal != null)
                        {
                            sortedGroup[i].Row[valueCol] = nextVal;
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < n; i++)
                    {
                        if (GetValue(sortedGroup[i].Row[valueCol]) != null) continue;

                        int prevIdx = -1;
                        for (int p = i - 1; p >= 0; p--)
                        {
                            if (GetValue(sortedGroup[p].Row[valueCol]) != null)
                            {
                                prevIdx = p;
                                break;
                            }
                        }

                        int nextIdx = -1;
                        for (int nx = i + 1; nx < n; nx++)
                        {
                            if (GetValue(sortedGroup[nx].Row[valueCol]) != null)
                            {
                                nextIdx = nx;
                                break;
                            }
                        }

                        if (prevIdx != -1 && nextIdx != -1)
                        {
                            var y0 = ToDecimal(GetValue(sortedGroup[prevIdx].Row[valueCol])!, "INTERPOLATE", valueCol);
                            var y1 = ToDecimal(GetValue(sortedGroup[nextIdx].Row[valueCol])!, "INTERPOLATE", valueCol);
                            var x0 = sortedGroup[prevIdx].Order;
                            var x1 = sortedGroup[nextIdx].Order;
                            var x = sortedGroup[i].Order;

                            sortedGroup[i].Row[valueCol] = x1 == x0
                                ? y0
                                : LinearInterpolate(y0, y1, x0, x1, x);
                        }
                    }
                }
            }

            foreach (var item in sortedGroup)
            {
                await sink.WriteRowAsync(item.Row);
            }
        }

        await sink.CompleteAsync();
    }

    private async Task ExecuteDeduplicate(TransformStatement stmt, IExecutionContext context)
    {
        ValidateAllowedOptions(stmt, "DEDUPLICATE", "KEY_COLS", "ORDER_BY", "KEEP", "REJECT_INTO");
        if (!stmt.Options.TryGetValue("KEY_COLS", out var kExpr))
        {
            throw new ExecutionException("DEDUPLICATE algorithm requires KEY_COLS parameter");
        }
        var keyCols = GetGroupColumns(kExpr);
        if (keyCols.Count == 0)
        {
            throw new ExecutionException("DEDUPLICATE KEY_COLS must specify at least one column.");
        }

        var orderByStr = stmt.Options.ContainsKey("ORDER_BY")
            ? GetStringOption(stmt.Options, "ORDER_BY", "DEDUPLICATE")
            : null;
        var keep = stmt.Options.ContainsKey("KEEP")
            ? GetStringOption(stmt.Options, "KEEP", "DEDUPLICATE").ToUpperInvariant()
            : "FIRST";

        if (keep is not ("FIRST" or "LAST" or "UNIQUE_ONLY"))
        {
            throw new ExecutionException($"Unsupported DEDUPLICATE KEEP option: {keep}");
        }

        string? rejectInto = stmt.Options.ContainsKey("REJECT_INTO")
            ? GetStringOption(stmt.Options, "REJECT_INTO", "DEDUPLICATE")
            : null;
        if (rejectInto != null)
        {
            if (!rejectInto.StartsWith("#", StringComparison.Ordinal))
            {
                throw new ExecutionException($"DEDUPLICATE REJECT_INTO target '{rejectInto}' must be a temporary table starting with '#'.");
            }
            if (string.Equals(rejectInto, stmt.TargetTable.TableName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ExecutionException($"DEDUPLICATE REJECT_INTO target '{rejectInto}' cannot be the same as the target table.");
            }
        }

        List<SortKey>? sortKeys = !string.IsNullOrWhiteSpace(orderByStr) ? ParseOrderBy(orderByStr) : null;

        using var staged = await StageSourceRows(stmt, context, "DEDUPLICATE");
        var (rows, columns, sourceSchema, sourceName) = (staged.Rows, staged.Columns, staged.SourceSchema, staged.SourceName);
        ValidateColumnsExist(sourceName, columns, "DEDUPLICATE",
            ("KEY_COLS", keyCols),
            ("ORDER_BY", sortKeys?.Select(sk => sk.ColumnName) ?? []));

        var targetSchemaDefs = CopySourceSchemaDefinitions(columns, sourceSchema);
        await using var sink = await TransformOutputSink.CreateAsync(stmt, context, "DEDUPLICATE", targetSchemaDefs, _logger);
        TransformOutputSink? rejectSink = rejectInto != null
            ? await TransformOutputSink.CreateForTableAsync(
                new TableReference(rejectInto),
                stmt,
                context,
                "DEDUPLICATE (REJECT_INTO)",
                CopySourceSchemaDefinitions(columns, sourceSchema),
                _logger)
            : null;

        try
        {
            var comparer = sortKeys is { Count: > 0 } ? new RowComparer(sortKeys) : null;
            foreach (var group in rows.GroupBy(row => BuildGroupKey(row, keyCols)))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                // LINQ OrderBy is a stable merge sort in .NET, preserving input order on ties.
                var groupRows = comparer != null
                    ? group.OrderBy(r => r, comparer).ToList()
                    : group.ToList();
                if (groupRows.Count == 0) continue;

                if (keep == "UNIQUE_ONLY")
                {
                    if (groupRows.Count == 1)
                    {
                        await sink.WriteRowAsync(groupRows[0]);
                    }
                    else if (rejectSink != null)
                    {
                        foreach (var rejected in groupRows)
                        {
                            await rejectSink.WriteRowAsync(rejected.Clone());
                        }
                    }
                    continue;
                }

                int keepIdx = keep == "LAST" ? groupRows.Count - 1 : 0;
                await sink.WriteRowAsync(groupRows[keepIdx]);

                if (rejectSink != null)
                {
                    for (int i = 0; i < groupRows.Count; i++)
                    {
                        if (i != keepIdx)
                        {
                            await rejectSink.WriteRowAsync(groupRows[i].Clone());
                        }
                    }
                }
            }

            await sink.CompleteAsync();
            if (rejectSink != null)
            {
                await rejectSink.CompleteAsync();
            }
        }
        finally
        {
            if (rejectSink != null)
            {
                await rejectSink.DisposeAsync();
            }
        }
    }

    private async Task ExecutePivot(TransformStatement stmt, IExecutionContext context)
    {
        ValidateAllowedOptions(stmt, "PIVOT", "ROW_FIELDS", "PIVOT_FIELD", "VALUE_FIELD", "AGGREGATE");
        if (!stmt.Options.TryGetValue("ROW_FIELDS", out var rfExpr))
        {
            throw new ExecutionException("PIVOT algorithm requires ROW_FIELDS parameter");
        }
        var rowFields = GetGroupColumns(rfExpr);
        var pivotField = GetStringOption(stmt.Options, "PIVOT_FIELD", "PIVOT");
        var valueField = GetStringOption(stmt.Options, "VALUE_FIELD", "PIVOT");
        var aggregate = stmt.Options.ContainsKey("AGGREGATE")
            ? GetStringOption(stmt.Options, "AGGREGATE", "PIVOT")
            : "SUM";
        ValidateAggregate(aggregate, "PIVOT");

        using var staged = await StageSourceRows(stmt, context, "PIVOT");
        var (rows, columns, sourceSchema, sourceName) = (staged.Rows, staged.Columns, staged.SourceSchema, staged.SourceName);
        ValidateColumnsExist(sourceName, columns, "PIVOT",
            ("ROW_FIELDS", rowFields),
            ("PIVOT_FIELD", [pivotField]),
            ("VALUE_FIELD", [valueField]));

        var pivotEntries = rows
            .Select(r => GetValue(r[pivotField]))
            .Select(v => (IsNull: v == null, ColumnName: v?.ToString() ?? "NULL"))
            .GroupBy(x => (x.IsNull, x.ColumnName.ToUpperInvariant()))
            .Select(g => g.First())
            .OrderBy(x => x.ColumnName, StringComparer.Ordinal)
            .ToList();

        var targetSchemaDefs = CopySourceSchemaDefinitions(rowFields, sourceSchema);
        var seenCols = new HashSet<string>(rowFields, StringComparer.OrdinalIgnoreCase);
        var valueSourceType = sourceSchema.TryGetValue(valueField, out var valDef) ? valDef.DataType : "DECIMAL";
        var pivotColType = TransformSchemaProjection.InferAggregateType(aggregate, valueSourceType);
        foreach (var entry in pivotEntries)
        {
            // COMPAT_BREAK: 0.23 - do not overwrite a grouping key or another pivot value.
            if (!seenCols.Add(entry.ColumnName))
            {
                throw new ExecutionException($"PIVOT output column '{entry.ColumnName}' is ambiguous. Rename the grouping column or pivot value before transforming.");
            }
            targetSchemaDefs.Add(new ColumnDefinition(entry.ColumnName, pivotColType, false) { IsNullable = true });
        }

        await using var sink = await TransformOutputSink.CreateAsync(stmt, context, "PIVOT", targetSchemaDefs, _logger);

        foreach (var group in rows.GroupBy(row => BuildGroupKey(row, rowFields)))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var groupRows = group.ToList();
            var firstRow = groupRows[0];

            var outRow = new Row(sink.Schema);
            foreach (var rf in rowFields)
            {
                outRow[rf] = firstRow[rf];
            }

            foreach (var entry in pivotEntries)
            {
                var matchingValues = groupRows
                    .Where(r =>
                    {
                        var val = GetValue(r[pivotField]);
                        return entry.IsNull
                            ? val == null
                            : val != null && string.Equals(val.ToString(), entry.ColumnName, StringComparison.OrdinalIgnoreCase);
                    })
                    .Select(r => r[valueField])
                    .ToList();

                outRow[entry.ColumnName] = AggregateValues(matchingValues, aggregate, "PIVOT");
            }

            await sink.WriteRowAsync(outRow);
        }

        await sink.CompleteAsync();
    }

    private async Task ExecuteUnpivot(TransformStatement stmt, IExecutionContext context)
    {
        var algoName = stmt.Algorithm.ToUpperInvariant();
        ValidateAllowedOptions(stmt, algoName, "ID_COLS", "VALUE_COLS", "NAME_COL", "VALUE_COL", "INCLUDE_NULLS");

        var idCols = GetOptionalColumnList(stmt.Options, "ID_COLS");
        if (!stmt.Options.TryGetValue("VALUE_COLS", out var vcExpr))
        {
            throw new ExecutionException($"{algoName} algorithm requires VALUE_COLS parameter");
        }
        var valueCols = GetGroupColumns(vcExpr);
        if (valueCols.Count == 0)
        {
            throw new ExecutionException($"{algoName} VALUE_COLS must specify at least one column.");
        }

        var distinctValueCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var vc in valueCols)
        {
            if (!distinctValueCols.Add(vc))
                throw new ExecutionException($"{algoName} VALUE_COLS contains duplicate column '{vc}'.");
        }

        var nameCol = stmt.Options.ContainsKey("NAME_COL")
            ? GetStringOption(stmt.Options, "NAME_COL", algoName)
            : "Name";
        var valueCol = stmt.Options.ContainsKey("VALUE_COL")
            ? GetStringOption(stmt.Options, "VALUE_COL", algoName)
            : "Value";

        bool includeNulls = false;
        if (stmt.Options.ContainsKey("INCLUDE_NULLS"))
        {
            var rawNulls = GetStringOption(stmt.Options, "INCLUDE_NULLS", algoName).Trim().ToUpperInvariant();
            if (rawNulls == "ON")
                includeNulls = true;
            else if (rawNulls == "OFF")
                includeNulls = false;
            else
                throw new ExecutionException($"{algoName} INCLUDE_NULLS must be ON or OFF, got '{rawNulls}'.");
        }

        using var staged = await StageSourceRows(stmt, context, algoName);
        var (rows, columns, sourceSchema, sourceName) = (staged.Rows, staged.Columns, staged.SourceSchema, staged.SourceName);
        ValidateColumnsExist(sourceName, columns, algoName,
            ("ID_COLS", idCols),
            ("VALUE_COLS", valueCols));

        var seenCols = new HashSet<string>(idCols, StringComparer.OrdinalIgnoreCase);
        if (!seenCols.Add(nameCol))
            throw new ExecutionException($"{algoName} output column NAME_COL '{nameCol}' collides with ID_COLS.");
        if (!seenCols.Add(valueCol))
            throw new ExecutionException($"{algoName} output column VALUE_COL '{valueCol}' collides with ID_COLS or NAME_COL.");

        var sourceTypeMap = sourceSchema.ToDictionary(kv => kv.Key, kv => kv.Value.DataType, StringComparer.OrdinalIgnoreCase);
        var valueColType = TransformSchemaProjection.InferUnpivotValueType(valueCols, sourceTypeMap);

        var targetSchemaDefs = CopySourceSchemaDefinitions(idCols, sourceSchema);
        targetSchemaDefs.Add(new ColumnDefinition(nameCol, "VARCHAR", false) { IsNullable = false });
        targetSchemaDefs.Add(new ColumnDefinition(valueCol, valueColType, false) { IsNullable = true });

        await using var sink = await TransformOutputSink.CreateAsync(stmt, context, algoName, targetSchemaDefs, _logger);

        foreach (var row in rows)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            foreach (var vc in valueCols)
            {
                var cellValue = GetValue(row[vc]);
                if (cellValue == null && !includeNulls)
                    continue;

                var outRow = new Row(sink.Schema);
                foreach (var idCol in idCols)
                {
                    outRow[idCol] = row[idCol];
                }
                outRow[nameCol] = vc;
                outRow[valueCol] = cellValue;
                await sink.WriteRowAsync(outRow);
            }
        }

        await sink.CompleteAsync();
    }

    private async Task ExecuteTopNOthers(TransformStatement stmt, IExecutionContext context)
    {
        ValidateAllowedOptions(stmt, "TOP_N_OTHERS", "N", "VALUE_COL", "CATEGORY_COL", "OTHERS_LABEL", "AGGREGATE", "BY_GROUP");
        var n = await EvaluateIntOption(stmt.Options, "N", "TOP_N_OTHERS", 5, context);
        if (n < 0)
        {
            throw new ExecutionException($"TOP_N_OTHERS N must be >= 0, got {n}.");
        }
        var valueCol = GetStringOption(stmt.Options, "VALUE_COL", "TOP_N_OTHERS");
        var categoryCol = GetStringOption(stmt.Options, "CATEGORY_COL", "TOP_N_OTHERS");
        var othersLabel = stmt.Options.ContainsKey("OTHERS_LABEL")
            ? GetStringOption(stmt.Options, "OTHERS_LABEL", "TOP_N_OTHERS")
            : "Others";
        var aggregate = stmt.Options.ContainsKey("AGGREGATE")
            ? GetStringOption(stmt.Options, "AGGREGATE", "TOP_N_OTHERS")
            : "SUM";
        ValidateAggregate(aggregate, "TOP_N_OTHERS");
        var groupColumns = GetOptionalColumnList(stmt.Options, "BY_GROUP");

        using var staged = await StageSourceRows(stmt, context, "TOP_N_OTHERS");
        var (rows, columns, sourceSchema, sourceName) = (staged.Rows, staged.Columns, staged.SourceSchema, staged.SourceName);
        ValidateColumnsExist(sourceName, columns, "TOP_N_OTHERS",
            ("VALUE_COL", [valueCol]),
            ("CATEGORY_COL", [categoryCol]),
            ("BY_GROUP", groupColumns));

        var targetSchemaDefs = BuildTargetSchemaDefinitions(stmt, columns, sourceSchema, forceNullablePassThrough: true);
        await using var sink = await TransformOutputSink.CreateAsync(stmt, context, "TOP_N_OTHERS", targetSchemaDefs, _logger);

        foreach (var group in rows.GroupBy(row => BuildGroupKey(row, groupColumns)))
        {
            var groupRows = group.ToList();
            if (groupRows.Count == 0) continue;

            var categoryAggs = groupRows
                .GroupBy(r =>
                {
                    var cat = GetValue(r[categoryCol]);
                    return (IsNull: cat == null, UpperKey: cat?.ToString()?.ToUpperInvariant() ?? string.Empty);
                })
                .Select(cg =>
                {
                    var catRows = cg.ToList();
                    var aggRaw = AggregateValues(catRows.Select(r => r[valueCol]).ToList(), aggregate, "TOP_N_OTHERS");
                    decimal? aggVal = aggRaw == null ? null : Convert.ToDecimal(aggRaw, CultureInfo.InvariantCulture);
                    return new
                    {
                        RawCategory = catRows[0][categoryCol],
                        Rows = catRows,
                        AggRaw = aggRaw,
                        AggValue = aggVal
                    };
                })
                .OrderByDescending(c => c.AggValue.HasValue)
                .ThenByDescending(c => c.AggValue)
                .ToList();

            var topEntries = categoryAggs.Take(n).ToList();
            var otherEntries = categoryAggs.Skip(n).ToList();

            foreach (var entry in topEntries)
            {
                var outRow = new Row(sink.Schema);
                var template = entry.Rows[0];
                foreach (var col in columns)
                {
                    if (groupColumns.Contains(col, StringComparer.OrdinalIgnoreCase))
                    {
                        outRow[col] = template[col];
                    }
                    else if (string.Equals(col, categoryCol, StringComparison.OrdinalIgnoreCase))
                    {
                        outRow[col] = entry.RawCategory;
                    }
                    else if (string.Equals(col, valueCol, StringComparison.OrdinalIgnoreCase))
                    {
                        outRow[col] = entry.AggRaw;
                    }
                    else
                    {
                        outRow[col] = null;
                    }
                }
                await sink.WriteRowAsync(outRow);
            }

            if (otherEntries.Count > 0)
            {
                var otherRows = otherEntries.SelectMany(e => e.Rows).ToList();
                var othersRow = new Row(sink.Schema);
                var firstOther = otherRows[0];
                foreach (var col in columns)
                {
                    if (groupColumns.Contains(col, StringComparer.OrdinalIgnoreCase))
                    {
                        othersRow[col] = firstOther[col];
                    }
                    else if (string.Equals(col, categoryCol, StringComparison.OrdinalIgnoreCase))
                    {
                        othersRow[col] = othersLabel;
                    }
                    else if (string.Equals(col, valueCol, StringComparison.OrdinalIgnoreCase))
                    {
                        othersRow[col] = AggregateValues(otherRows.Select(r => r[valueCol]).ToList(), aggregate, "TOP_N_OTHERS");
                    }
                    else
                    {
                        othersRow[col] = null;
                    }
                }
                await sink.WriteRowAsync(othersRow);
            }
        }

        await sink.CompleteAsync();
    }

    private async Task ExecutePeriodComparison(TransformStatement stmt, IExecutionContext context)
    {
        ValidateAllowedOptions(stmt, "PERIOD_COMPARISON", "DATE_COL", "VALUE_COL", "PERIOD", "BY_GROUP", "DIFF_COL", "PCT_COL");
        var dateCol = GetStringOption(stmt.Options, "DATE_COL", "PERIOD_COMPARISON");
        var valueCol = GetStringOption(stmt.Options, "VALUE_COL", "PERIOD_COMPARISON");
        var period = GetStringOption(stmt.Options, "PERIOD", "PERIOD_COMPARISON");
        ValidatePeriod(period);
        var groupColumns = GetOptionalColumnList(stmt.Options, "BY_GROUP");
        var diffCol = stmt.Options.ContainsKey("DIFF_COL")
            ? GetStringOption(stmt.Options, "DIFF_COL", "PERIOD_COMPARISON")
            : $"{valueCol}_Diff";
        var pctCol = stmt.Options.ContainsKey("PCT_COL")
            ? GetStringOption(stmt.Options, "PCT_COL", "PERIOD_COMPARISON")
            : $"{valueCol}_Pct";

        using var staged = await StageSourceRows(stmt, context, "PERIOD_COMPARISON");
        var (rows, columns, sourceSchema, sourceName) = (staged.Rows, staged.Columns, staged.SourceSchema, staged.SourceName);
        ValidateColumnsExist(sourceName, columns, "PERIOD_COMPARISON",
            ("DATE_COL", [dateCol]),
            ("VALUE_COL", [valueCol]),
            ("BY_GROUP", groupColumns));

        ValidateNumericColumn(rows, valueCol, "PERIOD_COMPARISON");
        var targetSchemaDefs = BuildTargetSchemaDefinitions(stmt, columns, sourceSchema);
        await using var sink = await TransformOutputSink.CreateAsync(stmt, context, "PERIOD_COMPARISON", targetSchemaDefs, _logger);

        foreach (var group in rows.GroupBy(row => BuildGroupKey(row, groupColumns)))
        {
            var sortedGroup = group
                .Select(row =>
                {
                    if (!TryGetDate(row[dateCol], out var dt))
                    {
                        throw new ExecutionException($"PERIOD_COMPARISON could not convert {dateCol} value '{row[dateCol]}' to a date.");
                    }
                    return (Row: row, Date: dt);
                })
                .OrderBy(item => item.Date)
                .ToList();

            var byDate = new Dictionary<DateTime, decimal?>();
            foreach (var dayGroup in sortedGroup.GroupBy(item => item.Date.Date))
            {
                var nonNullDayValues = dayGroup
                    .Select(item => GetValue(item.Row[valueCol]))
                    .Where(v => v != null)
                    .Select(v => ToDecimal(v!, "PERIOD_COMPARISON", valueCol))
                    .ToList();
                byDate[dayGroup.Key] = nonNullDayValues.Count > 0 ? nonNullDayValues.Sum() : null;
            }

            foreach (var item in sortedGroup)
            {
                var row = item.Row;
                var currentDate = item.Date;

                var outRow = new Row(sink.Schema);
                foreach (var col in columns)
                {
                    outRow[col] = row[col];
                }

                var rawCurVal = GetValue(row[valueCol]);
                if (rawCurVal != null && TryLookupPriorValue(byDate, currentDate, period, out var priorVal) && priorVal.HasValue)
                {
                    var curVal = ToDecimal(rawCurVal, "PERIOD_COMPARISON", valueCol);
                    outRow[diffCol] = curVal - priorVal.Value;
                    outRow[pctCol] = priorVal.Value != 0m ? (curVal - priorVal.Value) / priorVal.Value * 100m : null;
                }
                else
                {
                    outRow[diffCol] = null;
                    outRow[pctCol] = null;
                }

                await sink.WriteRowAsync(outRow);
            }
        }

        await sink.CompleteAsync();
    }

    private static bool TryLookupPriorValue(
        Dictionary<DateTime, decimal?> byDate,
        DateTime currentDate,
        string period,
        out decimal? priorValue)
    {
        var exactPrior = AddPeriod(currentDate, period, -1);
        if (byDate.TryGetValue(exactPrior.Date, out priorValue))
        {
            return true;
        }

        // Month-end alignment: e.g., 2026-02-28 (month end) looks back to 2026-01-31 (MONTH) or 2025-11-30 (QUARTER).
        if ((string.Equals(period, "MONTH", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(period, "QUARTER", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(period, "YEAR", StringComparison.OrdinalIgnoreCase)) &&
            currentDate.Day == DateTime.DaysInMonth(currentDate.Year, currentDate.Month))
        {
            var monthEndPrior = new DateTime(
                exactPrior.Year,
                exactPrior.Month,
                DateTime.DaysInMonth(exactPrior.Year, exactPrior.Month));
            if (byDate.TryGetValue(monthEndPrior, out priorValue))
            {
                return true;
            }
        }

        priorValue = null;
        return false;
    }

    private static DateTime AddPeriod(DateTime dt, string period, int count)
    {
        return period.ToUpperInvariant() switch
        {
            "DAY" => dt.AddDays(count),
            "WEEK" => dt.AddDays(count * 7),
            "MONTH" => dt.AddMonths(count),
            "QUARTER" => dt.AddMonths(count * 3),
            "YEAR" => dt.AddYears(count),
            _ => throw new ExecutionException($"Unsupported comparison period: {period}")
        };
    }

    private async Task ExecuteShareOfTotal(TransformStatement stmt, IExecutionContext context)
    {
        ValidateAllowedOptions(stmt, "SHARE_OF_TOTAL", "VALUE_COL", "BY_GROUP", "SHARE_COL");
        var valueCol = GetStringOption(stmt.Options, "VALUE_COL", "SHARE_OF_TOTAL");
        var groupColumns = GetOptionalColumnList(stmt.Options, "BY_GROUP");
        var shareCol = stmt.Options.ContainsKey("SHARE_COL")
            ? GetStringOption(stmt.Options, "SHARE_COL", "SHARE_OF_TOTAL")
            : $"{valueCol}_Share";

        using var staged = await StageSourceRows(stmt, context, "SHARE_OF_TOTAL");
        var (rows, columns, sourceSchema, sourceName) = (staged.Rows, staged.Columns, staged.SourceSchema, staged.SourceName);
        ValidateColumnsExist(sourceName, columns, "SHARE_OF_TOTAL",
            ("VALUE_COL", [valueCol]),
            ("BY_GROUP", groupColumns));

        ValidateNumericColumn(rows, valueCol, "SHARE_OF_TOTAL");
        var targetSchemaDefs = BuildTargetSchemaDefinitions(stmt, columns, sourceSchema);
        await using var sink = await TransformOutputSink.CreateAsync(stmt, context, "SHARE_OF_TOTAL", targetSchemaDefs, _logger);

        foreach (var group in rows.GroupBy(row => BuildGroupKey(row, groupColumns)))
        {
            var groupRows = group.ToList();
            var totalSum = groupRows
                .Select(r => GetValue(r[valueCol]))
                .Where(v => v != null)
                .Select(v => ToDecimal(v!, "SHARE_OF_TOTAL", valueCol))
                .Sum();

            foreach (var row in groupRows)
            {
                var outRow = new Row(sink.Schema);
                foreach (var col in columns)
                {
                    outRow[col] = row[col];
                }

                var rawVal = GetValue(row[valueCol]);
                if (rawVal == null)
                {
                    outRow[shareCol] = null;
                }
                else
                {
                    var val = ToDecimal(rawVal, "SHARE_OF_TOTAL", valueCol);
                    outRow[shareCol] = totalSum != 0m ? val / totalSum : 0m;
                }

                await sink.WriteRowAsync(outRow);
            }
        }

        await sink.CompleteAsync();
    }

    private async Task ExecuteNormalize(TransformStatement stmt, IExecutionContext context)
    {
        ValidateAllowedOptions(stmt, "NORMALIZE", "VALUE_COL", "VALUE_COLS", "METHOD", "BY_GROUP", "NORM_COL");
        var valueCols = GetValueColumnList(stmt.Options, "NORMALIZE");
        var method = stmt.Options.ContainsKey("METHOD")
            ? GetStringOption(stmt.Options, "METHOD", "NORMALIZE")
            : "MIN_MAX";
        var groupColumns = GetOptionalColumnList(stmt.Options, "BY_GROUP");

        var explicitNormCols = GetOptionalColumnList(stmt.Options, "NORM_COL");
        if (explicitNormCols.Count > 0 && explicitNormCols.Count != valueCols.Count)
        {
            throw new ExecutionException(
                $"NORMALIZE NORM_COL count ({explicitNormCols.Count}) must match VALUE_COLS count ({valueCols.Count}).");
        }
        var normCols = valueCols
            .Select((col, idx) => explicitNormCols.Count > 0 ? explicitNormCols[idx] : $"{col}_Normalized")
            .ToList();

        bool isMinMax = string.Equals(method, "MIN_MAX", StringComparison.OrdinalIgnoreCase);
        bool isZScore = string.Equals(method, "Z_SCORE", StringComparison.OrdinalIgnoreCase);
        if (!isMinMax && !isZScore)
        {
            throw new ExecutionException($"Unsupported NORMALIZE method: {method}");
        }

        using var staged = await StageSourceRows(stmt, context, "NORMALIZE");
        var (rows, columns, sourceSchema, sourceName) = (staged.Rows, staged.Columns, staged.SourceSchema, staged.SourceName);
        ValidateColumnsExist(sourceName, columns, "NORMALIZE",
            ("VALUE_COLS", valueCols),
            ("BY_GROUP", groupColumns));

        foreach (var numericCol in valueCols) ValidateNumericColumn(rows, numericCol, "NORMALIZE");
        var targetSchemaDefs = BuildTargetSchemaDefinitions(stmt, columns, sourceSchema);
        await using var sink = await TransformOutputSink.CreateAsync(stmt, context, "NORMALIZE", targetSchemaDefs, _logger);

        foreach (var group in rows.GroupBy(row => BuildGroupKey(row, groupColumns)))
        {
            var groupRows = group.ToList();
            if (groupRows.Count == 0) continue;

            var computedByRow = new Dictionary<string, decimal?[]>(StringComparer.OrdinalIgnoreCase);
            for (int colIdx = 0; colIdx < valueCols.Count; colIdx++)
            {
                var valueCol = valueCols[colIdx];
                var normCol = normCols[colIdx];
                var normalized = new decimal?[groupRows.Count];

                if (isMinMax)
                {
                    var nonNullVals = groupRows
                        .Select(r => GetValue(r[valueCol]))
                        .Where(v => v != null)
                        .Select(v => ToDecimal(v!, "NORMALIZE", valueCol))
                        .ToList();
                    var min = nonNullVals.Count > 0 ? nonNullVals.Min() : 0m;
                    var max = nonNullVals.Count > 0 ? nonNullVals.Max() : 0m;
                    var range = max - min;

                    for (int rIdx = 0; rIdx < groupRows.Count; rIdx++)
                    {
                        var rawVal = GetValue(groupRows[rIdx][valueCol]);
                        if (rawVal != null)
                        {
                            var val = ToDecimal(rawVal, "NORMALIZE", valueCol);
                            normalized[rIdx] = range != 0m ? (val - min) / range : 0m;
                        }
                    }
                }
                else
                {
                    var nonNullVals = groupRows
                        .Select(r => GetValue(r[valueCol]))
                        .Where(v => v != null)
                        .Select(v => ToDouble(v!, "NORMALIZE", valueCol))
                        .ToList();
                    var count = nonNullVals.Count;
                    var mean = count > 0 ? nonNullVals.Average() : 0.0;
                    var sumOfSquares = count > 0 ? nonNullVals.Select(v => Math.Pow(v - mean, 2)).Sum() : 0.0;
                    var stddev = count > 0 ? Math.Sqrt(sumOfSquares / count) : 0.0;

                    for (int rIdx = 0; rIdx < groupRows.Count; rIdx++)
                    {
                        var rawVal = GetValue(groupRows[rIdx][valueCol]);
                        if (rawVal != null)
                        {
                            var val = ToDouble(rawVal, "NORMALIZE", valueCol);
                            normalized[rIdx] = stddev != 0.0 ? (decimal)((val - mean) / stddev) : 0m;
                        }
                    }
                }

                computedByRow[normCol] = normalized;
            }

            for (int rIdx = 0; rIdx < groupRows.Count; rIdx++)
            {
                var row = groupRows[rIdx];
                var outRow = new Row(sink.Schema);
                foreach (var col in columns)
                {
                    outRow[col] = row[col];
                }
                foreach (var normCol in normCols)
                {
                    outRow[normCol] = computedByRow[normCol][rIdx];
                }
                await sink.WriteRowAsync(outRow);
            }
        }

        await sink.CompleteAsync();
    }

    private async Task ExecuteRollingAggregate(TransformStatement stmt, IExecutionContext context)
    {
        ValidateAllowedOptions(stmt, "ROLLING_AGGREGATE", "VALUE_COL", "ORDER_COL", "WINDOW_SIZE", "AGGREGATE", "BY_GROUP", "ROLLING_COL");
        var valueCol = GetStringOption(stmt.Options, "VALUE_COL", "ROLLING_AGGREGATE");
        var orderCol = GetStringOption(stmt.Options, "ORDER_COL", "ROLLING_AGGREGATE");
        var windowSize = await EvaluateIntOption(stmt.Options, "WINDOW_SIZE", "ROLLING_AGGREGATE", 7, context);
        if (windowSize < 1)
        {
            throw new ExecutionException($"ROLLING_AGGREGATE WINDOW_SIZE must be >= 1, got {windowSize}.");
        }
        var aggregate = stmt.Options.ContainsKey("AGGREGATE")
            ? GetStringOption(stmt.Options, "AGGREGATE", "ROLLING_AGGREGATE")
            : "AVG";
        ValidateAggregate(aggregate, "ROLLING_AGGREGATE");
        var groupColumns = GetOptionalColumnList(stmt.Options, "BY_GROUP");
        var rollingCol = stmt.Options.ContainsKey("ROLLING_COL")
            ? GetStringOption(stmt.Options, "ROLLING_COL", "ROLLING_AGGREGATE")
            : $"{valueCol}_Rolling";

        using var staged = await StageSourceRows(stmt, context, "ROLLING_AGGREGATE");
        var (rows, columns, sourceSchema, sourceName) = (staged.Rows, staged.Columns, staged.SourceSchema, staged.SourceName);
        ValidateColumnsExist(sourceName, columns, "ROLLING_AGGREGATE",
            ("VALUE_COL", [valueCol]),
            ("ORDER_COL", [orderCol]),
            ("BY_GROUP", groupColumns));

        var targetSchemaDefs = BuildTargetSchemaDefinitions(stmt, columns, sourceSchema);
        await using var sink = await TransformOutputSink.CreateAsync(stmt, context, "ROLLING_AGGREGATE", targetSchemaDefs, _logger);

        foreach (var group in rows.GroupBy(row => BuildGroupKey(row, groupColumns)))
        {
            var sortedGroup = group
                .Select(row => (Row: row, Order: GetOrderValue(row[orderCol], "ROLLING_AGGREGATE", orderCol)))
                .OrderBy(item => item.Order)
                .ToList();
            int n = sortedGroup.Count;

            for (int i = 0; i < n; i++)
            {
                var startIdx = Math.Max(0, i - windowSize + 1);
                var windowValues = new List<object?>(i - startIdx + 1);
                for (int w = startIdx; w <= i; w++)
                {
                    windowValues.Add(sortedGroup[w].Row[valueCol]);
                }

                var outRow = new Row(sink.Schema);
                foreach (var col in columns)
                {
                    outRow[col] = sortedGroup[i].Row[col];
                }

                outRow[rollingCol] = AggregateValues(windowValues, aggregate, "ROLLING_AGGREGATE");
                await sink.WriteRowAsync(outRow);
            }
        }

        await sink.CompleteAsync();
    }

    private static List<string> GetValueColumnList(Dictionary<string, Expression> options, string algorithm)
    {
        bool hasSingle = options.ContainsKey("VALUE_COL");
        bool hasMulti = options.ContainsKey("VALUE_COLS");
        if (hasSingle && hasMulti)
        {
            throw new ExecutionException($"{algorithm} accepts either VALUE_COL or VALUE_COLS, not both.");
        }
        if (!hasSingle && !hasMulti)
        {
            throw new ExecutionException($"Algorithm {algorithm} requires option 'VALUE_COL' or 'VALUE_COLS'");
        }

        var key = hasMulti ? "VALUE_COLS" : "VALUE_COL";
        var cols = GetGroupColumns(options[key]);
        if (cols.Count == 0)
        {
            throw new ExecutionException($"{algorithm} {key} must specify at least one column.");
        }
        return cols;
    }

    private static void ValidateAllowedOptions(
        TransformStatement stmt,
        string algorithm,
        params string[] allowedKeys)
    {
        var allowed = new HashSet<string>(allowedKeys, StringComparer.OrdinalIgnoreCase);
        foreach (var key in stmt.Options.Keys)
        {
            if (!allowed.Contains(key))
            {
                throw new ExecutionException(
                    $"Unknown option '{key}' for TRANSFORM {algorithm}. Allowed options: {string.Join(", ", allowedKeys)}.");
            }
        }
    }

    private static void ValidateColumnsExist(
        string sourceTableName,
        IReadOnlyCollection<string> columns,
        string algorithm,
        params (string OptionName, IEnumerable<string> ColumnNames)[] checks)
    {
        foreach (var (optionName, columnNames) in checks)
        {
            foreach (var col in columnNames)
            {
                if (!columns.Contains(col, StringComparer.OrdinalIgnoreCase))
                {
                    throw new ExecutionException(
                        $"{algorithm} {optionName} column '{col}' was not found in {sourceTableName}.");
                }
            }
        }
    }

    private static void ValidateAggregate(string aggregate, string algorithm)
    {
        if (!SupportedAggregates.Contains(aggregate))
        {
            throw new ExecutionException($"Unsupported {algorithm} aggregate function: {aggregate}");
        }
    }

    private static void ValidateFillInterval(string interval)
    {
        if (interval is not ("HOUR" or "DAY" or "WEEK" or "MONTH" or "QUARTER" or "YEAR"))
        {
            throw new ExecutionException($"Unsupported FILL_DATES INTERVAL: {interval}");
        }
    }

    private static DateTime NormalizeIntervalBucket(DateTime dt, string interval) => interval.ToUpperInvariant() switch
    {
        "HOUR" => new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0, dt.Kind),
        _ => dt.Date
    };

    private static DateTime StepFromStart(DateTime start, string interval, int step) => interval.ToUpperInvariant() switch
    {
        "HOUR" => start.AddHours(step),
        "DAY" => start.AddDays(step),
        "WEEK" => start.AddDays(step * 7),
        "MONTH" => start.AddMonths(step),
        "QUARTER" => start.AddMonths(step * 3),
        "YEAR" => start.AddYears(step),
        _ => throw new ExecutionException($"Unsupported FILL_DATES INTERVAL: {interval}")
    };

    private static int LastFillStep(DateTime start, DateTime end, string interval) => interval switch
    {
        "HOUR" => (int)((end.Ticks - start.Ticks) / TimeSpan.TicksPerHour),
        "DAY" => (end - start).Days,
        "WEEK" => (end - start).Days / 7,
        "MONTH" => (end.Year - start.Year) * 12 + end.Month - start.Month,
        "QUARTER" => ((end.Year - start.Year) * 12 + end.Month - start.Month) / 3,
        "YEAR" => end.Year - start.Year,
        _ => throw new ExecutionException($"Unsupported FILL_DATES INTERVAL: {interval}")
    };

    private static async Task<DateTime?> EvaluateOptionalBucketDateAsync(
        Dictionary<string, Expression> options,
        string key,
        string interval,
        IExecutionContext context)
    {
        if (!options.TryGetValue(key, out var expr))
            return null;

        var raw = GetValue(await context.EvaluationContext.EvaluateValue(expr, Row.Empty));
        if (!TryGetDateTime(raw, out var dt))
        {
            throw new ExecutionException($"FILL_DATES {key} value '{raw}' could not be converted to a date.");
        }
        return NormalizeIntervalBucket(dt, interval);
    }

    private static void ValidatePeriod(string period)
    {
        if (!string.Equals(period, "DAY", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(period, "WEEK", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(period, "MONTH", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(period, "QUARTER", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(period, "YEAR", StringComparison.OrdinalIgnoreCase))
        {
            throw new ExecutionException($"Unsupported comparison period: {period}");
        }
    }

    private static async Task<int> EvaluateIntOption(
        Dictionary<string, Expression> options,
        string key,
        string algorithm,
        int defaultValue,
        IExecutionContext context)
    {
        if (!options.TryGetValue(key, out var expr))
        {
            return defaultValue;
        }

        var raw = GetValue(await context.EvaluationContext.EvaluateValue(expr, Row.Empty));
        if (raw == null)
        {
            throw new ExecutionException($"{algorithm} {key} cannot be NULL.");
        }

        try
        {
            return Convert.ToInt32(raw, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw new ExecutionException($"{algorithm} {key} value '{raw}' must be an integer.", ex);
        }
    }

    private static string GetStringOption(Dictionary<string, Expression> options, string key, string algorithm)
    {
        if (!options.TryGetValue(key, out var expr))
            throw new ExecutionException($"Algorithm {algorithm} requires option '{key}'");

        if (expr is LiteralExpression { Value: string s })
            return s;
        if (expr is IdentifierExpression id)
            return id.Name;
        throw new ExecutionException($"Option '{key}' for algorithm {algorithm} must be a string literal or identifier");
    }

    private static List<string> GetOptionalColumnList(Dictionary<string, Expression> options, string key)
    {
        return options.TryGetValue(key, out var expr) ? GetGroupColumns(expr) : new List<string>();
    }

    private static List<string> GetGroupColumns(Expression expr)
    {
        string val;
        if (expr is LiteralExpression { Value: string s })
            val = s;
        else if (expr is IdentifierExpression id)
            val = id.Name;
        else
            throw new ExecutionException("Option must be a string literal or identifier");

        return val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static string BuildGroupKey(Row row, IReadOnlyList<string> groupColumns)
    {
        if (groupColumns.Count == 0) return string.Empty;

        var parts = new string[groupColumns.Count];
        for (int i = 0; i < groupColumns.Count; i++)
        {
            var val = GetValue(row[groupColumns[i]]);
            if (val == null)
            {
                parts[i] = "N";
                continue;
            }

            string text = val switch
            {
                decimal dec => (dec / 1.00000000000000000000000000000m).ToString(CultureInfo.InvariantCulture),
                DateTime dt => dt.Ticks.ToString(CultureInfo.InvariantCulture),
                DateOnly dOnly => dOnly.ToDateTime(TimeOnly.MinValue).Ticks.ToString(CultureInfo.InvariantCulture),
                _ => Convert.ToString(val, CultureInfo.InvariantCulture) ?? string.Empty
            };
            parts[i] = $"V{text.Length}:{text}";
        }
        return string.Join('|', parts);
    }

    private static Row CreateGapRow(
        TableSchema schema,
        IReadOnlyList<string> columns,
        string dateColumn,
        List<string> groupColumns,
        Row template,
        DateTime date,
        bool isForwardFill,
        Row? lastEmittedRow,
        object? gapFillValue,
        bool isHourly)
    {
        var row = new Row(schema);
        foreach (var column in columns)
        {
            if (column.Equals(dateColumn, StringComparison.OrdinalIgnoreCase))
            {
                // Keep the column's type: hourly intervals require DateTime; daily+ preserve DateOnly when present.
                row[column] = !isHourly && template[column] is DateOnly ? DateOnly.FromDateTime(date) : date;
            }
            else if (groupColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
            {
                row[column] = template[column];
            }
            else if (isForwardFill)
            {
                row[column] = lastEmittedRow != null ? lastEmittedRow[column] : null;
            }
            else
            {
                row[column] = gapFillValue;
            }
        }
        return row;
    }

    private static bool TryGetDateTime(object? value, out DateTime dateTime)
    {
        var v = GetValue(value);
        if (v == null)
        {
            dateTime = default;
            return false;
        }
        return EvaluationUtils.TryToDateTime(v, out dateTime);
    }

    private static bool TryGetDate(object? value, out DateTime date)
    {
        if (!TryGetDateTime(value, out var dt))
        {
            date = default;
            return false;
        }
        date = dt.Date;
        return true;
    }

    private static object? GetValue(object? val)
    {
        if (val == null || val == DBNull.Value) return null;
        return val;
    }

    private static decimal GetOrderValue(object? val, string algorithm, string orderCol)
    {
        var v = GetValue(val);
        if (v == null)
        {
            throw new ExecutionException($"{algorithm} ORDER_COL '{orderCol}' cannot contain NULL values.");
        }
        if (v is DateTime dt) return dt.Ticks;
        if (v is DateOnly dateOnly) return dateOnly.ToDateTime(TimeOnly.MinValue).Ticks;
        if (v is DateTimeOffset dto) return dto.DateTime.Ticks;
        if (v is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
        {
            return Convert.ToDecimal(v, CultureInfo.InvariantCulture);
        }
        if (v is string s)
        {
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            {
                return d;
            }
            if (EvaluationUtils.TryToDateTime(s, out var parsedDate))
            {
                return parsedDate.Ticks;
            }
        }
        throw new ExecutionException($"Cannot convert {algorithm} ORDER_COL '{orderCol}' value '{val}' to a numeric or date value.");
    }

    private static decimal LinearInterpolate(decimal y0, decimal y1, decimal x0, decimal x1, decimal x)
    {
        try
        {
            return y0 + (y1 - y0) * (x - x0) / (x1 - x0);
        }
        catch (OverflowException)
        {
            double frac = (double)(x - x0) / (double)(x1 - x0);
            return (decimal)((double)y0 + ((double)(y1 - y0) * frac));
        }
    }

    private static decimal ToDecimal(object value, string algorithm, string column)
    {
        try
        {
            return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw new ExecutionException($"{algorithm} column '{column}' value '{DescribeValue(value)}' is not numeric.", ex);
        }
    }

    private static double ToDouble(object value, string algorithm, string column)
    {
        try
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw new ExecutionException($"{algorithm} column '{column}' value '{DescribeValue(value)}' is not numeric.", ex);
        }
    }

    private record SortKey(string ColumnName, bool Descending);

    private static List<SortKey> ParseOrderBy(string orderBy)
    {
        var result = new List<SortKey>();
        var parts = orderBy.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var subParts = part.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (subParts.Length == 0) continue;
            if (subParts.Length > 2)
            {
                throw new ExecutionException($"Invalid DEDUPLICATE ORDER_BY clause: '{part}'.");
            }
            var colName = subParts[0];
            bool descending = false;
            if (subParts.Length == 2)
            {
                if (string.Equals(subParts[1], "DESC", StringComparison.OrdinalIgnoreCase))
                {
                    descending = true;
                }
                else if (!string.Equals(subParts[1], "ASC", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ExecutionException($"Invalid DEDUPLICATE ORDER_BY sort direction '{subParts[1]}' in '{part}'. Expected ASC or DESC.");
                }
            }
            result.Add(new SortKey(colName, descending));
        }
        return result;
    }

    private class RowComparer(List<SortKey> sortKeys) : IComparer<Row>
    {
        public int Compare(Row? x, Row? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            foreach (var sk in sortKeys)
            {
                var vx = x[sk.ColumnName];
                var vy = y[sk.ColumnName];
                int cmp = CompareValues(vx, vy);
                if (cmp != 0)
                {
                    return sk.Descending ? -cmp : cmp;
                }
            }
            return 0;
        }
    }

    private static int CompareValues(object? x, object? y)
    {
        return EvaluationUtils.CompareConstants(x, y, caseSensitive: true);
    }

    private static object? AggregateValues(List<object?> values, string aggregate, string algorithm)
    {
        var nonNulls = values.Select(GetValue).Where(v => v != null).Cast<object>().ToList();
        if (string.Equals(aggregate, "COUNT", StringComparison.OrdinalIgnoreCase))
        {
            return (decimal)nonNulls.Count;
        }

        if (nonNulls.Count == 0) return null;

        if (string.Equals(aggregate, "SUM", StringComparison.OrdinalIgnoreCase))
        {
            return nonNulls.Select(v => Convert.ToDecimal(v, CultureInfo.InvariantCulture)).Sum();
        }
        if (string.Equals(aggregate, "AVG", StringComparison.OrdinalIgnoreCase))
        {
            return nonNulls.Select(v => Convert.ToDecimal(v, CultureInfo.InvariantCulture)).Average();
        }
        if (string.Equals(aggregate, "MIN", StringComparison.OrdinalIgnoreCase))
        {
            nonNulls.Sort(CompareValues);
            return nonNulls[0];
        }
        if (string.Equals(aggregate, "MAX", StringComparison.OrdinalIgnoreCase))
        {
            nonNulls.Sort(CompareValues);
            return nonNulls[^1];
        }

        throw new ExecutionException($"Unsupported {algorithm} aggregate function: {aggregate}");
    }

    private async Task ExecuteClean(TransformStatement stmt, IExecutionContext context)
    {
        ValidateAllowedOptions(stmt, "CLEAN", "COLS", "RULES");

        var colsSpec = stmt.Options.ContainsKey("COLS")
            ? GetStringOption(stmt.Options, "COLS", "CLEAN")
            : "*";
        var rulesSpec = stmt.Options.ContainsKey("RULES")
            ? GetStringOption(stmt.Options, "RULES", "CLEAN")
            : null;
        var rules = ETL_SQL.Engine.Functions.TextCleaningEngine.ParseRules(rulesSpec);

        using var staged = await StageSourceRows(stmt, context, "CLEAN");
        var columns = staged.Columns;
        var sourceSchema = staged.SourceSchema;

        var targetCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (colsSpec.Trim() == "*")
        {
            foreach (var col in columns)
            {
                if (sourceSchema.TryGetValue(col, out var def))
                {
                    if (IsTextColumnType(def.DataType))
                        targetCols.Add(col);
                }
                else if (staged.Rows.Any(r => r.TryGetValue(col, out var v) && v is string))
                {
                    targetCols.Add(col);
                }
            }
        }
        else
        {
            var requested = colsSpec
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            if (requested.Count == 0)
                throw new ExecutionException("CLEAN requires COLS = '*' or a non-empty comma-separated list of column names.");

            var knownCols = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);
            foreach (var col in requested)
            {
                if (!knownCols.Contains(col))
                    throw new ExecutionException($"CLEAN column '{col}' does not exist in source table '{staged.SourceName}'.");
                targetCols.Add(col);
            }
        }

        var targetColumnDefs = CopySourceSchemaDefinitions(columns, sourceSchema);

        await using var sink = await TransformOutputSink.CreateAsync(stmt, context, "CLEAN", targetColumnDefs, _logger);
        foreach (var row in staged.Rows)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var outRow = new Row(sink.Schema);
            foreach (var col in columns)
            {
                var val = GetValue(row[col]);
                if (val != null && targetCols.Contains(col))
                {
                    var text = TypeConverter.ToInvariantText(val);
                    outRow[col] = ETL_SQL.Engine.Functions.TextCleaningEngine.ApplyRules(text, rules);
                }
                else
                {
                    outRow[col] = val;
                }
            }
            await sink.WriteRowAsync(outRow);
        }

        await sink.CompleteAsync();
    }

    private static bool IsTextColumnType(string? dataType)
    {
        if (string.IsNullOrWhiteSpace(dataType)) return false;
        var upper = dataType.Trim().ToUpperInvariant();
        return upper is "STRING" or "VARCHAR" or "NVARCHAR" or "CHAR" or "NCHAR" or "TEXT" or "CLOB"
            || upper.StartsWith("VARCHAR(", StringComparison.Ordinal)
            || upper.StartsWith("NVARCHAR(", StringComparison.Ordinal)
            || upper.StartsWith("CHAR(", StringComparison.Ordinal)
            || upper.StartsWith("NCHAR(", StringComparison.Ordinal);
    }

    private async Task ExecuteBin(TransformStatement stmt, IExecutionContext context)
    {
        var algoName = stmt.Algorithm.ToUpperInvariant();
        ValidateAllowedOptions(stmt, algoName,
            "VALUE_COL", "BINS", "METHOD", "THRESHOLDS", "BY_GROUP", "BIN_START_COL", "BIN_END_COL", "BIN_LABEL_COL");

        var valueCol = GetStringOption(stmt.Options, "VALUE_COL", algoName);
        var method = stmt.Options.ContainsKey("METHOD")
            ? GetStringOption(stmt.Options, "METHOD", algoName).Trim().ToUpperInvariant()
            : "EQUAL_WIDTH";
        if (method is not ("EQUAL_WIDTH" or "QUANTILE" or "THRESHOLDS"))
            throw new ExecutionException($"{algoName} METHOD must be EQUAL_WIDTH, QUANTILE, or THRESHOLDS, got '{method}'.");

        var startCol = stmt.Options.ContainsKey("BIN_START_COL") ? GetStringOption(stmt.Options, "BIN_START_COL", algoName) : "Bin_Start";
        var endCol = stmt.Options.ContainsKey("BIN_END_COL") ? GetStringOption(stmt.Options, "BIN_END_COL", algoName) : "Bin_End";
        var labelCol = stmt.Options.ContainsKey("BIN_LABEL_COL") ? GetStringOption(stmt.Options, "BIN_LABEL_COL", algoName) : "Bin_Label";
        var groupCols = GetOptionalColumnList(stmt.Options, "BY_GROUP");

        int binCount = 5;
        List<decimal>? customThresholds = null;
        if (method == "THRESHOLDS")
        {
            var rawSpec = stmt.Options.ContainsKey("THRESHOLDS")
                ? GetStringOption(stmt.Options, "THRESHOLDS", algoName)
                : stmt.Options.ContainsKey("BINS")
                    ? GetStringOption(stmt.Options, "BINS", algoName)
                    : throw new ExecutionException($"{algoName} with METHOD = 'THRESHOLDS' requires THRESHOLDS or BINS option.");
            customThresholds = ParseThresholds(rawSpec, algoName);
        }
        else if (stmt.Options.ContainsKey("BINS"))
        {
            binCount = await EvaluateIntOption(stmt.Options, "BINS", algoName, 5, context);
            if (binCount < 1)
                throw new ExecutionException($"{algoName} BINS must be a positive integer, got {binCount}.");
        }

        using var staged = await StageSourceRows(stmt, context, algoName);
        ValidateColumnsExist(staged.SourceName, staged.Columns, algoName,
            ("VALUE_COL", [valueCol]),
            ("BY_GROUP", groupCols));
        // Fail on a non-numeric VALUE_COL before the sink truncates the target, not mid-write.
        ValidateNumericColumn(staged.Rows, valueCol, algoName);

        var targetColumnDefs = CopySourceSchemaDefinitions(staged.Columns, staged.SourceSchema);
        AddOrUpdateColumnDef(targetColumnDefs, startCol, "DECIMAL", isNullable: true);
        AddOrUpdateColumnDef(targetColumnDefs, endCol, "DECIMAL", isNullable: true);
        AddOrUpdateColumnDef(targetColumnDefs, labelCol, "VARCHAR", isNullable: true);

        await using var sink = await TransformOutputSink.CreateAsync(stmt, context, algoName, targetColumnDefs, _logger);

        var groups = groupCols.Count > 0
            ? staged.Rows.GroupBy(r => BuildGroupKey(r, groupCols)).Select(g => g.ToList()).ToList()
            : staged.Rows.Count > 0 ? new List<List<Row>> { staged.Rows } : new List<List<Row>>();

        foreach (var groupRows in groups)
        {
            var nonNulls = new List<decimal>();
            foreach (var r in groupRows)
            {
                if (TryGetNumericDecimal(GetValue(r[valueCol]), algoName, valueCol, out var d))
                    nonNulls.Add(d);
            }

            List<decimal>? edges = null;
            if (method == "THRESHOLDS")
            {
                edges = customThresholds;
            }
            else if (nonNulls.Count > 0)
            {
                nonNulls.Sort();
                edges = method == "EQUAL_WIDTH"
                    ? BuildEqualWidthEdges(nonNulls[0], nonNulls[^1], binCount)
                    : BuildQuantileEdges(nonNulls, binCount);
            }

            foreach (var row in groupRows)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var outRow = new Row(sink.Schema);
                foreach (var col in staged.Columns)
                    outRow[col] = GetValue(row[col]);

                var raw = GetValue(row[valueCol]);
                if (edges == null || !TryGetNumericDecimal(raw, algoName, valueCol, out var val))
                {
                    outRow[startCol] = null;
                    outRow[endCol] = null;
                    outRow[labelCol] = null;
                }
                else
                {
                    var (bStart, bEnd, bLabel) = AssignBin(val, edges, method == "THRESHOLDS");
                    outRow[startCol] = bStart;
                    outRow[endCol] = bEnd;
                    outRow[labelCol] = bLabel;
                }
                await sink.WriteRowAsync(outRow);
            }
        }

        await sink.CompleteAsync();
    }

    private async Task ExecuteOutliers(TransformStatement stmt, IExecutionContext context)
    {
        const string algoName = "OUTLIERS";
        ValidateAllowedOptions(stmt, algoName,
            "VALUE_COL", "METHOD", "ACTION", "THRESHOLD", "LOWER_PCT", "UPPER_PCT", "FLAG_COL", "BY_GROUP");

        var valueCol = GetStringOption(stmt.Options, "VALUE_COL", algoName);
        var method = stmt.Options.ContainsKey("METHOD")
            ? GetStringOption(stmt.Options, "METHOD", algoName).Trim().ToUpperInvariant()
            : "IQR";
        if (method is not ("IQR" or "Z_SCORE" or "PERCENTILE"))
            throw new ExecutionException($"{algoName} METHOD must be IQR, Z_SCORE, or PERCENTILE, got '{method}'.");

        var action = stmt.Options.ContainsKey("ACTION")
            ? GetStringOption(stmt.Options, "ACTION", algoName).Trim().ToUpperInvariant()
            : "CLIP";
        if (action is not ("CLIP" or "FLAG" or "FILTER" or "NULL"))
            throw new ExecutionException($"{algoName} ACTION must be CLIP, FLAG, FILTER, or NULL, got '{action}'.");

        var flagCol = stmt.Options.ContainsKey("FLAG_COL")
            ? GetStringOption(stmt.Options, "FLAG_COL", algoName)
            : $"{valueCol}_IsOutlier";
        var groupCols = GetOptionalColumnList(stmt.Options, "BY_GROUP");

        double defaultThreshold = method switch
        {
            "IQR" => 1.5,
            "Z_SCORE" => 3.0,
            _ => 5.0
        };
        double threshold = stmt.Options.ContainsKey("THRESHOLD")
            ? await EvaluateDoubleOption(stmt.Options, "THRESHOLD", algoName, defaultThreshold, context)
            : defaultThreshold;
        if (threshold <= 0)
            throw new ExecutionException($"{algoName} THRESHOLD must be positive, got {threshold}.");

        double lowerPct = stmt.Options.ContainsKey("LOWER_PCT")
            ? await EvaluateDoubleOption(stmt.Options, "LOWER_PCT", algoName, threshold, context)
            : threshold;
        double upperPct = stmt.Options.ContainsKey("UPPER_PCT")
            ? await EvaluateDoubleOption(stmt.Options, "UPPER_PCT", algoName, 100.0 - lowerPct, context)
            : 100.0 - lowerPct;
        if (method == "PERCENTILE" && (lowerPct < 0 || upperPct > 100 || lowerPct >= upperPct))
            throw new ExecutionException($"{algoName} PERCENTILE requires 0 <= LOWER_PCT < UPPER_PCT <= 100, got [{lowerPct}, {upperPct}].");

        using var staged = await StageSourceRows(stmt, context, algoName);
        ValidateColumnsExist(staged.SourceName, staged.Columns, algoName,
            ("VALUE_COL", [valueCol]),
            ("BY_GROUP", groupCols));
        // Fail on a non-numeric VALUE_COL before the sink truncates the target, not mid-write.
        ValidateNumericColumn(staged.Rows, valueCol, algoName);

        var targetColumnDefs = CopySourceSchemaDefinitions(staged.Columns, staged.SourceSchema);
        if (action == "FLAG")
        {
            AddOrUpdateColumnDef(targetColumnDefs, flagCol, "INT", isNullable: true);
        }
        else if (action == "CLIP")
        {
            var existing = targetColumnDefs.FindIndex(c => c.ColumnName.Equals(valueCol, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0 && IsIntegralDataType(targetColumnDefs[existing].DataType))
                targetColumnDefs[existing] = new ColumnDefinition(targetColumnDefs[existing].ColumnName, "DECIMAL", isIdentity: false) { IsNullable = targetColumnDefs[existing].IsNullable };
        }
        else if (action == "NULL")
        {
            // ACTION = NULL writes NULL into VALUE_COL, so it must accept NULL even if the source did not.
            var existing = targetColumnDefs.FindIndex(c => c.ColumnName.Equals(valueCol, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0 && !targetColumnDefs[existing].IsNullable)
                targetColumnDefs[existing].IsNullable = true;
        }

        await using var sink = await TransformOutputSink.CreateAsync(stmt, context, algoName, targetColumnDefs, _logger);

        var groups = groupCols.Count > 0
            ? staged.Rows.GroupBy(r => BuildGroupKey(r, groupCols)).Select(g => g.ToList()).ToList()
            : staged.Rows.Count > 0 ? new List<List<Row>> { staged.Rows } : new List<List<Row>>();

        foreach (var groupRows in groups)
        {
            var nonNulls = new List<double>();
            foreach (var r in groupRows)
            {
                if (TryGetNumericDecimal(GetValue(r[valueCol]), algoName, valueCol, out var d))
                    nonNulls.Add((double)d);
            }

            double? lowBound = null;
            double? highBound = null;
            if (nonNulls.Count > 0)
            {
                nonNulls.Sort();
                if (method == "IQR")
                {
                    var q1 = ComputePercentile(nonNulls, 0.25);
                    var q3 = ComputePercentile(nonNulls, 0.75);
                    var iqr = q3 - q1;
                    lowBound = q1 - threshold * iqr;
                    highBound = q3 + threshold * iqr;
                }
                else if (method == "Z_SCORE")
                {
                    var mean = nonNulls.Average();
                    var variance = nonNulls.Sum(x => (x - mean) * (x - mean)) / nonNulls.Count;
                    var std = Math.Sqrt(variance);
                    if (std > 0)
                    {
                        lowBound = mean - threshold * std;
                        highBound = mean + threshold * std;
                    }
                }
                else
                {
                    lowBound = ComputePercentile(nonNulls, lowerPct / 100.0);
                    highBound = ComputePercentile(nonNulls, upperPct / 100.0);
                }
            }

            foreach (var row in groupRows)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var raw = GetValue(row[valueCol]);
                decimal decVal = 0m;
                bool isNull = !TryGetNumericDecimal(raw, algoName, valueCol, out decVal);
                bool isOutlier = !isNull && lowBound.HasValue && highBound.HasValue
                    && ((double)decVal < lowBound.Value || (double)decVal > highBound.Value);

                if (action == "FILTER" && isOutlier)
                    continue;

                var outRow = new Row(sink.Schema);
                foreach (var col in staged.Columns)
                    outRow[col] = GetValue(row[col]);

                if (action == "FLAG")
                {
                    outRow[flagCol] = isNull ? null : (isOutlier ? 1m : 0m);
                }
                else if (action == "NULL" && isOutlier)
                {
                    outRow[valueCol] = null;
                }
                else if (action == "CLIP" && isOutlier && lowBound.HasValue && highBound.HasValue)
                {
                    var clipped = (double)decVal < lowBound.Value ? lowBound.Value : highBound.Value;
                    outRow[valueCol] = Convert.ToDecimal(Math.Round(clipped, 10), CultureInfo.InvariantCulture);
                }

                await sink.WriteRowAsync(outRow);
            }
        }

        await sink.CompleteAsync();
    }

    private static async Task<double> EvaluateDoubleOption(
        Dictionary<string, Expression> options,
        string key,
        string algorithm,
        double defaultValue,
        IExecutionContext context)
    {
        if (!options.TryGetValue(key, out var expr))
            return defaultValue;

        var raw = GetValue(await context.EvaluationContext.EvaluateValue(expr, Row.Empty));
        if (raw == null)
            throw new ExecutionException($"{algorithm} {key} cannot be NULL.");

        try
        {
            return Convert.ToDouble(raw, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw new ExecutionException($"{algorithm} {key} value '{raw}' must be a number.", ex);
        }
    }

    private static void AddOrUpdateColumnDef(List<ColumnDefinition> defs, string name, string dataType, bool isNullable)
    {
        var idx = defs.FindIndex(c => c.ColumnName.Equals(name, StringComparison.OrdinalIgnoreCase));
        var def = new ColumnDefinition(name, dataType, isIdentity: false) { IsNullable = isNullable };
        if (idx >= 0) defs[idx] = def;
        else defs.Add(def);
    }

    private static bool IsIntegralDataType(string? dataType)
    {
        if (string.IsNullOrWhiteSpace(dataType)) return false;
        var u = dataType.Trim().ToUpperInvariant();
        return u is "INT" or "INTEGER" or "BIGINT" or "SMALLINT" or "TINYINT";
    }

    // Error text quotes at most a short prefix of a bad value: enough to find the row, without copying
    // a long free-text or PII field into logs and run history.
    private static string DescribeValue(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return text.Length <= 24 ? text : $"{text[..24]}... ({text.Length} chars)";
    }

    private static void ValidateNumericColumn(List<Row> rows, string column, string algorithm)
    {
        foreach (var row in rows)
            TryGetNumericDecimal(GetValue(row[column]), algorithm, column, out _);
    }

    // NULL is "no value" (false); any other value must be numeric. A non-numeric value fails the
    // statement like the other algorithms' ToDecimal, instead of silently becoming NULL.
    private static bool TryGetNumericDecimal(object? val, string algorithm, string column, out decimal result)
    {
        result = 0m;
        if (val == null) return false;
        if (val is double dbl && !double.IsFinite(dbl) || val is float flt && !float.IsFinite(flt))
            throw new ExecutionException($"{algorithm} column '{column}' value '{DescribeValue(val)}' is not a finite number.");
        result = ToDecimal(val, algorithm, column);
        return true;
    }

    private static List<decimal> ParseThresholds(string rawSpec, string algoName)
    {
        var parts = rawSpec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            throw new ExecutionException($"{algoName} THRESHOLDS requires at least two comma-separated numeric bounds.");

        var edges = new List<decimal>(parts.Length);
        foreach (var part in parts)
        {
            if (!decimal.TryParse(part, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                throw new ExecutionException($"{algoName} THRESHOLDS contains non-numeric bound '{part}'.");
            if (edges.Count > 0 && val <= edges[^1])
                throw new ExecutionException($"{algoName} THRESHOLDS must be strictly ascending.");
            edges.Add(val);
        }
        return edges;
    }

    private static List<decimal> BuildEqualWidthEdges(decimal min, decimal max, int bins)
    {
        if (min == max) return [min, max];
        var edges = new List<decimal>(bins + 1);
        decimal width = (max - min) / bins;
        for (int i = 0; i < bins; i++)
            edges.Add(min + i * width);
        edges.Add(max);
        return edges;
    }

    private static List<decimal> BuildQuantileEdges(List<decimal> sorted, int bins)
    {
        var doubles = sorted.Select(d => (double)d).ToList();
        var edges = new List<decimal>(bins + 1);
        for (int i = 0; i <= bins; i++)
        {
            var q = ComputePercentile(doubles, (double)i / bins);
            edges.Add(Convert.ToDecimal(Math.Round(q, 10), CultureInfo.InvariantCulture));
        }
        return edges;
    }

    private static double ComputePercentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        if (sorted.Count == 1) return sorted[0];
        var clamped = Math.Clamp(p, 0.0, 1.0);
        var pos = clamped * (sorted.Count - 1);
        int lo = (int)Math.Floor(pos);
        int hi = (int)Math.Ceiling(pos);
        if (lo == hi) return sorted[lo];
        return sorted[lo] + (pos - lo) * (sorted[hi] - sorted[lo]);
    }

    private static (decimal? Start, decimal? End, string Label) AssignBin(decimal value, List<decimal> edges, bool openEnds)
    {
        if (openEnds && value < edges[0])
            return (null, edges[0], $"< {FormatBinEdge(edges[0])}");
        if (openEnds && value > edges[^1])
            return (edges[^1], null, $"> {FormatBinEdge(edges[^1])}");

        int lastBin = edges.Count - 2;
        for (int i = 0; i <= lastBin; i++)
        {
            bool inBin = i == lastBin
                ? value <= edges[i + 1]
                : value < edges[i + 1];
            if (inBin)
            {
                var close = i == lastBin ? "]" : ")";
                return (edges[i], edges[i + 1], $"[{FormatBinEdge(edges[i])}, {FormatBinEdge(edges[i + 1])}{close}");
            }
        }

        return (edges[lastBin], edges[^1], $"[{FormatBinEdge(edges[lastBin])}, {FormatBinEdge(edges[^1])}]");
    }

    private static string FormatBinEdge(decimal d) =>
        (d / 1.00000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);

    private sealed class TransformOutputSink : IAsyncDisposable
    {
        private readonly TableReference _targetTable;
        private readonly IExecutionContext _context;
        private readonly string _algorithm;
        private readonly List<string> _targetColumns;
        private readonly ILogger _logger;
        private readonly IDataSource? _target;
        private DataTable _batch;

        public TableSchema Schema => _batch.Schema;
        public int ProducedRows { get; private set; }

        private TransformOutputSink(
            TableReference targetTable,
            IExecutionContext context,
            string algorithm,
            List<string> targetColumns,
            ILogger logger,
            IDataSource? target,
            DataTable batch)
        {
            _targetTable = targetTable;
            _context = context;
            _algorithm = algorithm;
            _targetColumns = targetColumns;
            _logger = logger;
            _target = target;
            _batch = batch;
        }

        public static Task<TransformOutputSink> CreateAsync(
            TransformStatement stmt,
            IExecutionContext context,
            string algorithm,
            List<ColumnDefinition> targetColumnDefs,
            ILogger logger) =>
            CreateForTableAsync(stmt.TargetTable, stmt, context, algorithm, targetColumnDefs, logger);

        public static async Task<TransformOutputSink> CreateForTableAsync(
            TableReference targetTable,
            TransformStatement stmt,
            IExecutionContext context,
            string algorithm,
            List<ColumnDefinition> targetColumnDefs,
            ILogger logger)
        {
            var targetColumns = targetColumnDefs.Select(c => c.ColumnName).ToList();
            var initialBatch = new DataTable();
            initialBatch.SetColumns(targetColumns);

            if (context.IsWhatIf)
            {
                if (!targetTable.TableName.StartsWith("#", StringComparison.Ordinal))
                {
                    _ = await context.ResolveDataSourceAsync(targetTable)
                        ?? throw new ExecutionException($"Could not resolve target dataset: {targetTable.TableName}");
                }
                return new TransformOutputSink(targetTable, context, algorithm, targetColumns, logger, target: null, initialBatch);
            }

            if (targetTable.TableName.StartsWith("#", StringComparison.Ordinal) &&
                !context.Connections.ContainsKey(targetTable.TableName))
            {
                var created = new InMemoryDataSource
                {
                    Validator = context as IDataValidator,
                    ExecutionContext = context,
                    MaxInMemoryBatches = context.MaxInMemoryBatches
                };
                created.SetSchema(targetColumnDefs);
                context.Connections[targetTable.TableName] = created;
            }

            var target = await context.ResolveDataSourceAsync(targetTable)
                ?? throw new ExecutionException($"Could not resolve target dataset: {targetTable.TableName}");
            await target.TruncateAsync();

            if (target is AppendOnlyColumnDataSource colStore &&
                targetTable.TableName.StartsWith("#", StringComparison.Ordinal))
            {
                if (SchemaDiffers(colStore.LogicalSchema, targetColumnDefs))
                {
                    var replacement = new InMemoryDataSource
                    {
                        Validator = context as IDataValidator,
                        ExecutionContext = context,
                        MaxInMemoryBatches = context.MaxInMemoryBatches
                    };
                    replacement.SetSchema(targetColumnDefs);
                    context.Connections[targetTable.TableName] = replacement;
                    target = replacement;
                }
            }
            else if (target is InMemoryDataSource inMemoryTarget)
            {
                inMemoryTarget.SetSchema(targetColumnDefs);
            }

            return new TransformOutputSink(targetTable, context, algorithm, targetColumns, logger, target, initialBatch);
        }

        private static bool SchemaDiffers(
            IReadOnlyDictionary<string, ColumnDefinition> existingSchema,
            IReadOnlyList<ColumnDefinition> targetColumnDefs)
        {
            if (existingSchema.Count != targetColumnDefs.Count) return true;
            foreach (var col in targetColumnDefs)
            {
                if (!existingSchema.TryGetValue(col.ColumnName, out var existing) ||
                    !string.Equals(existing.DataType, col.DataType, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public async Task WriteRowAsync(Row row)
        {
            _context.CancellationToken.ThrowIfCancellationRequested();
            ProducedRows++;

            if (_context.IsWhatIf) return;

            await _batch.AddRowAsync(row);
            if (_batch.Rows.Count >= _context.EffectiveBatchSize)
            {
                await _target!.WriteBatches(new[] { _batch }.ToAsyncEnumerable(), append: true, _context.CancellationToken);
                _batch = new DataTable();
                _batch.SetColumns(_targetColumns);
            }
        }

        public async Task CompleteAsync()
        {
            if (_context.IsWhatIf)
            {
                _logger.WriteLine(
                    $"WHAT IF: Would write {ProducedRows} rows to {_targetTable.TableName} via TRANSFORM {_algorithm}.",
                    ConsoleColor.Yellow);
                return;
            }

            if (_batch.Rows.Count > 0)
            {
                await _target!.WriteBatches(new[] { _batch }.ToAsyncEnumerable(), append: true, _context.CancellationToken);
                _batch = new DataTable();
                _batch.SetColumns(_targetColumns);
            }

            _context.Telemetry.RowsProcessed += ProducedRows;
            _logger.Info("TRANSFORM {Algorithm} complete: {RowCount} rows staged into {Target}",
                _algorithm, ProducedRows, _targetTable.TableName);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
