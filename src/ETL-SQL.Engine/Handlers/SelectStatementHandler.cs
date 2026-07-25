using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Planning;
using ETL_SQL.Data;
using ETL_SQL.Engine.Engines;
using ETL_SQL.Engine.Services;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the execution of SELECT statements, including CTEs, joins, aggregates, and window functions.
/// Supports both streaming and multi-pass (buffered) execution strategies.
/// </summary>
public class SelectStatementHandler(ILogger logger) : IStatementHandler
{
    private readonly ILogger _logger = logger;
    private readonly CteManager _cteManager = new(logger);
    private readonly PushdownEngine _pushdownEngine = new(logger);
    private readonly ResultProcessor _resultProcessor = new(logger);

    public Type SupportedStatementType => typeof(SelectStatement);



    /// <summary>
    /// Executes a SELECT statement, handling pushdown to remote sources or local evaluation.
    /// </summary>
    public async Task Execute(Statement statement, IExecutionContext context)
    {
        _logger.Info($"[SELECT] Executing SelectStatement. Session: {context.SessionId}");

        // 1. Handle Pushdown (Optimization: Push simple queries to DB)
        // GROUP BY ALL / ORDER BY ALL / star modifiers are resolved locally in EvaluateSelect;
        // skip the early raw-statement pushdown for them.
        if (statement is SelectStatement selPush && selPush.IntoTable == null
            && !selPush.GroupByAll && !selPush.OrderByAll && selPush.Sample == null
            && !selPush.Columns.Any(c => c.Expression is StarExpression))
        {
            if (_pushdownEngine.IsPushdownPossible(selPush, context, out var connName))
            {
                RecordSqlPushdownAccepted(context, "select.sql-pushdown", connName!);
                var result = await _pushdownEngine.ExecutePushdown(selPush, connName!, context);
                context.LastResult = result;
                context.LastResultSets.Add(result);
                context.OnResultSet?.Invoke(result);
                return;
            }
        }

        var intoTable = context.GetIntoTable(statement);
        var forClause = context.GetForClause(statement);

        // 2. Handle SELECT INTO (Extract -> Stage -> Load)
        if (intoTable != null)
        {
            var intoName = intoTable.ConnectionName ?? intoTable.TableName;
            if (context.VarContext.TryGetView(intoName, out _))
                throw new ExecutionException($"View {intoName} is read-only and cannot be used as a SELECT INTO target.");

            var destination = await context.ResolveDataSourceAsync(intoTable);
            await destination.TruncateAsync();

            // Import DB catalog metadata first so source comments inherit onto derived columns.
            await context.EnsureCatalogMetadataImportedAsync(statement.GetSourceTables());
            new LineageManager(context.LineageTracker).RecordSelectIntoLineage(statement, intoTable, context);

            if (statement is SelectStatement nativeSelect
                && await TryNativeSelectInto(nativeSelect, destination, context) is { } nativeRowCount)
            {
                RecordSelectIntoCompletion(intoTable, context, nativeRowCount);
                return;
            }

            IAsyncEnumerable<DataTable> batches;
            if (statement is SelectStatement selectQuery &&
                _pushdownEngine.IsPushdownPossible(selectQuery with { IntoTable = null }, context, out var connName))
            {
                RecordSqlPushdownAccepted(context, "select-into.sql-pushdown", connName!);
                batches = _pushdownEngine.ExecuteStreamingPushdown(selectQuery with { IntoTable = null }, connName!, context);
            }
            else
            {
                if (statement is SelectStatement fallbackSelect)
                    RecordSqlPushdownFallbackIfRemote(context, "select-into.sql-pushdown", fallbackSelect);
                batches = EvaluateQuery(statement, context);
            }

            var targetCols = (await destination.GetColumnsAsync(context.CancellationToken)).ToList();
            if (targetCols.Count > 0) batches = context.AlignColumns(batches, targetCols);
            if (forClause != null) batches = context.EvaluateForClause(batches, forClause);

            var boundBatches = context.InterceptProgress(batches);
            long totalRows = 0;
            async IAsyncEnumerable<DataTable> CountBatches(IAsyncEnumerable<DataTable> source)
            {
                await foreach (var batch in source)
                {
                    totalRows += batch.Rows.Count;
                    yield return batch;
                }
            }

            if (destination is InMemoryDataSource rowStore)
                await rowStore.WriteOwnedBatches(CountBatches(boundBatches), append: true);
            else
                await destination.WriteBatches(CountBatches(boundBatches), append: true);
            RecordSelectIntoCompletion(intoTable, context, totalRows);
        }
        // 3. Handle Standard SELECT (Extract -> Display)
        else
        {
            var batches = EvaluateQuery(statement, context);
            if (forClause != null) batches = context.EvaluateForClause(batches, forClause);
            await _resultProcessor.ProcessStream(batches, context, forClause != null);
        }
    }


    /// <summary>Evaluates a query statement and returns a stream of row batches.</summary>
    public async IAsyncEnumerable<DataTable> EvaluateQuery(Statement query, IExecutionContext context)
    {
        if (query.Ctes != null && query.Ctes.Count > 0) await _cteManager.RegisterCtes(query.Ctes, context);

        if (query is SelectStatement select)
        {
            await foreach (var batch in EvaluateSelect(select, context)) yield return batch;
        }
        else if (query is SetOperationStatement setOp)
        {
            var setEngine = new SetOperationEngine(context, _logger);
            await foreach (var batch in setEngine.ApplySetOperation(setOp)) yield return batch;
        }
    }


    /// <summary>
    /// Evaluates a SELECT statement, choosing between streaming or heavy multi-pass execution.
    /// </summary>
    public async IAsyncEnumerable<DataTable> EvaluateSelect(SelectStatement stmt, IExecutionContext context)
    {
        var aggregateEngine = new AggregateEngine(context, _logger);
        var windowEngine = new WindowEngine(context, aggregateEngine, _logger);
        var metadataHelper = new QueryMetadataHelper(_logger);
        var streamingEngine = new StreamingQueryEngine(context, _logger);

        // 1. Handle Remote Pushdown (delegate to PushdownEngine). ORDER BY ALL, GROUP BY ALL, and star modifiers
        //    (EXCLUDE/REPLACE/RENAME) are resolved locally below, so they are not pushed down.
        bool hasStarModifiers = stmt.Columns.Any(c => c.Expression is StarExpression);
        if (stmt.IntoTable == null && !stmt.OrderByAll && !stmt.GroupByAll && !hasStarModifiers && stmt.Sample == null
            && !HasLateralColumnAlias(stmt.Columns)
            && _pushdownEngine.IsPushdownPossible(stmt, context, out var connName))
        {
            RecordSqlPushdownAccepted(context, "select.stream.sql-pushdown", connName!);
            await foreach (var batch in _pushdownEngine.ExecuteStreamingPushdown(stmt, connName!, context)) yield return batch;
            yield break;
        }

        if (stmt.IntoTable == null)
            RecordSqlPushdownFallbackIfRemote(context, "select.stream.sql-pushdown", stmt);

        if (ColumnarJoinSelectPlan.TryCreate(stmt, out var nativeJoinPlan)
            && await nativeJoinPlan!.TryOpenAsync(context) is { } nativeJoinExecution)
        {
            RecordPlanDecision(context, "select.join", "ColumnarJoin", PlanDecisionOutcome.Accepted,
                PlanDecisionReasonCodes.SemanticGuard, "Columnar join path accepted.");
            await using (nativeJoinExecution)
                await foreach (var batch in nativeJoinExecution.ExecuteAsync()) yield return batch;
            yield break;
        }
        else if (nativeJoinPlan != null)
        {
            RecordPlanDecision(context, "select.join", "ColumnarJoin", PlanDecisionOutcome.Fallback,
                PlanDecisionReasonCodes.ConnectorCapabilityMissing,
                "Columnar join candidate could not open all sources as replayable columnar inputs.",
                ("fallbackDestination", "row-engine"));
        }

        if (ColumnarSortSelectPlan.TryCreate(stmt, out var nativeSortPlan)
            && await nativeSortPlan!.TryOpenAsync(context) is { } nativeSortExecution)
        {
            RecordPlanDecision(context, "select.sort", "ColumnarSort", PlanDecisionOutcome.Accepted,
                PlanDecisionReasonCodes.SemanticGuard, "Columnar sort path accepted.");
            await using (nativeSortExecution)
                await foreach (var batch in nativeSortExecution.ExecuteAsync()) yield return batch;
            yield break;
        }
        else if (nativeSortPlan != null)
        {
            RecordPlanDecision(context, "select.sort", "ColumnarSort", PlanDecisionOutcome.Fallback,
                PlanDecisionReasonCodes.ConnectorCapabilityMissing,
                "Columnar sort candidate could not open the source as replayable columnar input.",
                ("fallbackDestination", "row-engine"));
        }

        IColumnarGroupedAggregatePlan? groupedPlan = null;
        if (ColumnarGroupedAggregatePlan.TryCreate(context, stmt, out var singleGroupedPlan))
            groupedPlan = singleGroupedPlan;
        else if (ColumnarCompositeGroupedAggregatePlan.TryCreate(context, stmt, out var compositeGroupedPlan))
            groupedPlan = compositeGroupedPlan;
        if (groupedPlan == null && IsGroupedColumnarAggregateCandidate(stmt))
        {
            var source = await context.ResolveDataSourceAsync(stmt.FromTable);
            RecordPlanDecision(context, "select.grouped-aggregate", "ColumnarGroupedAggregate",
                PlanDecisionOutcome.Fallback,
                source is IColumnarDataSource
                    ? PlanDecisionReasonCodes.UnsupportedExpression
                    : PlanDecisionReasonCodes.ConnectorCapabilityMissing,
                source is IColumnarDataSource
                    ? "Columnar grouped aggregate planner rejected the statement shape before opening native batches."
                    : "Columnar grouped aggregate candidate source does not expose columnar batches.",
                ("fallbackDestination", "heavy-row-pipeline"));
        }
        else if (groupedPlan != null)
        {
            using (groupedPlan)
            {
                var source = await context.ResolveDataSourceAsync(stmt.FromTable);
                if (source is IColumnarDataSource columnarSource)
                {
                    var sourceColumns = (await source.GetColumnsAsync(context.CancellationToken)).ToList();
                    var (groupedColumns, groupedNames) = await metadataHelper.ExpandColumns(stmt, sourceColumns);
                    var nativeEnumerator = columnarSource.ReadColumnBatches(context.EffectiveBatchSize, context.CancellationToken)
                        .GetAsyncEnumerator(context.CancellationToken);
                    ColumnBatch? firstNative = null;
                    try
                    {
                        if (await nativeEnumerator.MoveNextAsync()) firstNative = nativeEnumerator.Current;
                        if (firstNative == null)
                        {
                            yield return await groupedPlan.FinalizeResultAsync(groupedNames);
                            yield break;
                        }

                        SelectionVector? firstSelection = null;
                        var supported = groupedPlan.CanApply(firstNative)
                            && (stmt.WhereClause == null || ColumnarPredicateCompiler.TrySelect(
                                firstNative, stmt.WhereClause, out firstSelection,
                                cancellationToken: context.CancellationToken,
                                caseSensitiveComparison: context.CaseSensitiveComparison));
                        if (supported)
                        {
                            RecordPlanDecision(context, "select.grouped-aggregate", "ColumnarGroupedAggregate",
                                PlanDecisionOutcome.Accepted, PlanDecisionReasonCodes.SemanticGuard,
                                "Columnar grouped aggregate path accepted.");
                            using (firstNative)
                            using (firstSelection)
                                groupedPlan.Accumulate(firstNative, firstSelection);
                            firstNative = null;
                            while (await nativeEnumerator.MoveNextAsync())
                            {
                                using var nativeBatch = nativeEnumerator.Current;
                                if (!groupedPlan.CanApply(nativeBatch))
                                    throw new InvalidOperationException("Columnar source changed to an incompatible schema during grouped aggregation.");
                                SelectionVector? selection = null;
                                if (stmt.WhereClause != null && !ColumnarPredicateCompiler.TrySelect(
                                    nativeBatch, stmt.WhereClause, out selection,
                                    cancellationToken: context.CancellationToken,
                                    caseSensitiveComparison: context.CaseSensitiveComparison))
                                    throw new InvalidOperationException("Columnar source changed to an incompatible predicate type during grouped aggregation.");
                                using (selection) groupedPlan.Accumulate(nativeBatch, selection);
                            }
                            yield return await groupedPlan.FinalizeResultAsync(groupedNames);
                            yield break;
                        }

                        firstSelection?.Dispose();
                        RecordPlanDecision(context, "select.grouped-aggregate", "ColumnarGroupedAggregate",
                            PlanDecisionOutcome.Fallback,
                            groupedPlan.CanApply(firstNative)
                                ? PlanDecisionReasonCodes.UnsupportedExpression
                                : PlanDecisionReasonCodes.UnsupportedType,
                            "Columnar grouped aggregate candidate replayed through the row pipeline.",
                            ("fallbackDestination", "heavy-row-pipeline"));
                        var rowBatches = ReplayNativeAsRows(firstNative, nativeEnumerator);
                        firstNative = null;
                        var executionEngine = new SelectExecutionEngine(context, _logger);
                        await foreach (var batch in executionEngine.ExecuteHeavyPipeline(
                            stmt, rowBatches, groupedColumns, groupedNames))
                            yield return batch;
                        yield break;
                    }
                    finally
                    {
                        firstNative?.Dispose();
                        await nativeEnumerator.DisposeAsync();
                    }
                }
                else
                {
                    RecordPlanDecision(context, "select.grouped-aggregate", "ColumnarGroupedAggregate",
                        PlanDecisionOutcome.Fallback, PlanDecisionReasonCodes.ConnectorCapabilityMissing,
                        "Columnar grouped aggregate candidate source does not expose columnar batches.",
                        ("fallbackDestination", "row-engine"));
                }
            }
        }

        if (IsValidatedCountCandidate(stmt))
        {
            var source = await context.ResolveDataSourceAsync(stmt.FromTable);
            if (source is IValidatedRowCountDataSource validatedCountSource)
            {
                var count = await validatedCountSource.CountRowsValidatedAsync(context.CancellationToken);
                var result = new DataTable();
                var outputName = stmt.Columns[0].Alias ?? "COUNT(*)";
                result.SetColumns(new[] { outputName });
                var row = new Row();
                // AggregateEngine exposes COUNT using the engine's numeric aggregate type.
                row[outputName] = (decimal)count;
                await result.AddRowAsync(row);
                yield return result;
                yield break;
            }
        }

        var globalAggregateCandidate = IsGlobalColumnarAggregateCandidate(stmt) && HasAggregateProjection(stmt);
        if (globalAggregateCandidate
            && ColumnarAggregatePlan.TryCreate(context, stmt.Columns, out var aggregatePlan))
        {
            var source = await context.ResolveDataSourceAsync(stmt.FromTable);
            if (source is IColumnarDataSource columnarSource)
            {
                var sourceColumns = (await source.GetColumnsAsync(context.CancellationToken)).ToList();
                var (aggregateColumns, aggregateNames) = await metadataHelper.ExpandColumns(stmt, sourceColumns);
                var nativeEnumerator = columnarSource.ReadColumnBatches(context.EffectiveBatchSize, context.CancellationToken)
                    .GetAsyncEnumerator(context.CancellationToken);
                ColumnBatch? firstNative = null;
                try
                {
                    if (await nativeEnumerator.MoveNextAsync()) firstNative = nativeEnumerator.Current;
                    if (firstNative == null)
                    {
                        yield return aggregatePlan!.FinalizeResult(aggregateNames);
                        yield break;
                    }

                    SelectionVector? firstSelection = null;
                    var supported = aggregatePlan!.CanApply(firstNative)
                        && (stmt.WhereClause == null || ColumnarPredicateCompiler.TrySelect(
                            firstNative, stmt.WhereClause, out firstSelection,
                            cancellationToken: context.CancellationToken,
                            caseSensitiveComparison: context.CaseSensitiveComparison));
                    if (supported)
                    {
                        RecordPlanDecision(context, "select.aggregate", "ColumnarAggregate",
                            PlanDecisionOutcome.Accepted, PlanDecisionReasonCodes.SemanticGuard,
                            "Columnar aggregate path accepted.");
                        using (firstNative)
                        using (firstSelection)
                            aggregatePlan.Accumulate(firstNative, firstSelection);
                        firstNative = null;

                        while (await nativeEnumerator.MoveNextAsync())
                        {
                            using var nativeBatch = nativeEnumerator.Current;
                            if (!aggregatePlan.CanApply(nativeBatch))
                                throw new InvalidOperationException("Columnar source changed to an incompatible schema during aggregation.");
                            SelectionVector? selection = null;
                            if (stmt.WhereClause != null && !ColumnarPredicateCompiler.TrySelect(
                                nativeBatch, stmt.WhereClause, out selection,
                                cancellationToken: context.CancellationToken,
                                caseSensitiveComparison: context.CaseSensitiveComparison))
                                throw new InvalidOperationException("Columnar source changed to an incompatible predicate type during aggregation.");
                            using (selection) aggregatePlan.Accumulate(nativeBatch, selection);
                        }
                        yield return aggregatePlan.FinalizeResult(aggregateNames);
                        yield break;
                    }

                    RecordPlanDecision(context, "select.aggregate", "ColumnarAggregate",
                        PlanDecisionOutcome.Fallback,
                        aggregatePlan.CanApply(firstNative)
                            ? PlanDecisionReasonCodes.UnsupportedExpression
                            : PlanDecisionReasonCodes.UnsupportedType,
                        "Columnar aggregate candidate replayed through the row pipeline.",
                        ("fallbackDestination", "heavy-row-pipeline"));
                    var rowBatches = ReplayNativeAsRows(firstNative, nativeEnumerator);
                    firstNative = null;
                    var executionEngine = new SelectExecutionEngine(context, _logger);
                    await foreach (var batch in executionEngine.ExecuteHeavyPipeline(
                        stmt, rowBatches, aggregateColumns, aggregateNames))
                        yield return batch;
                    yield break;
                }
                finally
                {
                    firstNative?.Dispose();
                    await nativeEnumerator.DisposeAsync();
                }
            }
            else
            {
                RecordPlanDecision(context, "select.aggregate", "ColumnarAggregate",
                    PlanDecisionOutcome.Fallback, PlanDecisionReasonCodes.ConnectorCapabilityMissing,
                    "Columnar aggregate candidate source does not expose columnar batches.",
                    ("fallbackDestination", "row-engine"));
            }
        }
        else if (globalAggregateCandidate)
        {
            var source = await context.ResolveDataSourceAsync(stmt.FromTable);
            RecordPlanDecision(context, "select.aggregate", "ColumnarAggregate",
                PlanDecisionOutcome.Fallback,
                source is IColumnarDataSource
                    ? PlanDecisionReasonCodes.UnsupportedExpression
                    : PlanDecisionReasonCodes.ConnectorCapabilityMissing,
                source is IColumnarDataSource
                    ? "Columnar aggregate planner rejected the statement shape before opening native batches."
                    : "Columnar aggregate candidate source does not expose columnar batches.",
                ("fallbackDestination", "heavy-row-pipeline"));
        }

        // Native island for the narrow read-only shape that can preserve semantics today. Complex
        // expressions and unsupported physical types replay through the existing row streaming path.
        if (IsSimpleColumnarCandidate(stmt))
        {
            var source = await context.ResolveDataSourceAsync(stmt.FromTable);
            if (source is IColumnarDataSource columnarSource)
            {
                var sourceColumns = (await source.GetColumnsAsync(context.CancellationToken)).ToList();
                var (nativeColumns, nativeNames) = await metadataHelper.ExpandColumns(stmt, sourceColumns);
                // Open the native source once. Unsupported projection shapes replay the already-read
                // batch through the established row evaluator without restarting the source.
                {
                    var nativeEnumerator = columnarSource.ReadColumnBatches(context.EffectiveBatchSize, context.CancellationToken)
                        .GetAsyncEnumerator(context.CancellationToken);
                    ColumnBatch? firstNative = null;
                    try
                    {
                        if (await nativeEnumerator.MoveNextAsync()) firstNative = nativeEnumerator.Current;
                        if (firstNative == null)
                        {
                            var empty = new DataTable();
                            empty.SetColumns(nativeNames);
                            yield return empty;
                            yield break;
                        }

                        if (!ColumnarProjectionCompiler.CanProject(firstNative, nativeColumns))
                        {
                            RecordPlanDecision(context, "select.projection", "ColumnarProjection",
                                PlanDecisionOutcome.Fallback, PlanDecisionReasonCodes.UnsupportedExpression,
                                "Columnar projection candidate replayed through the row streaming path.",
                                ("fallbackDestination", "row-streaming"));
                            var projectionFallbackBatches = ReplayNativeAsRows(firstNative, nativeEnumerator);
                            firstNative = null;
                            await foreach (var batch in streamingEngine.ExecuteStreamingSelect(
                                stmt, projectionFallbackBatches, nativeColumns, nativeNames))
                                yield return batch;
                            yield break;
                        }

                        SelectionVector? firstSelection = null;
                        var predicateSupported = stmt.WhereClause == null
                            || ColumnarPredicateCompiler.TrySelect(
                                firstNative, stmt.WhereClause, out firstSelection,
                                cancellationToken: context.CancellationToken,
                                caseSensitiveComparison: context.CaseSensitiveComparison);
                        if (predicateSupported)
                        {
                            RecordPlanDecision(context, "select.projection", "ColumnarProjection",
                                PlanDecisionOutcome.Accepted, PlanDecisionReasonCodes.SemanticGuard,
                                "Columnar projection/filter path accepted.");
                            var yieldedRows = false;
                            using (firstNative)
                            using (firstSelection)
                            {
                                var firstResult = ColumnarProjectionCompiler.ProjectToDataTable(
                                    firstNative, nativeColumns, nativeNames, firstSelection,
                                    context.CancellationToken);
                                if (firstResult.Rows.Count > 0)
                                {
                                    yieldedRows = true;
                                    yield return firstResult;
                                }
                            }
                            firstNative = null;

                            while (await nativeEnumerator.MoveNextAsync())
                            {
                                using var nativeBatch = nativeEnumerator.Current;
                                if (!ColumnarProjectionCompiler.CanProject(nativeBatch, nativeColumns))
                                    throw new InvalidOperationException("Columnar source changed to an incompatible projection schema during a query.");
                                SelectionVector? selection = null;
                                if (stmt.WhereClause != null
                                    && !ColumnarPredicateCompiler.TrySelect(
                                        nativeBatch, stmt.WhereClause, out selection,
                                        cancellationToken: context.CancellationToken,
                                        caseSensitiveComparison: context.CaseSensitiveComparison))
                                    throw new InvalidOperationException("Columnar source changed to an incompatible schema during a query.");
                                using (selection)
                                {
                                    var result = ColumnarProjectionCompiler.ProjectToDataTable(
                                        nativeBatch, nativeColumns, nativeNames, selection,
                                        context.CancellationToken);
                                    if (result.Rows.Count > 0)
                                    {
                                        yieldedRows = true;
                                        yield return result;
                                    }
                                }
                            }
                            if (!yieldedRows)
                            {
                                var empty = new DataTable();
                                empty.SetColumns(nativeNames);
                                yield return empty;
                            }
                            yield break;
                        }

                        // The expression shape or physical type is unsupported. Replay the already-read
                        // native batch through the established row evaluator; do not restart the source.
                        RecordPlanDecision(context, "select.projection", "ColumnarProjection",
                            PlanDecisionOutcome.Fallback, PlanDecisionReasonCodes.UnsupportedExpression,
                            "Columnar predicate candidate replayed through the row streaming path.",
                            ("fallbackDestination", "row-streaming"));
                        var rowBatches = ReplayNativeAsRows(firstNative, nativeEnumerator);
                        firstNative = null;
                        await foreach (var batch in streamingEngine.ExecuteStreamingSelect(
                            stmt, rowBatches, nativeColumns, nativeNames))
                            yield return batch;
                        yield break;
                    }
                    finally
                    {
                        firstNative?.Dispose();
                        await nativeEnumerator.DisposeAsync();
                    }
                }
            }
            else
            {
                RecordPlanDecision(context, "select.projection", "ColumnarProjection",
                    PlanDecisionOutcome.Fallback, PlanDecisionReasonCodes.ConnectorCapabilityMissing,
                    "Simple columnar candidate source does not expose columnar batches.",
                    ("fallbackDestination", "row-streaming"));
            }
        }

        // 2. Resolve Source
        _logger.Debug("[SELECT] Evaluating local engine for {TableName}", stmt.FromTable.TableName);
        var batches = context.ResolveAndApplyOperators(stmt.FromTable);

        // 3. Metadata Discovery & Column Expansion
        var enumerator = batches.GetAsyncEnumerator();
        DataTable? firstBatch = null;
        try { if (await enumerator.MoveNextAsync()) firstBatch = enumerator.Current; }
        catch { await enumerator.DisposeAsync(); throw; }

        var effectiveBatches = streamingEngine.ReplayBatches(firstBatch, enumerator);
        var (finalColumns, colNames) = await metadataHelper.ExpandColumns(stmt, firstBatch?.ColumnNames ?? new List<string>());

        // Lateral column aliases: let a SELECT item reference an alias defined earlier in the same
        // list (e.g. SELECT a+b AS total, total*2 AS dt). Resolved by inlining the earlier alias's
        // expression; a real source column always wins over an alias of the same name.
        finalColumns = ApplyLateralColumnAliases(finalColumns, firstBatch?.ColumnNames ?? new List<string>());

        // GROUP BY ALL: now that output columns are fully expanded, group by every non-aggregate, non-window column.
        if (stmt.GroupByAll)
        {
            var groupCols = finalColumns
                .Where(c => !aggregateEngine.IsAggregate(c.Expression) && !windowEngine.IsWindowFunction(c.Expression))
                .Select(c => c.Expression)
                .ToList();
            stmt = stmt with { GroupBy = groupCols, GroupByAll = false };
        }

        // ORDER BY ALL: now that output columns are known, expand to one ORDER BY per column.
        if (stmt.OrderByAll)
        {
            var ob = colNames.Select(n => new OrderByClause(new IdentifierExpression(n), stmt.OrderByAllDescending)).ToList();
            stmt = stmt with { OrderBy = ob, OrderByAll = false };
        }

        // 4. Strategy Selection
        bool hasAgg = finalColumns.Any(c => aggregateEngine.IsAggregate(c.Expression)) || stmt.GroupBy != null;
        bool hasWindow = finalColumns.Any(c => windowEngine.IsWindowFunction(c.Expression));
        bool hasOnlyStreamingRowNumber = hasWindow
            && finalColumns.Where(column => windowEngine.IsWindowFunction(column.Expression))
                .All(column => StreamingQueryEngine.IsStreamingRowNumber(column.Expression));
        bool isComplex = hasAgg || hasWindow && !hasOnlyStreamingRowNumber
            || (stmt.Joins != null && stmt.Joins.Count > 0) || stmt.OrderBy != null
            || !hasOnlyStreamingRowNumber && (stmt.Offset != null || stmt.LimitCount != null)
            || stmt.IsDistinct || stmt.QualifyClause != null
            // UNIQUE data-quality rules need to see the whole stream before any row's fate is
            // known, which the single-pass streaming engine cannot do — route to the pipeline,
            // where the spill-once two-pass scan lives.
            || HasUniqueDataQualityRule(stmt);

        IAsyncEnumerable<DataTable> output;
        if (!isComplex)
        {
            _logger.Debug("[SELECT] Execution Strategy: Fast Streaming");
            output = streamingEngine.ExecuteStreamingSelect(stmt, effectiveBatches, finalColumns, colNames);
        }
        else
        {
            _logger.Debug("[SELECT] Execution Strategy: Multi-Pass Pipeline");
            var executionEngine = new SelectExecutionEngine(context, _logger);
            output = executionEngine.ExecuteHeavyPipeline(stmt, effectiveBatches, finalColumns, colNames);
        }

        if (stmt.Sample != null) output = ApplySample(output, stmt.Sample);
        await foreach (var batch in output) yield return batch;
    }

    private static async Task<long?> TryNativeSelectInto(
        SelectStatement statement,
        IDataSource destination,
        IExecutionContext context)
    {
        if (destination is not IColumnarDataSink sink
            || !IsSimpleColumnarCandidate(statement)
            || statement.Columns.Count == 0)
            return null;

        var source = await context.ResolveDataSourceAsync(statement.FromTable);
        if (ReferenceEquals(source, destination) || source is not IColumnarDataSource columnarSource)
            return null;

        var sourceColumns = (await source.GetColumnsAsync(context.CancellationToken)).ToArray();
        var targetColumns = (await destination.GetColumnsAsync(context.CancellationToken)).ToArray();
        string[] projectedColumns = Array.Empty<string>();
        string[] outputColumns;
        IReadOnlyList<ColumnBatchField>? expressionOutputFields = null;
        if (statement.Columns.Count == 1 && statement.Columns[0].Alias == null
            && statement.Columns[0].Expression is StarExpression star
            && star.Qualifier == null && star.Pattern == null
            && star.Exclude.Count == 0 && star.Replace.Count == 0 && star.Rename.Count == 0)
        {
            projectedColumns = sourceColumns;
            outputColumns = sourceColumns;
        }
        else if (statement.Columns.All(column => column.Expression is IdentifierExpression))
        {
            projectedColumns = statement.Columns
                .Select(column => ((IdentifierExpression)column.Expression).Name.Split('.').Last())
                .ToArray();
            if (projectedColumns.Any(column => !sourceColumns.Contains(column, StringComparer.OrdinalIgnoreCase)))
                return null;
            outputColumns = statement.Columns
                .Select((column, index) => column.Alias ?? projectedColumns[index])
                .ToArray();
        }
        else if (destination is AppendOnlyColumnDataSource appendStore
            && statement.Columns.All(column => column.Expression is IdentifierExpression || column.Alias != null))
        {
            outputColumns = statement.Columns.Select(column => column.Alias
                ?? ((IdentifierExpression)column.Expression).Name.Split('.').Last()).ToArray();
            if (outputColumns.Any(name => !appendStore.LogicalSchema.ContainsKey(name))) return null;
            expressionOutputFields = outputColumns.Select(name =>
            {
                var definition = appendStore.LogicalSchema[name];
                return new ColumnBatchField(
                    name,
                    ColumnBatchAdapter.GetPhysicalType(definition.DataType),
                    definition.DataType,
                    definition.IsNullable);
            }).ToArray();
        }
        else
        {
            return null;
        }

        if (!outputColumns.SequenceEqual(targetColumns, StringComparer.OrdinalIgnoreCase))
            return null;
        var transfersWholeBatch = statement.WhereClause == null
            && projectedColumns.SequenceEqual(sourceColumns, StringComparer.OrdinalIgnoreCase)
            && outputColumns.SequenceEqual(sourceColumns, StringComparer.OrdinalIgnoreCase);

        var enumerator = columnarSource.ReadColumnBatches(context.EffectiveBatchSize, context.CancellationToken)
            .GetAsyncEnumerator(context.CancellationToken);
        if (!await enumerator.MoveNextAsync())
        {
            await enumerator.DisposeAsync();
            return 0;
        }

        var first = enumerator.Current;
        if (expressionOutputFields != null
            && !ColumnarProjectionCompiler.CanProjectToSchema(first, statement.Columns, expressionOutputFields))
        {
            RecordPlanDecision(context, "select-into.columnar", "ColumnarSelectInto",
                PlanDecisionOutcome.Fallback, PlanDecisionReasonCodes.UnsupportedExpression,
                "Columnar SELECT INTO candidate could not project to the destination schema.",
                ("fallbackDestination", "row-select-into"));
            first.Dispose();
            await enumerator.DisposeAsync();
            return null;
        }
        SelectionVector? firstSelection = null;
        if (statement.WhereClause != null && !ColumnarPredicateCompiler.TrySelect(
            first, statement.WhereClause, out firstSelection,
            cancellationToken: context.CancellationToken,
            caseSensitiveComparison: context.CaseSensitiveComparison))
        {
            RecordPlanDecision(context, "select-into.columnar", "ColumnarSelectInto",
                PlanDecisionOutcome.Fallback, PlanDecisionReasonCodes.UnsupportedExpression,
                "Columnar SELECT INTO candidate predicate is unsupported.",
                ("fallbackDestination", "row-select-into"));
            first.Dispose();
            await enumerator.DisposeAsync();
            return null;
        }

        long rowCount = 0;
        var firstPending = true;
        async IAsyncEnumerable<ColumnBatch> CountAndTransfer()
        {
            firstPending = false;
            var input = first;
            var selection = firstSelection;
            while (true)
            {
                ColumnBatch? output = null;
                var ownershipTransferred = false;
                try
                {
                    if (transfersWholeBatch)
                    {
                        output = input;
                        input = null!;
                    }
                    else if (expressionOutputFields != null)
                    {
                        using (selection)
                            output = ColumnarProjectionCompiler.ProjectToColumnBatch(
                                input, statement.Columns, expressionOutputFields, selection,
                                context.CancellationToken);
                        selection = null;
                        input.Dispose();
                        input = null!;
                    }
                    else
                    {
                        using (selection)
                            output = ColumnBatchAdapter.Compact(
                                input, projectedColumns, selection, context.CancellationToken, outputColumns);
                        selection = null;
                        input.Dispose();
                        input = null!;
                    }

                    rowCount += output.RowCount;
                    context.Telemetry.RowsProcessed += output.RowCount;
                    if (context is Evaluator evaluator) evaluator.OnBatchProcessed?.Invoke(output.RowCount);
                    yield return output;
                    ownershipTransferred = true;
                }
                finally
                {
                    if (!ownershipTransferred) output?.Dispose();
                    input?.Dispose();
                    selection?.Dispose();
                }

                if (!await enumerator.MoveNextAsync()) yield break;
                input = enumerator.Current;
                if (expressionOutputFields != null
                    && !ColumnarProjectionCompiler.CanProjectToSchema(input, statement.Columns, expressionOutputFields))
                {
                    input.Dispose();
                    throw new InvalidOperationException("Columnar source changed to an incompatible projection schema during SELECT INTO.");
                }
                if (statement.WhereClause != null && !ColumnarPredicateCompiler.TrySelect(
                    input, statement.WhereClause, out selection,
                    cancellationToken: context.CancellationToken,
                    caseSensitiveComparison: context.CaseSensitiveComparison))
                {
                    input.Dispose();
                    throw new InvalidOperationException("Columnar source changed to an incompatible predicate type during SELECT INTO.");
                }
            }
        }

        try
        {
            await sink.WriteColumnBatches(CountAndTransfer(), append: true, context.CancellationToken);
            RecordPlanDecision(context, "select-into.columnar", "ColumnarSelectInto",
                PlanDecisionOutcome.Accepted, PlanDecisionReasonCodes.SemanticGuard,
                "Columnar SELECT INTO path accepted.");
            return rowCount;
        }
        finally
        {
            if (firstPending) first.Dispose();
            await enumerator.DisposeAsync();
        }
    }

    private static void RecordSelectIntoCompletion(
        TableReference intoTable,
        IExecutionContext context,
        long totalRows)
    {
        context.Variables["@@ROWCOUNT"] = totalRows;
        context.Logger.Info($"{totalRows} rows affected.");

        if (context.InteractiveMode)
            context.OnMessage?.Invoke(new Diagnostic(
                $"{totalRows} rows affected (INTO {intoTable.TableName})",
                0, 0, DiagnosticSeverity.Info));
    }

    private static void RecordPlanDecision(
        IExecutionContext context,
        string operatorId,
        string candidatePath,
        PlanDecisionOutcome outcome,
        string reasonCode,
        string message,
        params (string Key, string Value)[] attributes)
    {
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in attributes)
            facts[key] = value;

        context.Telemetry.RecordPlanDecision(new PlanDecision(
            QueryId: $"select:{context.SessionId}",
            OperatorId: operatorId,
            CandidatePath: candidatePath,
            Outcome: outcome,
            ReasonCode: reasonCode,
            Message: message,
            Attributes: facts));
    }

    private static void RecordSqlPushdownAccepted(
        IExecutionContext context,
        string operatorId,
        string connectionName)
    {
        RecordPlanDecision(context, operatorId, "SqlPushdown", PlanDecisionOutcome.Accepted,
            PlanDecisionReasonCodes.SemanticGuard,
            "SQL pushdown path accepted.",
            ("connectionName", connectionName));
    }

    private static void RecordSqlPushdownFallbackIfRemote(
        IExecutionContext context,
        string operatorId,
        SelectStatement statement)
    {
        var connectionName = statement.FromTable?.ConnectionName;
        if (string.IsNullOrWhiteSpace(connectionName)) return;

        RecordPlanDecision(context, operatorId, "SqlPushdown", PlanDecisionOutcome.Fallback,
            PlanDecisionReasonCodes.ConnectorCapabilityMissing,
            "SQL pushdown candidate was not accepted; query will execute in the engine.",
            ("connectionName", connectionName),
            ("fallbackDestination", "row-engine"));
    }

    /// <summary>
    /// Statements carrying <c>@expect</c> data-quality rules are pinned to the local row pipeline:
    /// the columnar/native fast paths bypass local projection entirely, which is where rules are
    /// enforced and where the pre-projection input row for QUARANTINE capture is available.
    /// Upstream predicate/semi-join pushdown is unaffected — it moves filters, and rules validate
    /// output rows. A regression test guards this pin.
    /// </summary>
    private static bool HasDataQualityRules(SelectStatement stmt)
        => stmt.Columns.Any(c => ETL_SQL.Core.Quality.ColumnRuleParser.HasRuleTags(c.Metadata));

    /// <summary>
    /// True when any column carries a UNIQUE-family rule. Those require the whole-stream pre-pass,
    /// so the statement must run through the multi-pass pipeline rather than the streaming engine.
    /// Malformed rules are ignored here — the linter and the validator both report them.
    /// </summary>
    private static bool HasUniqueDataQualityRule(SelectStatement stmt)
    {
        foreach (var column in stmt.Columns)
        {
            if (!ETL_SQL.Core.Quality.ColumnRuleParser.HasRuleTags(column.Metadata)) continue;
            try
            {
                if (ETL_SQL.Core.Quality.ColumnRuleParser.ParseBindings(column.Metadata!)
                    .Any(b => b.Rules.Any(r => r is ETL_SQL.Core.Quality.UniqueRule)))
                    return true;
            }
            catch (ETL_SQL.Core.Quality.ColumnRuleParseException) { /* reported elsewhere */ }
        }
        return false;
    }

    private static bool IsSimpleColumnarCandidate(SelectStatement stmt)
        => stmt.FromTable.TableOperators.Count == 0
            && (stmt.Joins == null || stmt.Joins.Count == 0)
            && stmt.GroupBy == null && stmt.GroupingSet == null
            && stmt.OrderBy == null && stmt.Offset == null && stmt.LimitCount == null && stmt.TopCount == null
            && !stmt.IsDistinct && stmt.QualifyClause == null && stmt.Sample == null
            && !stmt.IsTopPercent && !stmt.GroupByAll && !stmt.OrderByAll
            && !HasLateralColumnAlias(stmt.Columns)
            && !HasDataQualityRules(stmt);

    private static bool IsGlobalColumnarAggregateCandidate(SelectStatement stmt)
        => stmt.FromTable.TableOperators.Count == 0
            && (stmt.Joins == null || stmt.Joins.Count == 0)
            && stmt.GroupBy == null && stmt.GroupingSet == null && stmt.HavingClause == null
            && stmt.OrderBy == null && stmt.Offset == null && stmt.LimitCount == null && stmt.TopCount == null
            && !stmt.IsDistinct && stmt.QualifyClause == null && stmt.Sample == null
            && !stmt.IsTopPercent && !stmt.GroupByAll && !stmt.OrderByAll
            && !HasDataQualityRules(stmt);

    private static bool IsGroupedColumnarAggregateCandidate(SelectStatement stmt)
        => stmt.FromTable.TableOperators.Count == 0
            && (stmt.Joins == null || stmt.Joins.Count == 0)
            && stmt.GroupBy != null && stmt.GroupingSet == null
            && stmt.OrderBy == null && stmt.Offset == null && stmt.LimitCount == null && stmt.TopCount == null
            && !stmt.IsDistinct && stmt.QualifyClause == null && stmt.Sample == null
            && !stmt.IsTopPercent && !stmt.GroupByAll && !stmt.OrderByAll
            && !HasDataQualityRules(stmt);

    private static bool HasAggregateProjection(SelectStatement stmt)
        => stmt.Columns.Any(column => column.Expression is FunctionCallExpression function
            && function.FunctionName.ToUpperInvariant() is "COUNT" or "SUM" or "AVG" or "MIN" or "MAX");

    private static bool IsValidatedCountCandidate(SelectStatement stmt)
        => IsGlobalColumnarAggregateCandidate(stmt)
            && stmt.WhereClause == null
            && stmt.Columns.Count == 1
            && stmt.Columns[0].Expression is FunctionCallExpression function
            && function.FunctionName.Equals("COUNT", StringComparison.OrdinalIgnoreCase)
            && !function.IsDistinct && function.Window == null && function.Filter == null
            && (function.Arguments.Count == 0
                || function.Arguments.Count == 1 && function.Arguments[0] is StarExpression
                || function.Arguments.Count == 1 && function.Arguments[0] is IdentifierExpression identifier
                    && identifier.Name == "*");

    private static async IAsyncEnumerable<DataTable> ReplayNativeAsRows(
        ColumnBatch first,
        IAsyncEnumerator<ColumnBatch> enumerator)
    {
        using (first) yield return ColumnBatchAdapter.ToDataTable(first);
        while (await enumerator.MoveNextAsync())
        {
            using var batch = enumerator.Current;
            yield return ColumnBatchAdapter.ToDataTable(batch);
        }
    }

    /// <summary>Applies a <c>USING SAMPLE</c> clause: Bernoulli per-row sampling for PERCENT,
    /// reservoir sampling for a fixed ROWS count. A seed makes the result repeatable.</summary>
    private static async IAsyncEnumerable<DataTable> ApplySample(IAsyncEnumerable<DataTable> source, SampleClause sample)
    {
        var rng = sample.Seed.HasValue ? new Random(sample.Seed.Value) : new Random();
        if (sample.IsPercent)
        {
            double p = (double)sample.Count / 100.0;
            await foreach (var batch in source)
            {
                var outB = new DataTable();
                outB.SetColumns(batch.ColumnNames.ToList());
                foreach (var r in batch.Rows) if (rng.NextDouble() < p) await outB.AddRowAsync(r);
                if (outB.Rows.Count > 0) yield return outB;
            }
        }
        else
        {
            int n = (int)sample.Count;
            var reservoir = new List<Row>(Math.Max(0, n));
            List<string>? cols = null;
            int seen = 0;
            await foreach (var batch in source)
            {
                cols ??= batch.ColumnNames.ToList();
                foreach (var r in batch.Rows)
                {
                    seen++;
                    if (reservoir.Count < n) reservoir.Add(r);
                    else { int j = rng.Next(seen); if (j < n) reservoir[j] = r; }
                }
            }
            var outB = new DataTable();
            outB.SetColumns(cols ?? new List<string>());
            foreach (var r in reservoir) await outB.AddRowAsync(r);
            yield return outB;
        }
    }

    /// <summary>
    /// Rewrites the SELECT list so a column may reference an alias defined by an earlier column
    /// (lateral column alias), by inlining the earlier column's (already-inlined) expression. A real
    /// source column always takes precedence over an alias of the same name, so existing queries are
    /// unaffected.
    /// </summary>
    /// <summary>
    /// Conservatively detects whether any SELECT item references an alias defined by an earlier
    /// item (a lateral column alias). Such queries are resolved locally rather than pushed down,
    /// because most remote dialects cannot resolve a SELECT alias laterally. Reuses
    /// <see cref="InlineAliases"/> with an empty source set so detection exactly mirrors inlining
    /// (qualified references are ignored, matching the feature's semantics).
    /// </summary>
    private static bool HasLateralColumnAlias(List<SelectColumn> columns)
    {
        var seen = new Dictionary<string, Expression>(StringComparer.OrdinalIgnoreCase);
        var emptySrc = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in columns)
        {
            if (seen.Count > 0 && !ReferenceEquals(InlineAliases(col.Expression, seen, emptySrc), col.Expression))
                return true;
            if (!string.IsNullOrEmpty(col.Alias) && !seen.ContainsKey(col.Alias))
                seen[col.Alias] = col.Expression;
        }
        return false;
    }

    private static List<SelectColumn> ApplyLateralColumnAliases(List<SelectColumn> columns, List<string> sourceColumns)
    {
        var srcBase = new HashSet<string>(
            sourceColumns.Select(c => c.Contains('.') ? c.Split('.').Last() : c),
            StringComparer.OrdinalIgnoreCase);
        var aliasMap = new Dictionary<string, Expression>(StringComparer.OrdinalIgnoreCase);
        var result = new List<SelectColumn>(columns.Count);
        foreach (var col in columns)
        {
            var rewritten = aliasMap.Count > 0 ? InlineAliases(col.Expression, aliasMap, srcBase) : col.Expression;
            result.Add(ReferenceEquals(rewritten, col.Expression)
                ? col
                : new SelectColumn(rewritten, col.Alias) { Line = col.Line, Column = col.Column, EndLine = col.EndLine, EndColumn = col.EndColumn });
            if (!string.IsNullOrEmpty(col.Alias) && !srcBase.Contains(col.Alias) && !aliasMap.ContainsKey(col.Alias))
                aliasMap[col.Alias] = rewritten;
        }
        return result;
    }

    private static Expression InlineAliases(Expression expr, Dictionary<string, Expression> aliases, HashSet<string> srcBase)
    {
        switch (expr)
        {
            case IdentifierExpression id when !id.Name.Contains('.'):
                return !srcBase.Contains(id.Name) && aliases.TryGetValue(id.Name, out var repl) ? repl : expr;
            case BinaryExpression bin:
                {
                    var l = InlineAliases(bin.Left, aliases, srcBase);
                    var r = InlineAliases(bin.Right, aliases, srcBase);
                    return ReferenceEquals(l, bin.Left) && ReferenceEquals(r, bin.Right) ? expr : new BinaryExpression(l, bin.Operator, r);
                }
            case UnaryExpression un:
                {
                    var inner = InlineAliases(un.Expression, aliases, srcBase);
                    return ReferenceEquals(inner, un.Expression) ? expr : new UnaryExpression(un.Operator, inner);
                }
            case FunctionCallExpression fn:
                {
                    bool changed = false;
                    var newArgs = new List<Expression>(fn.Arguments.Count);
                    foreach (var arg in fn.Arguments) { var q = InlineAliases(arg, aliases, srcBase); newArgs.Add(q); if (!ReferenceEquals(q, arg)) changed = true; }
                    if (!changed) return expr;
                    return new FunctionCallExpression(fn.FunctionName, newArgs)
                    { IsDistinct = fn.IsDistinct, Window = fn.Window, WithinGroupOrderBy = fn.WithinGroupOrderBy, Filter = fn.Filter, JsonTable = fn.JsonTable };
                }
            case InExpression inExpr when inExpr.Subquery == null:
                {
                    var l = InlineAliases(inExpr.Left, aliases, srcBase);
                    var r = InlineAliases(inExpr.Right, aliases, srcBase);
                    return ReferenceEquals(l, inExpr.Left) && ReferenceEquals(r, inExpr.Right) ? expr : new InExpression(l, r, inExpr.IsNot);
                }
            case BetweenExpression bt:
                {
                    var l = InlineAliases(bt.Left, aliases, srcBase);
                    var s = InlineAliases(bt.Start, aliases, srcBase);
                    var e = InlineAliases(bt.End, aliases, srcBase);
                    return ReferenceEquals(l, bt.Left) && ReferenceEquals(s, bt.Start) && ReferenceEquals(e, bt.End) ? expr : new BetweenExpression(l, s, e, bt.IsNot);
                }
            case IsNullExpression isn:
                {
                    var inner = InlineAliases(isn.Expression, aliases, srcBase);
                    return ReferenceEquals(inner, isn.Expression) ? expr : new IsNullExpression(inner, isn.Not);
                }
            case IsDistinctFromExpression idf:
                {
                    var l = InlineAliases(idf.Left, aliases, srcBase);
                    var r = InlineAliases(idf.Right, aliases, srcBase);
                    return ReferenceEquals(l, idf.Left) && ReferenceEquals(r, idf.Right) ? expr : new IsDistinctFromExpression(l, r, idf.Not);
                }
            case LikeExpression like:
                {
                    var l = InlineAliases(like.Left, aliases, srcBase);
                    return ReferenceEquals(l, like.Left) ? expr : new LikeExpression(l, like.Pattern, like.IsNot, like.EscapeChar, like.IsCaseInsensitive);
                }
            case CaseExpression ce:
                {
                    bool changed = false;
                    var whens = new List<(Expression, Expression)>(ce.WhenClauses.Count);
                    foreach (var (c, res) in ce.WhenClauses)
                    {
                        var qc = InlineAliases(c, aliases, srcBase);
                        var qr = InlineAliases(res, aliases, srcBase);
                        whens.Add((qc, qr));
                        if (!ReferenceEquals(qc, c) || !ReferenceEquals(qr, res)) changed = true;
                    }
                    var ni = ce.InputExpression != null ? InlineAliases(ce.InputExpression, aliases, srcBase) : null;
                    var ne = ce.ElseResult != null ? InlineAliases(ce.ElseResult, aliases, srcBase) : null;
                    if (!changed && ReferenceEquals(ni, ce.InputExpression) && ReferenceEquals(ne, ce.ElseResult)) return expr;
                    return new CaseExpression(whens, ne, ni);
                }
            default:
                return expr;
        }
    }
}
