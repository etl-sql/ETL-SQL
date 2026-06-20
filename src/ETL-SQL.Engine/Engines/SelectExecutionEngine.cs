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

namespace ETL_SQL.Engine.Engines
{
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

                allRows = await _joinEngine.ApplyJoinsStreaming(joinInput, stmt.Joins, stmt).ToListAsync();

                // Phase 1b (runtime): Drop columns not referenced by any downstream clause.
                // Reduces in-memory working set before GROUP BY / WINDOW / ORDER BY.
                PruneColumns(allRows, logicalPlan.RequiredColumns);
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
                        var combinedStream = PrependRows(bufferedForSpill, ContinueStream(enumerator));
                        // Must materialize inside the try block: ContinueStream captures the enumerator,
                        // which the finally clause disposes immediately after the try exits.
                        allRows = await externalAgg.ApplyAggregationExternal(combinedStream, stmt.GroupBy, finalColumns, colNames, stmt.HavingClause, stmt.GroupingSet).ToListAsync();
                    }
                    else
                    {
                        allRows = await _aggregateEngine.ApplyAggregation(bufferedForSpill.ToAsyncEnumerable(), stmt.GroupBy, finalColumns, colNames, stmt.HavingClause, stmt.GroupingSet);
                    }
                }
                finally { await enumerator.DisposeAsync(); }
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
                    allRows = new List<Row>();
                    if (!whereApplied && stmt.WhereClause != null)
                    {
                        // Apply WHERE during materialization so unmatched rows are never buffered.
                        // This matters most for ORDER BY / DISTINCT queries with selective predicates.
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
                foreach (var r in allRows) if (await _context.EvaluateCondition(stmt.WhereClause, r)) filtered.Add(r);
                allRows = filtered;
            }

            // 2. GROUP BY
            if (!streamAggregate && (stmt.GroupBy != null || stmt.GroupingSet != null || hasAggInColumns))
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
            if (hasWindowInColumns)
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
                await MaterializeEngineStream();
                // Temporarily add aliases to rows so QUALIFY can reference them by alias (e.g., QUALIFY rnk <= 1)
                foreach (var row in allRows)
                {
                    foreach (var col in stmt.Columns)
                    {
                        if (col.Alias != null && WindowEngine.ContainsWindowFunction(col.Expression))
                        {
                            // If the column expression is a window function, find its computed value in the row
                            // and attach it to the alias for the duration of the QUALIFY evaluation.
                            var winCalls = WindowEngine.CollectWindowCalls(col.Expression);
                            if (winCalls.Count == 1)
                            {
                                var winKey = $"WINDOW_{winCalls[0].ToSql().ToUpperInvariant()}";
                                if (row.HasColumn(winKey))
                                {
                                    row[col.Alias] = row[winKey];
                                }
                            }
                        }
                    }
                }

                var filtered = new List<Row>();
                foreach (var r in allRows) if (await _context.EvaluateCondition(stmt.QualifyClause, r)) filtered.Add(r);
                allRows = filtered;
            }

            // 5. ORDER BY
            if (stmt.OrderBy != null && stmt.OrderBy.Count > 0)
            {
                // Phase 7: If an external engine stream is pending and the query has a LIMIT/TOP,
                // run the TopN heap directly on the stream to avoid full materialization.
                if (externalEngineStream != null
                    && !stmt.IsTopPercent && !stmt.WithTies && !stmt.IsDistinct
                    && (stmt.LimitCount != null || stmt.TopCount != null))
                {
                    int limit = Convert.ToInt32(await _context.EvaluateValue(stmt.LimitCount ?? stmt.TopCount!, new Row()));
                    int topOffset = stmt.Offset != null ? Convert.ToInt32(await _context.EvaluateValue(stmt.Offset, new Row())) : 0;
                    allRows = await TopNFromStream(externalEngineStream, stmt.OrderBy, colNames, finalColumns, limit, topOffset, hasPreEvaluatedColumns);
                    externalEngineStream = null;
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
            // Phase 7: Flush any pending external engine stream (e.g. external agg with no ORDER BY).
            await MaterializeEngineStream();
            allRows = await ApplyLimits(allRows, stmt, colNames, finalColumns, hasPreEvaluatedColumns);

            // Final Projection & Batching
            var seenRows = stmt.IsDistinct ? new HashSet<string>() : null;
            var batch = new DataTable();
            batch.SetColumns(colNames);
            bool yielded = false;
            foreach (var row in allRows)
            {
                if (canDeferWhere && !await _context.EvaluateCondition(stmt.WhereClause!, row)) continue;
                var resRow = batch.NewRow();

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
                    if (hasPreEvaluatedColumns && col.Expression is FunctionCallExpression winFce && winFce.Window != null)
                    {
                        var winKey = $"WINDOW_{winFce.ToSql().ToUpperInvariant()}";
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
                    else if (hasPreEvaluatedColumns && row.HasColumn(col.Expression.ToSql()))
                    {
                        resRow[i] = row[col.Expression.ToSql()];
                    }
                    else if (hasPreEvaluatedColumns && row.HasColumn($"AGG_{col.Expression.ToSql().ToUpperInvariant()}"))
                    {
                        resRow[i] = row[$"AGG_{col.Expression.ToSql().ToUpperInvariant()}"];
                    }
                    else
                    {
                        resRow[i] = await _context.EvaluateValue(col.Expression, row);
                    }
                }

                if (seenRows != null)
                {
                    // Fix DISTINCT collapse: Use a unique sentinel for NULL to distinguish it from empty string
                    var key = string.Join("\0", Enumerable.Range(0, colNames.Count).Select(i =>
                    {
                        var val = resRow[i];
                        if (val == null || val == DBNull.Value) return "__NULL__";
                        var s = val.ToString() ?? "";
                        return s == "__NULL__" ? "__[NULL]__" : s; // Escape literal "__NULL__"
                    }));
                    if (!seenRows.Add(key)) continue;
                }

                await batch.AddRowAsync(resRow);
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

        private async Task<List<Row>> SortInMemory(List<Row> rows, List<OrderByClause> orderBy, List<string> colNames, List<SelectColumn> finalColumns, bool hasPreEvaluatedColumns)
        {
            var rowSortKeys = new List<(Row Row, object?[] Keys)>(rows.Count);
            foreach (var row in rows)
                rowSortKeys.Add((row, await ExtractSortKeys(row, orderBy, colNames, finalColumns, hasPreEvaluatedColumns)));

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

            await foreach (var row in source)
            {
                var keys = await ExtractSortKeys(row, orderBy, colNames, finalColumns, hasPreEvaluatedColumns);
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

        private async Task<object?[]> ExtractSortKeys(Row row, List<OrderByClause> orderBy, List<string> colNames, List<SelectColumn> finalColumns, bool hasPreEvaluatedColumns)
        {
            var keys = new object?[orderBy.Count];
            for (int i = 0; i < orderBy.Count; i++)
            {
                var expr = orderBy[i].Expression;
                if (expr is LiteralExpression lit && lit.Type == TokenType.NUMBER
                    && decimal.TryParse(lit.Value?.ToString(), out var num) && num > 0 && num <= colNames.Count)
                {
                    var colIdx = (int)num - 1;
                    var colName = colNames[colIdx];
                    // Use direct lookup when the column is already projected (post-agg/window),
                    // otherwise evaluate the SELECT expression on the pre-projection source row.
                    keys[i] = (hasPreEvaluatedColumns && row.HasColumn(colName))
                        ? row[colName]
                        : await _context.EvaluateValue(finalColumns[colIdx].Expression, row);
                    continue;
                }
                if (expr is IdentifierExpression id && colNames.Contains(id.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if (hasPreEvaluatedColumns && row.HasColumn(id.Name))
                        keys[i] = row[id.Name];
                    else
                    {
                        var colIdx = colNames.FindIndex(c => c.Equals(id.Name, StringComparison.OrdinalIgnoreCase));
                        keys[i] = colIdx >= 0
                            ? await _context.EvaluateValue(finalColumns[colIdx].Expression, row)
                            : null;
                    }
                }
                else if (expr is IdentifierExpression idAlias
                    && finalColumns.FirstOrDefault(c => string.Equals(c.Alias, idAlias.Name, StringComparison.OrdinalIgnoreCase)) is SelectColumn col)
                {
                    keys[i] = await _context.EvaluateValue(col.Expression, row);
                }
                else
                {
                    keys[i] = await _context.EvaluateValue(expr, row);
                }
            }
            return keys;
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
                var lastKeys = await ExtractSortKeys(taken[^1], stmt.OrderBy, colNames, finalColumns, hasPreEvaluatedColumns);
                for (int i = take; i < rows.Count; i++)
                {
                    var keys = await ExtractSortKeys(rows[i], stmt.OrderBy, colNames, finalColumns, hasPreEvaluatedColumns);
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

        private static async IAsyncEnumerable<Row> ContinueStream(IAsyncEnumerator<Row> e)
        {
            while (await e.MoveNextAsync()) yield return e.Current;
        }

        private static async IAsyncEnumerable<Row> WhereStream(IAsyncEnumerable<Row> source, Expression clause, IExecutionContext context)
        {
            await foreach (var r in source) if (await context.EvaluateCondition(clause, r)) yield return r;
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
}
