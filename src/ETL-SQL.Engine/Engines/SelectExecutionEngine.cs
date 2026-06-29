using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Planning;
using ETL_SQL.Data;
using ETL_SQL.Engine.Planning;
using ETL_SQL.Engine.Services;

namespace ETL_SQL.Engine.Engines;
/// <summary>
/// Encapsulates the multi-pass execution pipeline for complex SELECT statements
/// involving JOINS, AGGREGATES, WINDOW FUNCTIONS, and SORTING.
/// </summary>
public class SelectExecutionEngine
{
    private readonly IExecutionContext _context;
    private readonly ILogger _logger;
    private readonly JoinEngine _joinEngine;
    private readonly AggregateEngine _aggregateEngine;
    private readonly WindowEngine _windowEngine;
    private readonly ExternalWindowEngine _externalWindowEngine;

    // Per-query lease into the process-wide memory grant pool. Set for the duration of the
    // pipeline so the spill helpers can force an early spill under global memory pressure.
    private IMemoryGrantLease _memLease = UnlimitedMemoryGrantArbiter.Instance.AcquireLease();

    public SelectExecutionEngine(IExecutionContext context, ILogger logger)
    {
        _context = context;
        _logger = logger;
        _aggregateEngine = new AggregateEngine(context, logger);
        _joinEngine = new JoinEngine(context, logger);
        _windowEngine = new WindowEngine(context, _aggregateEngine, logger);
        _externalWindowEngine = new ExternalWindowEngine(context, _windowEngine, logger);
    }

    public async IAsyncEnumerable<DataTable> ExecuteHeavyPipeline(
        SelectStatement stmt,
        IAsyncEnumerable<DataTable> sourceBatches,
        List<SelectColumn> finalColumns,
        List<string> colNames)
    {
        // Hold a grant-pool lease for the lifetime of the pipeline; disposed when the consumer
        // finishes or abandons enumeration, releasing this query's reserved footprint.
        using var lease = _context.MemoryArbiter.AcquireLease();
        _memLease = lease;
        await foreach (var batch in ExecuteHeavyPipelineCore(stmt, sourceBatches, finalColumns, colNames))
            yield return batch;
    }

    private async IAsyncEnumerable<DataTable> ExecuteHeavyPipelineCore(
        SelectStatement stmt,
        IAsyncEnumerable<DataTable> sourceBatches,
        List<SelectColumn> finalColumns,
        List<string> colNames)
    {
        // Qualify bare identifiers (e.g. col → alias.col) so the predicate optimizer
        // can attribute each predicate to the correct source alias.
        stmt = await IdentifierQualifier.QualifyAsync(stmt, _context);
        stmt = await SemiJoinPushdownOptimizer.OptimizeAsync(stmt, _context);

        // Logical optimizer: classify WHERE predicates by scope and promote eligible
        // CROSS JOIN → INNER JOIN rewrites (subsumes CrossJoinPredicatePushdown).
        var logicalPlan = PredicatePushdownOptimizer.Optimize(stmt);
        stmt = logicalPlan.Statement;

        string fromName = stmt.FromTable.Alias ?? stmt.FromTable.TableName;
        bool hasAggInColumns = stmt.Columns.Any(c => _aggregateEngine.IsAggregate(c.Expression));
        bool hasWindowInColumns = stmt.Columns.Any(c => _windowEngine.IsWindowFunction(c.Expression));
        bool hasGroupBy = stmt.GroupBy != null || stmt.GroupingSet != null;
        bool hasPreEvaluatedColumns = hasAggInColumns || hasWindowInColumns || hasGroupBy;
        bool distinctApplied = false;

        _logger.Debug("[PIPELINE] Initializing Multi-Pass Engine Pipeline for {TableName}", fromName);

        var inputStream = sourceBatches.SelectMany(b => b.Rows.Select(r =>
        {
            var cloned = r.Clone();
            foreach (var colName in r.GetColumnNames())
            {
                // Only qualify if not already qualified
                if (!colName.Contains("."))
                    cloned[$"{fromName}.{colName}"] = r[colName];
            }
            return cloned;
        }).ToAsyncEnumerable());

        List<Row> allRows;
        bool whereApplied = false;
        bool aggregateApplied = false;
        bool windowApplied = false;
        // Phase 7: Tracks a lazy stream from an external engine (aggregate or window).
        // Downstream stages consume it directly without materializing, unless forced by QUALIFY,
        // ORDER BY without LIMIT, or the final projection loop.
        IAsyncEnumerable<Row>? externalEngineStream = null;

        async Task MaterializeEngineStream()
        {
            if (externalEngineStream != null)
            {
                allRows = await externalEngineStream.ToListAsync();
                externalEngineStream = null;
            }
        }

        // Optimization for streaming aggregates
        bool streamAggregate = (stmt.Joins == null || stmt.Joins.Count == 0)
            && !hasWindowInColumns
            && (stmt.GroupBy != null || stmt.GroupingSet != null || hasAggInColumns);

        if (stmt.Joins != null && stmt.Joins.Count > 0)
        {
            // Phase 6: Stream the left side through hash-built right tables (O(right_size) space).
            // Each join's right side is fully buffered into a hash table; the left side (arbitrarily
            // large) streams through without pre-buffering. When a right side exceeds the memory
            // grant, StreamSingleJoin automatically delegates to ExternalJoinEngine for that pair.
            // Intentional materialization: GROUP BY / WINDOW / ORDER BY require random access.

            // Phase 4 (runtime): Apply LeftSingle predicates to the input stream before any hash
            // table is built, reducing the number of rows probed through each right-side table.
            var leftPreds = PredicatePushdownOptimizer.GetSingleSourcePredicates(logicalPlan, fromName)
                .Select(p => p.Predicate).ToList();
            var joinInput = leftPreds.Count > 0
                ? WhereStream(inputStream, CombineAnds(leftPreds), _context)
                : inputStream;

            bool canStreamJoinToProjection =
                !hasPreEvaluatedColumns
                && stmt.QualifyClause == null
                && (stmt.OrderBy == null || stmt.OrderBy.Count == 0)
                && !stmt.IsDistinct
                && !stmt.IsTopPercent;

            if (canStreamJoinToProjection)
            {
                IAsyncEnumerable<Row> joinedStream = _joinEngine.ApplyJoinsStreaming(joinInput, stmt.Joins, stmt);
                if (!whereApplied && stmt.WhereClause != null)
                {
                    joinedStream = WhereStream(joinedStream, stmt.WhereClause, _context);
                    whereApplied = true;
                }

                await foreach (var projectedBatch in ProjectAndBatch(
                    ApplyLimitsStream(joinedStream, stmt),
                    stmt,
                    finalColumns,
                    colNames,
                    hasPreEvaluatedColumns,
                    canDeferWhere: false))
                    yield return projectedBatch;
                yield break;
            }

            IAsyncEnumerable<Row> blockingJoinStream = _joinEngine.ApplyJoinsStreaming(joinInput, stmt.Joins, stmt);
            if (!whereApplied && stmt.WhereClause != null)
            {
                blockingJoinStream = WhereStream(blockingJoinStream, stmt.WhereClause, _context);
                whereApplied = true;
            }

            if (hasGroupBy || hasAggInColumns)
            {
                var bufferedJoinRows = new List<Row>();
                var joinEnumerator = blockingJoinStream.GetAsyncEnumerator();
                bool joinEnumeratorHandedOff = false;
                try
                {
                    long grantBytes = (long)_context.OperatorMemoryGrantMB * 1024 * 1024;
                    while (bufferedJoinRows.Count < _context.JoinSpillThreshold && await joinEnumerator.MoveNextAsync())
                    {
                        bufferedJoinRows.Add(joinEnumerator.Current);
                        if (bufferedJoinRows.Count % 1000 == 0
                            && RowWidthEstimator.EstimateTotalBytes(bufferedJoinRows) > grantBytes)
                            break;
                    }

                    var bufferedBytes = RowWidthEstimator.EstimateTotalBytes(bufferedJoinRows);
                    var needsSpill = bufferedJoinRows.Count >= _context.JoinSpillThreshold
                        || bufferedBytes > grantBytes
                        || _memLease.RegisterAndCheckSpill(bufferedBytes);
                    if (needsSpill)
                    {
                        var externalAggregate = new ExternalAggregateEngine(_context, _logger);
                        var aggregateInput = PrependRows(bufferedJoinRows, ContinueStreamAndDispose(joinEnumerator));
                        joinEnumeratorHandedOff = true;
                        externalEngineStream = externalAggregate.ApplyAggregationExternal(
                            aggregateInput, stmt.GroupBy, finalColumns, colNames, stmt.HavingClause, stmt.GroupingSet);
                        allRows = [];
                    }
                    else
                    {
                        allRows = await _aggregateEngine.ApplyAggregation(
                            bufferedJoinRows.ToAsyncEnumerable(), stmt.GroupBy, finalColumns, colNames, stmt.HavingClause, stmt.GroupingSet);
                    }
                    aggregateApplied = true;
                }
                finally
                {
                    if (!joinEnumeratorHandedOff)
                        await joinEnumerator.DisposeAsync();
                }
            }
            else if (hasWindowInColumns)
            {
                var bufferedJoinRows = new List<Row>();
                var joinEnumerator = blockingJoinStream.GetAsyncEnumerator();
                bool joinEnumeratorHandedOff = false;
                try
                {
                    long grantBytes = (long)_context.OperatorMemoryGrantMB * 1024 * 1024;
                    while (bufferedJoinRows.Count < _context.WindowSpillThreshold && await joinEnumerator.MoveNextAsync())
                    {
                        bufferedJoinRows.Add(joinEnumerator.Current);
                        if (bufferedJoinRows.Count % 1000 == 0
                            && RowWidthEstimator.EstimateTotalBytes(bufferedJoinRows) > grantBytes)
                            break;
                    }

                    var bufferedBytes = RowWidthEstimator.EstimateTotalBytes(bufferedJoinRows);
                    var needsSpill = bufferedJoinRows.Count >= _context.WindowSpillThreshold
                        || bufferedBytes > grantBytes
                        || _memLease.RegisterAndCheckSpill(bufferedBytes);
                    if (needsSpill)
                    {
                        var windowInput = PrependRows(bufferedJoinRows, ContinueStreamAndDispose(joinEnumerator));
                        joinEnumeratorHandedOff = true;
                        externalEngineStream = _externalWindowEngine.ApplyWindowFunctionsExternal(windowInput, stmt);
                        allRows = [];
                        windowApplied = true;
                    }
                    else
                    {
                        allRows = bufferedJoinRows;
                    }
                }
                finally
                {
                    if (!joinEnumeratorHandedOff)
                        await joinEnumerator.DisposeAsync();
                }
            }
            else
            {
                allRows = await blockingJoinStream.ToListAsync();

                // Drop columns not referenced by downstream clauses before blocking stages.
                PruneColumns(allRows, logicalPlan.RequiredColumns);
            }
        }
        else if (streamAggregate)
        {
            IAsyncEnumerable<Row> aggInput = inputStream;
            if (stmt.WhereClause != null)
            {
                aggInput = WhereStream(inputStream, stmt.WhereClause, _context);
                whereApplied = true;
            }

            var bufferedForSpill = new List<Row>();
            var enumerator = aggInput.GetAsyncEnumerator();
            bool enumeratorHandedOff = false;
            try
            {
                int count = 0;
                long aggGrantBytes = (long)_context.OperatorMemoryGrantMB * 1024 * 1024;
                while (count < _context.JoinSpillThreshold && await enumerator.MoveNextAsync())
                {
                    bufferedForSpill.Add(enumerator.Current);
                    count++;
                    // Early spill if byte grant exceeded before the row-count backstop fires.
                    if (count % 1000 == 0 && RowWidthEstimator.EstimateTotalBytes(bufferedForSpill) > aggGrantBytes)
                        break;
                }

                long aggBufferedBytes = RowWidthEstimator.EstimateTotalBytes(bufferedForSpill);
                bool aggNeedsSpill = count >= _context.JoinSpillThreshold
                    || aggBufferedBytes > aggGrantBytes
                    // Global pressure: spill if holding this buffer would breach the process-wide pool.
                    || _memLease.RegisterAndCheckSpill(aggBufferedBytes);
                if (aggNeedsSpill)
                {
                    _logger.Info("[SELECT] Aggregate threshold reached ({Count} rows, grant={GrantMB} MB). Switching to ExternalAggregateEngine.", count, _context.OperatorMemoryGrantMB);
                    var externalAgg = new ExternalAggregateEngine(_context, _logger);
                    var combinedStream = PrependRows(bufferedForSpill, ContinueStreamAndDispose(enumerator));
                    enumeratorHandedOff = true;
                    externalEngineStream = externalAgg.ApplyAggregationExternal(combinedStream, stmt.GroupBy, finalColumns, colNames, stmt.HavingClause, stmt.GroupingSet);
                    allRows = [];
                }
                else
                {
                    allRows = await _aggregateEngine.ApplyAggregation(bufferedForSpill.ToAsyncEnumerable(), stmt.GroupBy, finalColumns, colNames, stmt.HavingClause, stmt.GroupingSet);
                }
            }
            finally
            {
                if (!enumeratorHandedOff)
                    await enumerator.DisposeAsync();
            }
        }
        else
        {
            // Phase 3: Top-N heap — when ORDER BY + LIMIT is present with no blocking
            // aggregate / window / qualify / distinct, stream rows through a size-N heap
            // instead of materializing then full-sorting. O(n log N) time, O(N) space.
            bool canTopN = stmt.OrderBy != null && stmt.OrderBy.Count > 0
                && !stmt.IsTopPercent && !stmt.WithTies && !stmt.IsDistinct
                && stmt.QualifyClause == null && !hasAggInColumns && !hasWindowInColumns
                && (stmt.LimitCount != null || stmt.TopCount != null);

            if (canTopN)
            {
                int limit = Convert.ToInt32(await _context.EvaluateValue(
                    stmt.LimitCount ?? stmt.TopCount!, new Row()));
                int topOffset = stmt.Offset != null
                    ? Convert.ToInt32(await _context.EvaluateValue(stmt.Offset, new Row()))
                    : 0;

                var src = !whereApplied && stmt.WhereClause != null
                    ? WhereStream(inputStream, stmt.WhereClause, _context)
                    : inputStream;
                if (!whereApplied && stmt.WhereClause != null) whereApplied = true;

                allRows = await TopNFromStream(src, stmt.OrderBy!, colNames, finalColumns, limit, topOffset, hasPreEvaluatedColumns);
            }
            else
            {
                var canHybridFullSort = stmt.OrderBy is { Count: > 0 }
                    && !stmt.IsDistinct
                    && stmt.QualifyClause == null && !hasAggInColumns && !hasWindowInColumns
                    && (stmt.LimitCount == null && stmt.TopCount == null || stmt.IsTopPercent || stmt.WithTies);
                if (canHybridFullSort)
                {
                    var sortInput = !whereApplied && stmt.WhereClause != null
                        ? WhereStream(inputStream, stmt.WhereClause, _context)
                        : inputStream;
                    if (!whereApplied && stmt.WhereClause != null) whereApplied = true;

                    var sortPrefix = new List<Row>();
                    var sortEnumerator = sortInput.GetAsyncEnumerator();
                    bool sortEnumeratorHandedOff = false;
                    try
                    {
                        while (sortPrefix.Count < _context.ExternalSortChunkSize && await sortEnumerator.MoveNextAsync())
                            sortPrefix.Add(sortEnumerator.Current);
                        if (sortPrefix.Count >= _context.ExternalSortChunkSize)
                        {
                            externalEngineStream = PrependRows(sortPrefix, ContinueStreamAndDispose(sortEnumerator));
                            sortEnumeratorHandedOff = true;
                            allRows = [];
                        }
                        else
                        {
                            allRows = sortPrefix;
                        }
                    }
                    finally
                    {
                        if (!sortEnumeratorHandedOff)
                            await sortEnumerator.DisposeAsync();
                    }
                }
                else
                {
                    var canStreamDistinctSource = stmt.IsDistinct
                        && stmt.QualifyClause == null && !hasAggInColumns && !hasWindowInColumns;
                    // Window queries: prefix-buffer hybrid (mirrors the sort hybrid above). Small inputs
                    // stay in-memory (the in-memory window engine normalizes partition values); large
                    // inputs stream through the external window engine, which partitions/spills internally
                    // (ProcessBucketDeepSpill streams ranking funcs). This avoids materializing the whole
                    // input into allRows — the dominant memory cost at scale (ROW_NUMBER over one huge
                    // partition was OOMing).
                    bool canHybridWindow = hasWindowInColumns && !hasAggInColumns && stmt.QualifyClause == null;
                    if (canStreamDistinctSource)
                    {
                        externalEngineStream = !whereApplied && stmt.WhereClause != null
                            ? WhereStream(inputStream, stmt.WhereClause, _context)
                            : inputStream;
                        if (!whereApplied && stmt.WhereClause != null) whereApplied = true;
                        allRows = [];
                    }
                    else if (canHybridWindow)
                    {
                        var winInput = !whereApplied && stmt.WhereClause != null
                            ? WhereStream(inputStream, stmt.WhereClause, _context)
                            : inputStream;
                        if (!whereApplied && stmt.WhereClause != null) whereApplied = true;

                        var winPrefix = new List<Row>();
                        var winEnumerator = winInput.GetAsyncEnumerator();
                        bool winHandedOff = false;
                        int winCap = Math.Max(1, _context.WindowSpillThreshold);
                        try
                        {
                            while (winPrefix.Count < winCap && await winEnumerator.MoveNextAsync())
                                winPrefix.Add(winEnumerator.Current);
                            if (winPrefix.Count >= winCap)
                            {
                                externalEngineStream = PrependRows(winPrefix, ContinueStreamAndDispose(winEnumerator));
                                winHandedOff = true;
                                allRows = [];
                            }
                            else
                            {
                                allRows = winPrefix;
                            }
                        }
                        finally
                        {
                            if (!winHandedOff) await winEnumerator.DisposeAsync();
                        }
                    }
                    else
                    {
                        allRows = new List<Row>();
                        if (!whereApplied && stmt.WhereClause != null)
                        {
                            // Apply WHERE during materialization so unmatched rows are never buffered.
                            await foreach (var r in WhereStream(inputStream, stmt.WhereClause, _context))
                                allRows.Add(r);
                            whereApplied = true;
                        }
                        else
                        {
                            await foreach (var r in inputStream) allRows.Add(r);
                        }
                    }
                }
            }
        }

        // 1. WHERE
        // When no post-WHERE stage needs all rows upfront (no GROUP BY, WINDOW, QUALIFY,
        // ORDER BY, LIMIT, or DISTINCT), defer the filter to the projection loop so we avoid
        // allocating a second List<Row> copy of the join output.
        bool canDeferWhere = !whereApplied && stmt.WhereClause != null
            && !(stmt.GroupBy != null || stmt.GroupingSet != null || hasAggInColumns)
            && !hasWindowInColumns
            && stmt.QualifyClause == null
            && (stmt.OrderBy == null || stmt.OrderBy.Count == 0)
            && stmt.Offset == null && stmt.LimitCount == null && stmt.TopCount == null
            && !stmt.IsDistinct;

        if (!whereApplied && !canDeferWhere && stmt.WhereClause != null)
        {
            var filtered = new List<Row>();
            var compiledWhere = RowExpressionCompiler.TryCompilePredicate(_context, stmt.WhereClause, out var wherePredicate)
                ? wherePredicate
                : null;
            foreach (var r in allRows)
            {
                var passesWhere = compiledWhere != null
                    ? compiledWhere(r)
                    : await _context.EvaluateCondition(stmt.WhereClause, r);
                if (passesWhere) filtered.Add(r);
            }
            allRows = filtered;
        }

        // 2. GROUP BY
        if (!streamAggregate && !aggregateApplied && (stmt.GroupBy != null || stmt.GroupingSet != null || hasAggInColumns))
        {
            if (ShouldSpill(allRows))
            {
                var externalAgg = new ExternalAggregateEngine(_context, _logger);
                // Phase 7: Keep as lazy stream; WINDOW can chain directly; QUALIFY or full ORDER BY forces materialization.
                externalEngineStream = externalAgg.ApplyAggregationExternal(allRows.ToAsyncEnumerable(), stmt.GroupBy, finalColumns, colNames, stmt.HavingClause, stmt.GroupingSet);
                allRows = [];
            }
            else
            {
                allRows = await _aggregateEngine.ApplyAggregation(allRows.ToAsyncEnumerable(), stmt.GroupBy, finalColumns, colNames, stmt.HavingClause, stmt.GroupingSet);
            }
        }

        // 3. WINDOW FUNCTIONS
        if (hasWindowInColumns && !windowApplied)
        {
            if (externalEngineStream != null)
            {
                // Phase 7: External agg output streams directly into the window engine —
                // no intermediate materialization. The result stays lazy until QUALIFY or ORDER BY forces it.
                externalEngineStream = _externalWindowEngine.ApplyWindowFunctionsExternal(externalEngineStream, stmt);
            }
            else if (ShouldSpillWindow(allRows))
            {
                _logger.WriteLine($"[yellow]HYPER-SCALE: Switching to ExternalWindowEngine. Row count {allRows.Count} >= threshold {_context.WindowSpillThreshold}. Session: {_context.SessionId}[/]");
                externalEngineStream = _externalWindowEngine.ApplyWindowFunctionsExternal(allRows.ToAsyncEnumerable(), stmt);
                allRows = [];
            }
            else
            {
                allRows = await _windowEngine.ApplyWindowFunctions(allRows, stmt);
            }
        }

        // 4. QUALIFY
        if (stmt.QualifyClause != null)
        {
            if (externalEngineStream != null)
            {
                externalEngineStream = QualifyStream(externalEngineStream, stmt);
            }
            else
            {
                // Temporarily add aliases to rows so QUALIFY can reference them by alias (e.g., QUALIFY rnk <= 1)
                foreach (var row in allRows)
                    AddQualifyAliases(row, stmt.Columns);

                var filtered = new List<Row>();
                var compiledQualify = RowExpressionCompiler.TryCompilePredicate(_context, stmt.QualifyClause, out var qualifyPredicate)
                    ? qualifyPredicate
                    : null;
                foreach (var r in allRows)
                {
                    var passesQualify = compiledQualify != null
                        ? compiledQualify(r)
                        : await _context.EvaluateCondition(stmt.QualifyClause, r);
                    if (passesQualify) filtered.Add(r);
                }
                allRows = filtered;
            }
        }

        // DISTINCT is defined over projected rows and logically precedes ORDER BY.
        if (stmt.IsDistinct)
        {
            var distinctInput = externalEngineStream ?? allRows.ToAsyncEnumerable();
            externalEngineStream = new ExternalDistinctEngine(_context).ApplyAsync(
                ProjectRows(distinctInput, stmt, finalColumns, colNames, hasPreEvaluatedColumns, canDeferWhere));
            allRows = [];
            hasPreEvaluatedColumns = true;
            whereApplied = true;
            canDeferWhere = false;
            distinctApplied = true;
        }

        // 5. ORDER BY
        if (stmt.OrderBy != null && stmt.OrderBy.Count > 0)
        {
            // Phase 7: If an external engine stream is pending and the query has a LIMIT/TOP,
            // run the TopN heap directly on the stream to avoid full materialization.
            if (externalEngineStream != null
                && !stmt.IsTopPercent && !stmt.WithTies
                && (stmt.LimitCount != null || stmt.TopCount != null))
            {
                int limit = Convert.ToInt32(await _context.EvaluateValue(stmt.LimitCount ?? stmt.TopCount!, new Row()));
                int topOffset = stmt.Offset != null ? Convert.ToInt32(await _context.EvaluateValue(stmt.Offset, new Row())) : 0;
                allRows = await TopNFromStream(externalEngineStream, stmt.OrderBy, colNames, finalColumns, limit, topOffset, hasPreEvaluatedColumns);
                externalEngineStream = null;
            }
            else if (externalEngineStream != null)
            {
                var externalSort = new ExternalSortEngine(_context, _logger);
                externalEngineStream = externalSort.SortStreamAsync(externalEngineStream, stmt.OrderBy);
            }
            else
            {
                await MaterializeEngineStream();
                if (ShouldSpill(allRows))
                {
                    var externalSort = new ExternalSortEngine(_context, _logger);
                    allRows = await externalSort.SortExternal(allRows, stmt.OrderBy);
                }
                else
                {
                    allRows = await SortInMemory(allRows, stmt.OrderBy, colNames, finalColumns, hasPreEvaluatedColumns);
                }
            }
        }

        // 6. OFFSET / LIMIT
        // Phase 7: If a spill-backed operator still has a lazy stream, keep it lazy through
        // OFFSET / LIMIT and final projection. Percentage limits count and replay through
        // encrypted spill storage; WITH TIES retains only the boundary sort keys.
        if (externalEngineStream != null)
        {
            int? percentTake = null;
            if (stmt.IsTopPercent)
            {
                var replay = await SpillForReplay(externalEngineStream);
                externalEngineStream = replay.Rows;
                var offset = stmt.Offset != null
                    ? Convert.ToInt32(await _context.EvaluateValue(stmt.Offset, new Row()))
                    : 0;
                var percent = Convert.ToInt32(await _context.EvaluateValue(stmt.TopCount!, new Row()));
                percentTake = (int)Math.Ceiling(Math.Max(0, replay.Count - offset) * percent / 100.0);
            }
            await foreach (var projectedBatch in ProjectAndBatch(
                ApplyLimitsStream(externalEngineStream, stmt, colNames, finalColumns, hasPreEvaluatedColumns, percentTake),
                stmt,
                finalColumns,
                colNames,
                hasPreEvaluatedColumns,
                canDeferWhere,
                distinctApplied))
                yield return projectedBatch;
            yield break;
        }

        // Flush pending external streams only when a downstream semantic needs the full list.
        await MaterializeEngineStream();
        allRows = await ApplyLimits(allRows, stmt, colNames, finalColumns, hasPreEvaluatedColumns);

        // Final Projection & Batching
        await foreach (var projectedBatch in ProjectAndBatch(
            allRows.ToAsyncEnumerable(),
            stmt,
            finalColumns,
            colNames,
            hasPreEvaluatedColumns,
            canDeferWhere,
            distinctApplied))
            yield return projectedBatch;
    }

    private async IAsyncEnumerable<DataTable> ProjectAndBatch(
        IAsyncEnumerable<Row> rows,
        SelectStatement stmt,
        List<SelectColumn> finalColumns,
        List<string> colNames,
        bool hasPreEvaluatedColumns,
        bool canDeferWhere,
        bool alreadyProjected = false)
    {
        var projectedRows = alreadyProjected
            ? rows
            : ProjectRows(rows, stmt, finalColumns, colNames, hasPreEvaluatedColumns, canDeferWhere);
        var batch = new DataTable();
        batch.SetColumns(colNames);
        bool yielded = false;
        await foreach (var row in projectedRows)
        {
            await batch.AddRowAsync(row);
            if (batch.Rows.Count >= _context.BatchSize)
            {
                yield return batch;
                yielded = true;
                batch = new DataTable();
                batch.SetColumns(colNames);
            }
        }
        if (batch.Rows.Count > 0 || !yielded) yield return batch;
    }

    private async IAsyncEnumerable<Row> ProjectRows(
        IAsyncEnumerable<Row> rows,
        SelectStatement stmt,
        List<SelectColumn> finalColumns,
        List<string> colNames,
        bool hasPreEvaluatedColumns,
        bool canDeferWhere)
    {
        var expressionKeys = hasPreEvaluatedColumns
            ? finalColumns.Select(c => c.Expression.ToSql()).ToArray()
            : Array.Empty<string>();
        var aggregateKeys = hasPreEvaluatedColumns
            ? expressionKeys.Select(k => $"AGG_{k.ToUpperInvariant()}").ToArray()
            : Array.Empty<string>();
        var windowKeys = hasPreEvaluatedColumns
            ? finalColumns.Select(c =>
                c.Expression is FunctionCallExpression f && f.Window != null
                    ? $"WINDOW_{c.Expression.ToSql().ToUpperInvariant()}"
                    : null).ToArray()
            : Array.Empty<string?>();
        var projected = new DataTable();
        projected.SetColumns(colNames);
        var deferredWhere = canDeferWhere && RowExpressionCompiler.TryCompilePredicate(_context, stmt.WhereClause, out var compiledDeferredWhere)
            ? compiledDeferredWhere
            : null;
        var compiledColumns = new RowExpressionCompiler.RowValue?[finalColumns.Count];
        for (int i = 0; i < finalColumns.Count; i++)
        {
            if (RowExpressionCompiler.TryCompileValue(_context, finalColumns[i].Expression, out var value))
                compiledColumns[i] = value;
        }
        await foreach (var row in rows)
        {
            if (canDeferWhere)
            {
                var passesWhere = deferredWhere != null
                    ? deferredWhere(row)
                    : await _context.EvaluateCondition(stmt.WhereClause!, row);
                if (!passesWhere) continue;
            }
            var resRow = projected.NewRow();

            bool schemaMatches = hasPreEvaluatedColumns && row.Schema != null && row.Schema.ColumnCount == finalColumns.Count;
            if (schemaMatches)
            {
                for (int k = 0; k < finalColumns.Count; k++)
                {
                    if (!string.Equals(row.Schema!.GetName(k), colNames[k], StringComparison.OrdinalIgnoreCase))
                    {
                        schemaMatches = false;
                        break;
                    }
                }
            }

            for (int i = 0; i < finalColumns.Count; i++)
            {
                var col = finalColumns[i];
                // Window results are stored in the dynamic dict under WINDOW_ keys, not in the schema slot.
                // Check before schemaMatches so GROUP BY + window queries resolve correctly.
                if (hasPreEvaluatedColumns && windowKeys[i] is { } winKey)
                {
                    if (row.HasColumn(winKey)) { resRow[i] = row[winKey]; continue; }
                }

                if (schemaMatches)
                {
                    resRow[i] = row[i];
                }
                // If the column (by alias or exact expression match) is already in the row, use it.
                // This is essential after Aggregation or Window functions.
                else if (hasPreEvaluatedColumns && col.Alias != null && row.HasColumn(col.Alias))
                {
                    resRow[i] = row[col.Alias];
                }
                else if (hasPreEvaluatedColumns && row.HasColumn(expressionKeys[i]))
                {
                    resRow[i] = row[expressionKeys[i]];
                }
                else if (hasPreEvaluatedColumns && row.HasColumn(aggregateKeys[i]))
                {
                    resRow[i] = row[aggregateKeys[i]];
                }
                else
                {
                    resRow[i] = compiledColumns[i] != null
                        ? compiledColumns[i]!(row)
                        : await _context.EvaluateValue(col.Expression, row);
                }
            }

            yield return resRow;
        }
    }

    private async IAsyncEnumerable<Row> ApplyLimitsStream(
        IAsyncEnumerable<Row> rows,
        SelectStatement stmt,
        List<string>? colNames = null,
        List<SelectColumn>? finalColumns = null,
        bool hasPreEvaluatedColumns = false,
        int? takeOverride = null)
    {
        int offset = 0;
        if (stmt.Offset != null)
            offset = Convert.ToInt32(await _context.EvaluateValue(stmt.Offset, new Row()));

        int? take = takeOverride;
        if (!take.HasValue && stmt.TopCount != null)
            take = Convert.ToInt32(await _context.EvaluateValue(stmt.TopCount, new Row()));
        else if (!take.HasValue && stmt.LimitCount != null)
            take = Convert.ToInt32(await _context.EvaluateValue(stmt.LimitCount, new Row()));

        var skipped = 0;
        var yielded = 0;
        object?[]? boundaryKeys = null;
        // Build the WITH TIES sort-key extractor once (was recompiled per tied row via the 5-arg overload).
        SortKeyExtractor? tieExtractor = (stmt.WithTies && stmt.OrderBy != null && colNames != null && finalColumns != null)
            ? BuildSortKeyExtractor(stmt.OrderBy, colNames, finalColumns, hasPreEvaluatedColumns)
            : null;
        await foreach (var row in rows)
        {
            if (skipped < offset)
            {
                skipped++;
                continue;
            }

            if (take.HasValue && yielded >= take.Value)
            {
                if (!stmt.WithTies || stmt.OrderBy == null || boundaryKeys == null
                    || colNames == null || finalColumns == null) yield break;

                var keys = await tieExtractor!.ExtractAsync(row);
                var tied = true;
                for (var i = 0; i < keys.Length; i++)
                    if (_context.CompareConstants(keys[i], boundaryKeys[i]) != 0) { tied = false; break; }
                if (!tied) yield break;
                yield return row;
                continue;
            }

            yielded++;
            if (take.HasValue && yielded == take.Value && stmt.WithTies && stmt.OrderBy != null
                && colNames != null && finalColumns != null)
                boundaryKeys = await tieExtractor!.ExtractAsync(row);
            yield return row;
        }
    }

    private async Task<(IAsyncEnumerable<Row> Rows, long Count)> SpillForReplay(IAsyncEnumerable<Row> rows)
    {
        var name = $"select_replay_{Guid.NewGuid():N}.tmp";
        long count = 0;
        await using (var writer = await _context.SpillStore.CreateWriterAsync(name))
        {
            await foreach (var row in rows)
            {
                await writer.WriteRowAsync(row);
                count++;
            }
        }
        return (ReadReplay(name), count);
    }

    private async IAsyncEnumerable<Row> ReadReplay(string name)
    {
        try
        {
            await using var reader = await _context.SpillStore.CreateReaderAsync(name);
            await foreach (var row in reader.AsEnumerableAsync()) yield return row;
        }
        finally
        {
            _context.SpillStore.DeleteChunk(name);
        }
    }

    private async Task<List<Row>> SortInMemory(List<Row> rows, List<OrderByClause> orderBy, List<string> colNames, List<SelectColumn> finalColumns, bool hasPreEvaluatedColumns)
    {
        var rowSortKeys = new List<(Row Row, object?[] Keys)>(rows.Count);
        var extractor = BuildSortKeyExtractor(orderBy, colNames, finalColumns, hasPreEvaluatedColumns);
        foreach (var row in rows)
            rowSortKeys.Add((row, await extractor.ExtractAsync(row)));

        rowSortKeys.Sort((a, b) =>
        {
            for (int i = 0; i < orderBy.Count; i++)
            {
                var res = _context.CompareConstants(a.Keys[i], b.Keys[i]);
                if (res != 0) return orderBy[i].Descending ? -res : res;
            }
            return 0;
        });
        return rowSortKeys.Select(x => x.Row).ToList();
    }

    /// <summary>
    /// Streams <paramref name="source"/> through a min-heap of size (limit+offset),
    /// returning up to (limit+offset) rows in final output order. O(n log(limit+offset)) time
    /// and O(limit+offset) space — compared to O(n log n) / O(n) for a full sort.
    /// </summary>
    private async Task<List<Row>> TopNFromStream(
        IAsyncEnumerable<Row> source,
        List<OrderByClause> orderBy,
        List<string> colNames,
        List<SelectColumn> finalColumns,
        int limit,
        int offset,
        bool hasPreEvaluatedColumns)
    {
        int keep = Math.Max(0, checked(offset + limit));
        if (keep == 0) return new List<Row>();

        // The heap is a MAX-heap over the output order (heap top = worst kept row).
        // PriorityQueue is a min-heap, so we invert the output compare.
        // heap.Peek() returns the row that would appear LAST among the kept rows.
        var heap = new PriorityQueue<(Row Row, object?[] Keys), (Row Row, object?[] Keys)>(
            Comparer<(Row Row, object?[] Keys)>.Create((a, b) =>
            {
                for (int i = 0; i < orderBy.Count; i++)
                {
                    var res = _context.CompareConstants(a.Keys[i], b.Keys[i]);
                    if (res != 0) return orderBy[i].Descending ? res : -res; // inverted
                }
                return 0;
            }));
        var extractor = BuildSortKeyExtractor(orderBy, colNames, finalColumns, hasPreEvaluatedColumns);

        await foreach (var row in source)
        {
            var keys = await extractor.ExtractAsync(row);
            var entry = (row, keys);

            if (heap.Count < keep)
            {
                heap.Enqueue(entry, entry);
            }
            else
            {
                var peekKeys = heap.Peek().Keys;
                // Check whether the new row is better (appears earlier) than the worst kept.
                bool better = false;
                for (int i = 0; i < orderBy.Count; i++)
                {
                    var res = _context.CompareConstants(keys[i], peekKeys[i]);
                    if (res != 0) { better = orderBy[i].Descending ? res > 0 : res < 0; break; }
                }
                if (better) heap.DequeueEnqueue(entry, entry);
            }
        }

        // Drain in inverted output order (worst-first), then reverse to get correct order.
        var sorted = new List<Row>(heap.Count);
        while (heap.Count > 0) sorted.Add(heap.Dequeue().Row);
        sorted.Reverse();
        return sorted;
    }

    /// <summary>
    /// Builds a reusable sort-key extractor: each ORDER BY expression is resolved ONCE to its column
    /// name / compiled delegate / fallback expression, so per-row extraction does no repeated
    /// column-name scans and no expression recompilation. Build once per sort, then call
    /// <see cref="SortKeyExtractor.ExtractAsync"/> for each row.
    /// </summary>
    private SortKeyExtractor BuildSortKeyExtractor(
        List<OrderByClause> orderBy, List<string> colNames, List<SelectColumn> finalColumns, bool hasPreEvaluatedColumns)
    {
        var compiledOrder = CompileOrderExpressions(orderBy);
        var compiledFinal = CompileFinalColumnExpressions(finalColumns);
        var resolvers = new SortKeyExtractor.Resolver[orderBy.Count];
        for (int i = 0; i < orderBy.Count; i++)
        {
            var expr = orderBy[i].Expression;
            if (expr is LiteralExpression lit && lit.Type == TokenType.NUMBER
                && decimal.TryParse(lit.Value?.ToString(), out var num) && num > 0 && num <= colNames.Count)
            {
                // Positional: direct lookup when already projected (post-agg/window), else evaluate the
                // SELECT expression on the pre-projection source row.
                var colIdx = (int)num - 1;
                resolvers[i] = new SortKeyExtractor.Resolver(
                    hasPreEvaluatedColumns ? colNames[colIdx] : null, compiledFinal[colIdx], finalColumns[colIdx].Expression);
            }
            else if (expr is IdentifierExpression id && colNames.Contains(id.Name, StringComparer.OrdinalIgnoreCase))
            {
                var colIdx = colNames.FindIndex(c => c.Equals(id.Name, StringComparison.OrdinalIgnoreCase));
                resolvers[i] = new SortKeyExtractor.Resolver(
                    hasPreEvaluatedColumns ? id.Name : null,
                    colIdx >= 0 ? compiledFinal[colIdx] : null,
                    colIdx >= 0 ? finalColumns[colIdx].Expression : null);
            }
            else if (expr is IdentifierExpression idAlias
                && finalColumns.FirstOrDefault(c => string.Equals(c.Alias, idAlias.Name, StringComparison.OrdinalIgnoreCase)) is SelectColumn col)
            {
                var colIdx = finalColumns.IndexOf(col);
                resolvers[i] = new SortKeyExtractor.Resolver(
                    null, colIdx >= 0 ? compiledFinal[colIdx] : null, col.Expression);
            }
            else
            {
                resolvers[i] = new SortKeyExtractor.Resolver(null, compiledOrder[i], expr);
            }
        }
        return new SortKeyExtractor(_context, resolvers);
    }

    /// <summary>Per-row ORDER BY key extractor driven by a precomputed plan (see <see cref="BuildSortKeyExtractor"/>).</summary>
    private sealed class SortKeyExtractor
    {
        internal readonly record struct Resolver(
            string? DirectColName, RowExpressionCompiler.RowValue? Compiled, Expression? FallbackExpr);

        private readonly IExecutionContext _ctx;
        private readonly Resolver[] _resolvers;

        public SortKeyExtractor(IExecutionContext ctx, Resolver[] resolvers)
        {
            _ctx = ctx;
            _resolvers = resolvers;
        }

        public async Task<object?[]> ExtractAsync(Row row)
        {
            var keys = new object?[_resolvers.Length];
            for (int i = 0; i < _resolvers.Length; i++)
            {
                var r = _resolvers[i];
                if (r.DirectColName != null && row.HasColumn(r.DirectColName))
                    keys[i] = row[r.DirectColName];
                else if (r.Compiled != null)
                    keys[i] = r.Compiled(row);
                else if (r.FallbackExpr != null)
                    keys[i] = await _ctx.EvaluateValue(r.FallbackExpr, row);
                else
                    keys[i] = null;
            }
            return keys;
        }
    }

    private RowExpressionCompiler.RowValue?[] CompileOrderExpressions(List<OrderByClause> orderBy)
    {
        var compiled = new RowExpressionCompiler.RowValue?[orderBy.Count];
        for (int i = 0; i < orderBy.Count; i++)
        {
            if (RowExpressionCompiler.TryCompileValue(_context, orderBy[i].Expression, out var value))
                compiled[i] = value;
        }
        return compiled;
    }

    private RowExpressionCompiler.RowValue?[] CompileFinalColumnExpressions(List<SelectColumn> finalColumns)
    {
        var compiled = new RowExpressionCompiler.RowValue?[finalColumns.Count];
        for (int i = 0; i < finalColumns.Count; i++)
        {
            if (RowExpressionCompiler.TryCompileValue(_context, finalColumns[i].Expression, out var value))
                compiled[i] = value;
        }
        return compiled;
    }

    private bool ShouldSpill(IReadOnlyList<Row> rows)
    {
        if (rows.Count > _context.JoinSpillThreshold) return true;
        long grantBytes = (long)_context.OperatorMemoryGrantMB * 1024 * 1024;
        long bytes = RowWidthEstimator.EstimateTotalBytes(rows);
        if (bytes > grantBytes) return true;
        // Global pressure: spill if holding this buffer would breach the process-wide pool.
        return _memLease.RegisterAndCheckSpill(bytes);
    }

    private bool ShouldSpillWindow(IReadOnlyList<Row> rows)
    {
        if (rows.Count >= _context.WindowSpillThreshold) return true;
        long grantBytes = (long)_context.OperatorMemoryGrantMB * 1024 * 1024;
        long bytes = RowWidthEstimator.EstimateTotalBytes(rows);
        if (bytes > grantBytes) return true;
        return _memLease.RegisterAndCheckSpill(bytes);
    }

    private async Task<List<Row>> ApplyLimits(List<Row> rows, SelectStatement stmt,
        List<string>? colNames = null, List<SelectColumn>? finalColumns = null, bool hasPreEvaluatedColumns = false)
    {
        if (stmt.Offset != null)
        {
            int offset = Convert.ToInt32(await _context.EvaluateValue(stmt.Offset, new Row()));
            if (offset < 0) throw new ExecutionException("OFFSET must be a non-negative integer.");
            if (offset > 0) rows = rows.Skip(offset).ToList();
        }

        int take = -1;
        if (stmt.TopCount != null)
        {
            take = Convert.ToInt32(await _context.EvaluateValue(stmt.TopCount, new Row()));
            if (stmt.IsTopPercent) take = (int)Math.Ceiling(rows.Count * take / 100.0);
        }
        else if (stmt.LimitCount != null)
        {
            take = Convert.ToInt32(await _context.EvaluateValue(stmt.LimitCount, new Row()));
        }

        if (take < 0) return rows;

        if (stmt.WithTies && stmt.OrderBy != null && colNames != null && finalColumns != null && take < rows.Count)
        {
            var taken = rows.Take(take).ToList();
            var tieExtractor = BuildSortKeyExtractor(stmt.OrderBy, colNames, finalColumns, hasPreEvaluatedColumns);
            var lastKeys = await tieExtractor.ExtractAsync(taken[^1]);
            for (int i = take; i < rows.Count; i++)
            {
                var keys = await tieExtractor.ExtractAsync(rows[i]);
                bool tied = true;
                for (int j = 0; j < stmt.OrderBy.Count; j++)
                    if (_context.CompareConstants(keys[j], lastKeys[j]) != 0) { tied = false; break; }
                if (!tied) break;
                taken.Add(rows[i]);
            }
            return taken;
        }

        return rows.Take(take).ToList();
    }

    private static async IAsyncEnumerable<Row> PrependRows(IEnumerable<Row> buffered, IAsyncEnumerable<Row> remaining)
    {
        foreach (var r in buffered) yield return r;
        await foreach (var r in remaining) yield return r;
    }

    private async IAsyncEnumerable<Row> QualifyStream(IAsyncEnumerable<Row> source, SelectStatement stmt)
    {
        var compiledQualify = RowExpressionCompiler.TryCompilePredicate(_context, stmt.QualifyClause, out var qualifyPredicate)
            ? qualifyPredicate
            : null;
        await foreach (var row in source)
        {
            AddQualifyAliases(row, stmt.Columns);
            var passesQualify = compiledQualify != null
                ? compiledQualify(row)
                : await _context.EvaluateCondition(stmt.QualifyClause!, row);
            if (passesQualify) yield return row;
        }
    }

    private static void AddQualifyAliases(Row row, List<SelectColumn> columns)
    {
        foreach (var col in columns)
        {
            if (col.Alias == null || !WindowEngine.ContainsWindowFunction(col.Expression))
                continue;

            var winCalls = WindowEngine.CollectWindowCalls(col.Expression);
            if (winCalls.Count != 1)
                continue;

            var winKey = $"WINDOW_{winCalls[0].ToSql().ToUpperInvariant()}";
            if (row.HasColumn(winKey))
                row[col.Alias] = row[winKey];
        }
    }

    private static async IAsyncEnumerable<Row> ContinueStreamAndDispose(IAsyncEnumerator<Row> e)
    {
        try
        {
            while (await e.MoveNextAsync()) yield return e.Current;
        }
        finally
        {
            await e.DisposeAsync();
        }
    }

    private static async IAsyncEnumerable<Row> WhereStream(IAsyncEnumerable<Row> source, Expression clause, IExecutionContext context)
    {
        var compiledWhere = RowExpressionCompiler.TryCompilePredicate(context, clause, out var predicate)
            ? predicate
            : null;
        await foreach (var r in source)
        {
            var passesWhere = compiledWhere != null
                ? compiledWhere(r)
                : await context.EvaluateCondition(clause, r);
            if (passesWhere) yield return r;
        }
    }

    private async IAsyncEnumerable<Row> ConvertToAsyncEnumerable(List<Row> rows)
    {
        foreach (var r in rows) yield return r;
        await Task.CompletedTask;
    }

    /// <summary>
    /// Builds a single AND expression from a list of predicates.
    /// </summary>
    private static Expression CombineAnds(List<Expression> preds)
        => preds.Count == 1
            ? preds[0]
            : preds.Skip(1).Aggregate(preds[0],
                (acc, p) => (Expression)new BinaryExpression(acc, TokenType.AND, p));

    /// <summary>
    /// Nulls out column values that are not referenced by any downstream clause
    /// (WHERE / JOIN / GROUP BY / ORDER BY / SELECT projection).
    /// The column slot stays in the schema (Row has no public remove API) but the object
    /// reference is cleared, freeing GC pressure on wide join results.
    /// A null <paramref name="required"/> set (e.g. SELECT *) means pruning is skipped.
    /// </summary>
    private static void PruneColumns(List<Row> rows, HashSet<string>? required)
    {
        if (required == null || rows.Count == 0) return;
        // All rows produced by a join share the same schema — compute the drop list once.
        var toDrop = rows[0].GetColumnNames()
            .Where(k => !RequiredColumnAnalyzer.IsRequired(k, required))
            .ToList();
        if (toDrop.Count == 0) return;
        foreach (var row in rows)
            foreach (var k in toDrop)
                row[k] = null;
    }
}
