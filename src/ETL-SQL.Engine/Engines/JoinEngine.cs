using ETL_SQL.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Spill;

namespace ETL_SQL.Engine.Engines
{
    /// <summary>
    /// Coordinates and executes join operations (Hash, Merge, Nested Loop, Semi/Anti, and Apply).
    /// </summary>
    public class JoinEngine
    {
        private readonly IExecutionContext _context;
        private readonly ILogger _logger;

        public JoinEngine(IExecutionContext context, ILogger logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>Applies multiple joins to a set of buffered rows, choosing the best algorithm for each join.</summary>
        public async Task<List<Row>> ApplyJoins(List<Row> allBufferedRows, List<JoinClause> joins, SelectStatement stmt)
        {
            if (joins == null || joins.Count == 0) return allBufferedRows;

            // Ensure the initial left rows are qualified with the base table alias.
            // Use ForEachColumn to avoid allocating a Dictionary per row.
            string baseAlias = stmt.FromTable.Alias ?? stmt.FromTable.TableName;
            if (allBufferedRows.Count > 0)
            {
                var toAdd = new List<(string k, object? v)>();
                foreach (var r in allBufferedRows)
                {
                    toAdd.Clear();
                    r.ForEachColumn((k, v) => { if (!k.Contains('.')) toAdd.Add(($"{baseAlias}.{k}", v)); });
                    foreach (var (k, v) in toAdd) r[k] = v;
                }
            }

            // Flatten WHERE predicates for progressive pushdown into CROSS JOINs whose predicates use
            // unqualified column names (which CrossJoinPredicatePushdown cannot handle via GetSourceTables).
            var wherePredicates = new List<Expression>();
            if (stmt.WhereClause != null) FlattenAnds(stmt.WhereClause, wherePredicates);

            // Pre-filter the FROM table before any joins so single-table predicates (e.g., 765=b4)
            // never participate in a Cartesian product at all.
            if (allBufferedRows.Count > 0 && wherePredicates.Count > 0)
                allBufferedRows = await ApplyResolvablePredicates(allBufferedRows, wherePredicates, "INNER JOIN");

            foreach (var join in joins)
            {
                if (join.IsApply)
                {
                    allBufferedRows = await PerformApplyJoin(allBufferedRows, join);
                    if (wherePredicates.Count > 0)
                        allBufferedRows = await ApplyResolvablePredicates(allBufferedRows, wherePredicates, join.JoinType);
                    continue;
                }

                _logger.Debug("Joining table {TableName}{Alias} ({JoinType})", join.Table.TableName, join.Table.Alias != null ? $" AS {join.Table.Alias}" : "", join.JoinType);
                var joinRows = await GetJoinRows(join);

                // Pre-filter the join table using predicates that reference ONLY its own columns —
                // none of the already-accumulated left columns. Eliminates single-table predicates
                // (e.g., d6 IN (...)) before any Cartesian product is formed.
                if (joinRows.Count > 0 && wherePredicates.Count > 0 && allBufferedRows.Count > 0)
                    joinRows = await PreFilterJoinTable(joinRows, wherePredicates, BuildBareColumnSet(allBufferedRows[0]));

                // For CROSS JOINs still carrying a literal-true condition, find WHERE predicates whose
                // referenced columns are all present in the combined left+right row — and use them as
                // the join condition. This prevents O(n^k) Cartesian materialization for queries that
                // use unqualified column names (e.g., WHERE a3=b9 AND c9=688).
                var effectiveJoin = wherePredicates.Count > 0
                    ? TryEnrichCrossJoin(join, allBufferedRows, joinRows, wherePredicates)
                    : join;

                if (effectiveJoin.IsFuzzy)
                {
                    var fuzzyEngine = new FuzzyJoinEngine(_context, _logger);
                    allBufferedRows = await fuzzyEngine.PerformFuzzyJoin(allBufferedRows, joinRows, effectiveJoin);
                    if (wherePredicates.Count > 0)
                        allBufferedRows = await ApplyResolvablePredicates(allBufferedRows, wherePredicates, effectiveJoin.JoinType);
                    continue;
                }
                else if (effectiveJoin.JoinType.Equals("SEMI", StringComparison.OrdinalIgnoreCase))
                {
                    var rightAlias = effectiveJoin.Table.Alias ?? effectiveJoin.Table.TableName;
                    var hashKeysLeft = new List<string>();
                    var hashKeysRight = new List<string>();
                    if (TryExtractEqualityKeys(effectiveJoin.Condition, baseAlias, rightAlias, hashKeysLeft, hashKeysRight))
                        allBufferedRows = PerformHashSemiAntiJoin(allBufferedRows, joinRows, hashKeysLeft, hashKeysRight, semi: true);
                    else
                        allBufferedRows = await PerformSemiJoin(allBufferedRows, joinRows, effectiveJoin);
                }
                else if (effectiveJoin.JoinType.Equals("ANTI", StringComparison.OrdinalIgnoreCase))
                {
                    var rightAlias = effectiveJoin.Table.Alias ?? effectiveJoin.Table.TableName;
                    var hashKeysLeft = new List<string>();
                    var hashKeysRight = new List<string>();
                    if (TryExtractEqualityKeys(effectiveJoin.Condition, baseAlias, rightAlias, hashKeysLeft, hashKeysRight))
                        allBufferedRows = PerformHashSemiAntiJoin(allBufferedRows, joinRows, hashKeysLeft, hashKeysRight, semi: false);
                    else
                        allBufferedRows = await PerformAntiJoin(allBufferedRows, joinRows, effectiveJoin);
                }
                else // INNER, LEFT, RIGHT, FULL
                {
                    var rightAlias = effectiveJoin.Table.Alias ?? effectiveJoin.Table.TableName;
                    var hashKeysLeft = new List<string>();
                    var hashKeysRight = new List<string>();
                    var leftColSet = allBufferedRows.Count > 0 ? BuildBareColumnSet(allBufferedRows[0]) : null;
                    var rightColSet = joinRows.Count > 0 ? BuildBareColumnSet(joinRows[0]) : null;
                    bool hasEquality = TryExtractEqualityKeys(effectiveJoin.Condition, baseAlias, rightAlias, hashKeysLeft, hashKeysRight, leftColSet, rightColSet);

                    // HYPER-SCALE: Check for disk-spilling threshold
                    if (hasEquality && (allBufferedRows.Count > _context.JoinSpillThreshold || joinRows.Count > _context.JoinSpillThreshold))
                    {
                        _logger.WriteLine($"[yellow]HYPER-SCALE: Memory threshold exceeded ({Math.Max(allBufferedRows.Count, joinRows.Count)} rows). Triggering External Disk-Spilling Join.[/]");

                        var externalEngine = new ExternalJoinEngine(_context, _logger);
                        allBufferedRows = await externalEngine.ApplyHashJoinExternal(allBufferedRows.ToAsyncEnumerable(), joinRows.ToAsyncEnumerable(), effectiveJoin, hashKeysLeft, hashKeysRight).ToListAsync();
                    }
                    else
                    {
                        JoinHint algorithm = GetBestAlgorithm(effectiveJoin, allBufferedRows.Count, joinRows.Count, hasEquality);

                        switch (algorithm)
                        {
                            case JoinHint.Hash:
                                allBufferedRows = await PerformHashJoin(allBufferedRows, joinRows, effectiveJoin, hashKeysLeft, hashKeysRight);
                                break;
                            case JoinHint.Merge:
                                allBufferedRows = await PerformMergeJoin(allBufferedRows, joinRows, effectiveJoin, hashKeysLeft, hashKeysRight);
                                break;
                            default:
                                if (allBufferedRows.Count > _context.JoinSpillThreshold)
                                    allBufferedRows = await PerformNestedLoopJoinSpilled(allBufferedRows, joinRows, effectiveJoin);
                                else
                                    allBufferedRows = await PerformNestedLoopJoin(allBufferedRows, joinRows, effectiveJoin);
                                break;
                        }
                    }
                }

                // After each join step, apply any WHERE predicates whose columns are now fully
                // available in the result. This handles non-equality predicates (e.g., IN expressions,
                // LIKE, range checks) that TryEnrichCrossJoin can't convert to join conditions.
                // Filtering early prevents feeding large intermediate sets into subsequent join steps.
                if (wherePredicates.Count > 0)
                    allBufferedRows = await ApplyResolvablePredicates(allBufferedRows, wherePredicates, effectiveJoin.JoinType);
            }
            return allBufferedRows;
        }

        /// <summary>
        /// For a CROSS JOIN with a literal-true condition, finds WHERE predicates whose referenced columns
        /// are all available in the combined left+right row, promotes them to the join condition (converting
        /// the CROSS to an INNER), and removes them from <paramref name="wherePredicates"/> so they are
        /// not re-evaluated by subsequent steps. Falls back to the original join if none are applicable.
        /// </summary>
        private JoinClause TryEnrichCrossJoin(JoinClause join, List<Row> leftRows, List<Row> rightRows, List<Expression> wherePredicates)
        {
            if (join.Condition is not LiteralExpression lit || !true.Equals(lit.Value)) return join;
            if (!string.Equals(join.JoinType, "CROSS JOIN", StringComparison.OrdinalIgnoreCase)) return join;
            if (leftRows.Count == 0 || rightRows.Count == 0) return join;

            var combinedBareCols = BuildBareColumnSet(leftRows[0], rightRows[0]);
            var applicable = new List<Expression>();
            foreach (var p in wherePredicates)
            {
                var cols = p.GetSourceColumns().ToList();
                if (cols.Count > 0 && cols.All(c => combinedBareCols.Contains(c)))
                    applicable.Add(p);
            }

            if (applicable.Count == 0) return join;

            Expression cond = applicable.Count == 1
                ? applicable[0]
                : applicable.Skip(1).Aggregate(applicable[0],
                    (acc, p) => (Expression)new BinaryExpression(acc, TokenType.AND, p));

            _logger.Debug("[JOIN] Progressive WHERE pushdown: {Count} predicate(s) applied to CROSS JOIN on {Table}", applicable.Count, join.Table.TableName ?? "");

            // Remove promoted predicates — they're now the join condition and won't need re-filtering.
            foreach (var p in applicable) wherePredicates.Remove(p);

            return new JoinClause("INNER JOIN", join.Table, cond, join.Hint, join.KeepBest);
        }

        private static HashSet<string> BuildBareColumnSet(Row sample)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            sample.ForEachColumn((k, _) => set.Add(k.Contains('.') ? k[(k.IndexOf('.') + 1)..] : k));
            return set;
        }

        private static HashSet<string> BuildBareColumnSet(Row leftSample, Row rightSample)
        {
            var set = BuildBareColumnSet(leftSample);
            rightSample.ForEachColumn((k, _) => set.Add(k.Contains('.') ? k[(k.IndexOf('.') + 1)..] : k));
            return set;
        }

        /// <summary>
        /// Applies WHERE predicates that are fully resolvable against the current row set,
        /// then removes them from <paramref name="wherePredicates"/> so they are not re-evaluated
        /// in subsequent join steps. Skips outer-join steps to avoid discarding null-extended rows.
        /// </summary>
        private async Task<List<Row>> ApplyResolvablePredicates(List<Row> rows, List<Expression> wherePredicates, string joinType)
        {
            if (rows.Count == 0 || wherePredicates.Count == 0) return rows;

            // Don't pre-filter after outer joins — the null-extended rows are deliberate and the
            // outer WHERE is the correct place to filter them.
            if (IsLeftOuter(joinType) || IsRightOuter(joinType)) return rows;

            var currentCols = BuildBareColumnSet(rows[0]);
            var applicable = new List<Expression>();
            foreach (var p in wherePredicates)
            {
                var cols = p.GetSourceColumns().ToList();
                if (cols.Count > 0 && cols.All(c => currentCols.Contains(c)))
                    applicable.Add(p);
            }
            if (applicable.Count == 0) return rows;

            Expression filter = applicable.Count == 1
                ? applicable[0]
                : applicable.Skip(1).Aggregate(applicable[0],
                    (acc, p) => (Expression)new BinaryExpression(acc, TokenType.AND, p));

            var filtered = new List<Row>(rows.Count);
            foreach (var r in rows)
                if (await _context.EvaluateCondition(filter, r)) filtered.Add(r);

            if (filtered.Count < rows.Count)
                _logger.Debug("[JOIN] Post-join filter: {Predicates} predicate(s) reduced {Before} → {After} rows",
                    applicable.Count, rows.Count, filtered.Count);

            // Remove applied predicates so subsequent join steps don't re-evaluate them.
            foreach (var p in applicable) wherePredicates.Remove(p);

            return filtered;
        }

        /// <summary>
        /// Applies WHERE predicates that reference ONLY columns in the join table — none from the
        /// already-accumulated left side. This pre-filters each join table before it participates
        /// in any Cartesian product, preventing exponential row explosion for comma-join queries
        /// with per-table filter predicates (e.g., IN-lists scoped to a single table).
        /// </summary>
        private async Task<List<Row>> PreFilterJoinTable(
            List<Row> joinRows,
            List<Expression> wherePredicates,
            HashSet<string> leftBareColumns)
        {
            if (joinRows.Count == 0 || wherePredicates.Count == 0) return joinRows;

            var joinBare = BuildBareColumnSet(joinRows[0]);
            var applicable = new List<Expression>();
            foreach (var p in wherePredicates)
            {
                var cols = p.GetSourceColumns().ToList();
                if (cols.Count > 0
                    && cols.All(c => joinBare.Contains(c))
                    && !cols.Any(c => leftBareColumns.Contains(c)))
                    applicable.Add(p);
            }
            if (applicable.Count == 0) return joinRows;

            Expression filter = applicable.Count == 1
                ? applicable[0]
                : applicable.Skip(1).Aggregate(applicable[0],
                    (acc, p) => (Expression)new BinaryExpression(acc, TokenType.AND, p));

            var filtered = new List<Row>(joinRows.Count);
            foreach (var r in joinRows)
                if (await _context.EvaluateCondition(filter, r)) filtered.Add(r);

            if (filtered.Count < joinRows.Count)
                _logger.Debug("[JOIN] Pre-join table filter: {Predicates} predicate(s) reduced {Before} → {After} rows",
                    applicable.Count, joinRows.Count, filtered.Count);

            foreach (var p in applicable) wherePredicates.Remove(p);
            return filtered;
        }

        private static void FlattenAnds(Expression expr, List<Expression> list)
        {
            if (expr is BinaryExpression bin && bin.Operator == TokenType.AND)
            {
                FlattenAnds(bin.Left, list);
                FlattenAnds(bin.Right, list);
            }
            else
            {
                list.Add(expr);
            }
        }

        public async IAsyncEnumerable<Row> ApplyJoinsStreaming(IAsyncEnumerable<Row> leftStream, List<JoinClause> joins, SelectStatement stmt)
        {
            var currentStream = leftStream;
            foreach (var join in joins)
            {
                currentStream = StreamSingleJoin(currentStream, join, stmt);
            }
            await foreach (var row in currentStream)
            {
                yield return row;
            }
        }

        private async IAsyncEnumerable<Row> StreamSingleJoin(IAsyncEnumerable<Row> leftStream, JoinClause join, SelectStatement stmt)
        {
            if (join.IsApply)
            {
                await foreach (var left in leftStream)
                {
                    _context.OuterRowStack.Push(left);
                    try
                    {
                        var rightBatches = _context.ResolveAndApplyOperators(join.Table);
                        bool hasRight = false;
                        await foreach (var rb in rightBatches)
                        {
                            foreach (var rr in rb.Rows)
                            {
                                hasRight = true;
                                yield return CombineRows(left, rr);
                            }
                        }
                        if (!hasRight && join.JoinType.Equals("OUTER APPLY", StringComparison.OrdinalIgnoreCase))
                        {
                            yield return left.Clone();
                        }
                    }
                    finally { _context.OuterRowStack.Pop(); }
                }
                yield break;
            }

            var joinRows = await GetJoinRows(join); // Buffer right side (usually smaller)

            // FUZZY JOIN in streaming context — buffer left, run buffered fuzzy join, re-stream
            if (join.IsFuzzy)
            {
                var leftBuffered = new List<Row>();
                await foreach (var l in leftStream) leftBuffered.Add(l);
                var fuzzyEngine = new FuzzyJoinEngine(_context, _logger);
                var results = await fuzzyEngine.PerformFuzzyJoin(leftBuffered, joinRows, join);
                foreach (var r in results) yield return r;
                yield break;
            }

            var leftAlias = stmt.FromTable.Alias ?? stmt.FromTable.TableName;
            var rightAlias = join.Table.Alias ?? join.Table.TableName;
            var hashKeysLeft = new List<string>();
            var hashKeysRight = new List<string>();
            bool hasEquality = TryExtractEqualityKeys(join.Condition, leftAlias, rightAlias, hashKeysLeft, hashKeysRight);

            JoinHint algorithm = GetBestAlgorithm(join, -1, joinRows.Count, hasEquality); // -1 means unknown (stream)
            
            _logger.Debug("Join Strategy (Streaming): {Algorithm} Join between {LeftAlias} and {RightAlias}", algorithm, leftAlias, rightAlias);

            if (algorithm == JoinHint.Hash)
            {
                await foreach (var r in PerformHashJoinStream(leftStream, joinRows, join, hashKeysLeft, hashKeysRight)) yield return r;
            }
            else if (algorithm == JoinHint.Merge)
            {
                // Merge requires both sides sorted. For simplicity in streaming, we buffer and sort.
                // In a production engine, we'd check if sides are already sorted by an index/ORDER BY.
                var leftBuffered = new List<Row>();
                await foreach (var l in leftStream) leftBuffered.Add(l);
                var results = await PerformMergeJoin(leftBuffered, joinRows, join, hashKeysLeft, hashKeysRight);
                foreach (var r in results) yield return r;
            }
            else
            {
                await foreach (var r in PerformNestedLoopJoinStream(leftStream, joinRows, join)) yield return r;
            }
        }

        private JoinHint GetBestAlgorithm(JoinClause join, int leftCount, int rightCount, bool hasEquality)
        {
            if (join.Hint != JoinHint.None)
            {
                if (join.Hint != JoinHint.Loop && !hasEquality)
                {
                    _logger.WriteLine($"[yellow]WARNING: {join.Hint} JOIN requested but no equality condition found. Falling back to LOOP JOIN.[/]");
                    return JoinHint.Loop;
                }
                return join.Hint;
            }

            if (!hasEquality) return JoinHint.Loop;
            if (rightCount < 20 || (leftCount != -1 && leftCount < 20)) return JoinHint.Loop;
            if (rightCount > _context.JoinSpillThreshold && leftCount > _context.JoinSpillThreshold) return JoinHint.Merge;
            return JoinHint.Hash;

        }

        public async Task<List<Row>> GetJoinRows(JoinClause join)
        {
            var rows = new List<Row>();
            await foreach (var r in GetJoinRowsAsyncEnumerable(join)) rows.Add(r);
            return rows;
        }

        public async IAsyncEnumerable<Row> GetJoinRowsAsyncEnumerable(JoinClause join)
        {
            var joinBatches = _context.ResolveAndApplyOperators(join.Table);
            string joinName = join.Table.Alias ?? join.Table.TableName;
            TableSchema? joinSchema = null;

            await foreach (var jb in joinBatches)
            {
                foreach (var jr in jb.Rows)
                {
                    if (jr.Schema != null)
                    {
                        // Build schema once per join: bare names are canonical, qualified names are
                        // aliases pointing to the same slot. All rows in this join table share this
                        // schema — no per-row duplication of qualified column entries.
                        if (joinSchema == null)
                        {
                            joinSchema = new TableSchema(jr.Schema.ColumnNames);
                            for (int i = 0; i < jr.Schema.ColumnCount; i++)
                                joinSchema.AddAlias($"{joinName}.{jr.Schema.GetName(i)}", i);
                        }

                        var vals = new object?[joinSchema.ColumnCount];
                        for (int i = 0; i < Math.Min(jr.Schema.ColumnCount, vals.Length); i++)
                            vals[i] = jr[jr.Schema.GetName(i)];
                        yield return new Row(joinSchema, vals);
                    }
                    else
                    {
                        // Fallback for schema-less rows: use original clone-and-qualify approach.
                        var r = jr.Clone();
                        jr.ForEachColumn((k, v) => r[$"{joinName}.{k}"] = v);
                        yield return r;
                    }
                }
            }
        }

        private async Task<List<Row>> PerformApplyJoin(List<Row> leftRows, JoinClause join)
        {
            var nextRows = new List<Row>();
            foreach (var left in leftRows)
            {
                _context.OuterRowStack.Push(left);
                try
                {
                    var rightBatches = _context.ResolveAndApplyOperators(join.Table);
                    bool foundMatch = false;
                    await foreach (var rb in rightBatches)
                    {
                        foreach (var rr in rb.Rows)
                        {
                            nextRows.Add(CombineRows(left, rr));
                            foundMatch = true;
                        }
                    }
                    if (!foundMatch && join.JoinType.Contains("OUTER", StringComparison.OrdinalIgnoreCase))
                    {
                        nextRows.Add(left.Clone());
                    }
                }
                finally { _context.OuterRowStack.Pop(); }
            }
            return nextRows;
        }

        private async Task<List<Row>> PerformSemiJoin(List<Row> leftRows, List<Row> rightRows, JoinClause join)
        {
            var nextRows = new List<Row>();
            foreach (var left in leftRows)
            {
                bool foundMatch = false;
                foreach (var right in rightRows)
                {
                    if (await _context.EvaluateCondition(join.Condition, CombineRows(left, right))) { foundMatch = true; break; }
                }
                if (foundMatch) nextRows.Add(left);
            }
            return nextRows;
        }

        private async Task<List<Row>> PerformAntiJoin(List<Row> leftRows, List<Row> rightRows, JoinClause join)
        {
            var nextRows = new List<Row>();
            foreach (var left in leftRows)
            {
                bool foundMatch = false;
                foreach (var right in rightRows)
                {
                    if (await _context.EvaluateCondition(join.Condition, CombineRows(left, right))) { foundMatch = true; break; }
                }
                if (!foundMatch) nextRows.Add(left);
            }
            return nextRows;
        }

        private List<Row> PerformHashSemiAntiJoin(List<Row> leftRows, List<Row> rightRows,
            List<string> leftKeys, List<string> rightKeys, bool semi)
        {
            var rightKeySet = new HashSet<CompoundKey>(rightRows.Select(r => GetHashKey(r, rightKeys)));
            var results = new List<Row>(leftRows.Count);
            foreach (var left in leftRows)
            {
                bool found = rightKeySet.Contains(GetHashKey(left, leftKeys));
                if (semi ? found : !found)
                    results.Add(left);
            }
            return results;
        }

        private async Task<List<Row>> PerformNestedLoopJoin(List<Row> leftRows, List<Row> rightRows, JoinClause join)
        {
            TableSchema? schema = leftRows.Count > 0 && rightRows.Count > 0
                ? BuildCombinedSchema(leftRows[0], rightRows[0]) : null;
            var results = new List<Row>();
            await foreach (var r in PerformNestedLoopJoinStream(leftRows.ToAsyncEnumerable(), rightRows, join, schema)) results.Add(r);
            return results;
        }

        /// <summary>
        /// Block nested-loop join for large left sides: spills left rows to disk and processes them
        /// one page at a time against the in-memory right side. Prevents OOM when no equality keys
        /// are available for hash join but the left side exceeds the spill threshold.
        /// </summary>
        private async Task<List<Row>> PerformNestedLoopJoinSpilled(List<Row> leftRows, List<Row> rightRows, JoinClause join)
        {
            _logger.WriteLine($"[yellow]HYPER-SCALE: Left side ({leftRows.Count} rows) exceeds threshold for nested-loop join. Spilling left side to disk.[/]");

            string spillName = $"{Guid.NewGuid():N}_nl_left.tmp";
            try
            {
                await using (var writer = await _context.SpillStore.CreateWriterAsync(spillName))
                    await writer.WriteRowsAsync(leftRows);

                TableSchema? schema = leftRows.Count > 0 && rightRows.Count > 0
                    ? BuildCombinedSchema(leftRows[0], rightRows[0]) : null;

                var results = new List<Row>();
                var matchedRightIndices = IsRightOuter(join.JoinType) ? new HashSet<int>() : null;

                await using var reader = await _context.SpillStore.CreateReaderAsync(spillName);
                await foreach (var left in reader.AsEnumerableAsync())
                {
                    if (schema == null && rightRows.Count > 0)
                        schema = BuildCombinedSchema(left, rightRows[0]);

                    bool foundMatch = false;
                    for (int ri = 0; ri < rightRows.Count; ri++)
                    {
                        var combined = CombineRows(left, rightRows[ri], schema);
                        if (await _context.EvaluateCondition(join.Condition, combined))
                        {
                            foundMatch = true;
                            if (join.JoinType.Equals("SEMI", StringComparison.OrdinalIgnoreCase) ||
                                join.JoinType.Equals("ANTI", StringComparison.OrdinalIgnoreCase)) break;
                            results.Add(combined);
                            matchedRightIndices?.Add(ri);
                        }
                    }

                    if (join.JoinType.Equals("SEMI", StringComparison.OrdinalIgnoreCase) && foundMatch) results.Add(left.Clone());
                    else if (join.JoinType.Equals("ANTI", StringComparison.OrdinalIgnoreCase) && !foundMatch) results.Add(left.Clone());
                    else if (!foundMatch && IsLeftOuter(join.JoinType)) results.Add(left.Clone());
                }

                if (IsRightOuter(join.JoinType) && matchedRightIndices != null)
                {
                    for (int ri = 0; ri < rightRows.Count; ri++)
                    {
                        if (!matchedRightIndices.Contains(ri))
                            results.Add(schema != null
                                ? CombineRows(new Row(schema), rightRows[ri], schema)
                                : rightRows[ri].Clone());
                    }
                }

                return results;
            }
            finally
            {
                _context.SpillStore.DeleteChunk(spillName);
            }
        }

        private async IAsyncEnumerable<Row> PerformNestedLoopJoinStream(IAsyncEnumerable<Row> leftStream, List<Row> rightRows, JoinClause join, TableSchema? combinedSchema = null)
        {
            var matchedRight = new HashSet<Row>();

            await foreach (var left in leftStream)
            {
                if (combinedSchema == null && rightRows.Count > 0)
                    combinedSchema = BuildCombinedSchema(left, rightRows[0]);

                bool foundMatch = false;
                foreach (var right in rightRows)
                {
                    var combined = CombineRows(left, right, combinedSchema);
                    if (await _context.EvaluateCondition(join.Condition, combined))
                    {
                        foundMatch = true;
                        if (join.JoinType.Equals("SEMI", StringComparison.OrdinalIgnoreCase) || join.JoinType.Equals("ANTI", StringComparison.OrdinalIgnoreCase)) break;
                        
                        yield return combined;
                        matchedRight.Add(right);
                    }
                }
                
                if (join.JoinType.Equals("SEMI", StringComparison.OrdinalIgnoreCase) && foundMatch) yield return left.Clone();
                else if (join.JoinType.Equals("ANTI", StringComparison.OrdinalIgnoreCase) && !foundMatch) yield return left.Clone();
                else if (!foundMatch && IsLeftOuter(join.JoinType)) yield return left.Clone();
            }

            if (IsRightOuter(join.JoinType))
            {
                foreach (var right in rightRows)
                {
                    if (!matchedRight.Contains(right))
                        yield return combinedSchema != null
                            ? CombineRows(new Row(combinedSchema), right, combinedSchema)
                            : right.Clone();
                }
            }
        }

        private async Task<List<Row>> PerformHashJoin(List<Row> leftRows, List<Row> rightRows, JoinClause join, List<string> leftKeys, List<string> rightKeys)
        {
            TableSchema? schema = leftRows.Count > 0 && rightRows.Count > 0
                ? BuildCombinedSchema(leftRows[0], rightRows[0]) : null;
            var results = new List<Row>();
            await foreach (var r in PerformHashJoinStream(leftRows.ToAsyncEnumerable(), rightRows, join, leftKeys, rightKeys, schema)) results.Add(r);
            return results;
        }

        private async IAsyncEnumerable<Row> PerformHashJoinStream(IAsyncEnumerable<Row> leftStream, List<Row> rightRows, JoinClause join, List<string> leftKeys, List<string> rightKeys, TableSchema? combinedSchema = null)
        {
            // Build hash table on the buffered right side
            var hashTable = new Dictionary<CompoundKey, List<Row>>();
            foreach (var r in rightRows)
            {
                var key = GetHashKey(r, rightKeys);
                if (!hashTable.TryGetValue(key, out var list)) { list = new List<Row>(); hashTable[key] = list; }
                list.Add(r);
            }

            var matchedRight = new HashSet<Row>();

            await foreach (var left in leftStream)
            {
                // For the streaming entry-point (called from StreamSingleJoin without a pre-built schema),
                // derive the schema on the first left row so subsequent rows share array-based combined rows.
                if (combinedSchema == null && rightRows.Count > 0)
                    combinedSchema = BuildCombinedSchema(left, rightRows[0]);

                var key = GetHashKey(left, leftKeys);
                bool foundMatch = false;
                if (hashTable.TryGetValue(key, out var matches))
                {
                    foreach (var right in matches)
                    {
                        var combined = CombineRows(left, right, combinedSchema);
                        if (await _context.EvaluateCondition(join.Condition, combined))
                        {
                            foundMatch = true;
                            if (join.JoinType.Equals("SEMI", StringComparison.OrdinalIgnoreCase) || join.JoinType.Equals("ANTI", StringComparison.OrdinalIgnoreCase)) break;

                            yield return combined;
                            matchedRight.Add(right);
                        }
                    }
                }

                if (join.JoinType.Equals("SEMI", StringComparison.OrdinalIgnoreCase) && foundMatch) yield return left.Clone();
                else if (join.JoinType.Equals("ANTI", StringComparison.OrdinalIgnoreCase) && !foundMatch) yield return left.Clone();
                else if (!foundMatch && IsLeftOuter(join.JoinType)) yield return left.Clone();
            }

            if (IsRightOuter(join.JoinType))
            {
                foreach (var right in rightRows)
                {
                    if (!matchedRight.Contains(right))
                        yield return combinedSchema != null
                            ? CombineRows(new Row(combinedSchema), right, combinedSchema)
                            : right.Clone();
                }
            }
        }

        private async Task<List<Row>> PerformMergeJoin(List<Row> leftRows, List<Row> rightRows, JoinClause join, List<string> leftKeys, List<string> rightKeys)
        {
            _logger.Debug("  Performing Merge Join (Sorting {LeftCount} and {RightCount} rows)", leftRows.Count, rightRows.Count);
            var sortedLeft = leftRows.OrderBy(r => GetHashKey(r, leftKeys)).ToList();
            var sortedRight = rightRows.OrderBy(r => GetHashKey(r, rightKeys)).ToList();

            TableSchema? combinedSchema = sortedLeft.Count > 0 && sortedRight.Count > 0
                ? BuildCombinedSchema(sortedLeft[0], sortedRight[0]) : null;

            var nextRows = new List<Row>();
            var matchedLeft = new HashSet<Row>();
            var matchedRight = new HashSet<Row>();

            int i = 0, j = 0;
            while (i < sortedLeft.Count && j < sortedRight.Count)
            {
                var lKey = GetHashKey(sortedLeft[i], leftKeys);
                var rKey = GetHashKey(sortedRight[j], rightKeys);

                int cmp = lKey.CompareTo(rKey);
                if (cmp < 0) i++;
                else if (cmp > 0) j++;
                else
                {
                    int jStart = j;
                    while (i < sortedLeft.Count && GetHashKey(sortedLeft[i], leftKeys).Equals(lKey))
                    {
                        j = jStart;
                        while (j < sortedRight.Count && GetHashKey(sortedRight[j], rightKeys).Equals(rKey))
                        {
                            var combined = CombineRows(sortedLeft[i], sortedRight[j], combinedSchema);
                            if (await _context.EvaluateCondition(join.Condition, combined))
                            {
                                nextRows.Add(combined);
                                matchedLeft.Add(sortedLeft[i]);
                                matchedRight.Add(sortedRight[j]);
                            }
                            j++;
                        }
                        i++;
                    }
                }
            }

            if (IsLeftOuter(join.JoinType))
            {
                foreach (var left in leftRows) if (!matchedLeft.Contains(left)) nextRows.Add(left.Clone());
            }
            if (IsRightOuter(join.JoinType))
            {
                foreach (var right in rightRows)
                    if (!matchedRight.Contains(right))
                        nextRows.Add(combinedSchema != null
                            ? CombineRows(new Row(combinedSchema), right, combinedSchema)
                            : right.Clone());
            }

            return nextRows;
        }

        /// <summary>
        /// Builds a combined schema from the column names of two sample rows.
        /// Used once per join step so all combined rows share the same schema (array-based storage).
        /// When the right side has a bare column name that conflicts with the left, the right's
        /// qualified alias (e.g. c.cat_id) is added as a new canonical slot so that both sides'
        /// values are independently addressable and join conditions like uc.cat_id = c.cat_id
        /// evaluate correctly even when both tables share a bare column name.
        /// </summary>
        private static TableSchema? BuildCombinedSchema(Row leftSample, Row rightSample)
        {
            var cols = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            leftSample.ForEachColumn((k, _) => { if (seen.Add(k)) cols.Add(k); });
            rightSample.ForEachColumn((k, _) =>
            {
                if (seen.Add(k))
                {
                    cols.Add(k);
                }
                else if (rightSample.Schema != null)
                {
                    // Bare name conflicts with the left. Add the right's qualified aliases as
                    // separate canonical slots so both sides' values remain independently addressable.
                    foreach (var alias in rightSample.Schema.EnumerateAliasesOf(k))
                        if (seen.Add(alias)) cols.Add(alias);
                }
            });
            if (cols.Count == 0) return null;

            var schema = new TableSchema(cols);
            // Propagate qualified-name aliases from both sides so lookups like t6.d6
            // resolve correctly in the combined row even when the canonical name is the bare d6.
            leftSample.Schema?.CopyAliasesTo(schema);
            // Right's CopyAliasesTo: for any alias whose canonical was already in the left and
            // whose qualified name is now a separate canonical slot, TryAdd is a no-op (correct).
            rightSample.Schema?.CopyAliasesTo(schema);
            return schema;
        }

        /// <summary>
        /// Merges two rows into one. When <paramref name="schema"/> is provided (built once per join step
        /// via <see cref="BuildCombinedSchema"/>), the result uses array-based storage instead of a
        /// dynamic dictionary, reducing GC pressure by ~5× per combined row.
        /// When a right bare column name conflicts with the left and the combined schema has a separate
        /// canonical slot for the right's qualified alias, the value is written to that slot so the
        /// left's value is not overwritten.
        /// </summary>
        private static Row CombineRows(Row left, Row right, TableSchema? schema = null)
        {
            var combined = schema != null ? new Row(schema) : new Row();
            left.ForEachColumn((k, v) => combined[k] = v);

            if (schema != null && right.Schema != null)
            {
                right.ForEachColumn((k, v) =>
                {
                    // If the bare name maps to a slot already owned by the left, route the right's value
                    // to its qualified alias slot (which BuildCombinedSchema created separately).
                    if (left.HasColumn(k))
                    {
                        int leftSlot = schema.GetIndex(k);
                        string? rightSlot = right.Schema.EnumerateAliasesOf(k)
                            .FirstOrDefault(a => schema.GetIndex(a) != leftSlot && schema.GetIndex(a) >= 0);
                        if (rightSlot != null)
                        { combined[rightSlot] = v; return; }
                    }
                    combined[k] = v;
                });
            }
            else
            {
                right.ForEachColumn((k, v) => combined[k] = v);
            }

            return combined;
        }

        private bool IsLeftOuter(string type) => type.Contains("LEFT", StringComparison.OrdinalIgnoreCase) || type.Contains("FULL", StringComparison.OrdinalIgnoreCase) || type.Contains("OUTER", StringComparison.OrdinalIgnoreCase);
        private bool IsRightOuter(string type) => type.Contains("RIGHT", StringComparison.OrdinalIgnoreCase) || type.Contains("FULL", StringComparison.OrdinalIgnoreCase);

        public bool TryExtractEqualityKeys(
            Expression? cond,
            string leftAlias,
            string rightAlias,
            List<string> leftKeys,
            List<string> rightKeys,
            IReadOnlySet<string>? leftCols = null,
            IReadOnlySet<string>? rightCols = null)
        {
            if (cond is BinaryExpression bin)
            {
                if (bin.Operator == TokenType.EQUALS)
                {
                    if (bin.Left is IdentifierExpression lid && bin.Right is IdentifierExpression rid)
                    {
                        // Exact match: left is leftAlias, right is rightAlias
                        if (lid.Name.StartsWith(leftAlias + ".", StringComparison.OrdinalIgnoreCase) && rid.Name.StartsWith(rightAlias + ".", StringComparison.OrdinalIgnoreCase))
                        {
                            leftKeys.Add(lid.Name); rightKeys.Add(rid.Name);
                            return true;
                        }
                        if (rid.Name.StartsWith(leftAlias + ".", StringComparison.OrdinalIgnoreCase) && lid.Name.StartsWith(rightAlias + ".", StringComparison.OrdinalIgnoreCase))
                        {
                            leftKeys.Add(rid.Name); rightKeys.Add(lid.Name);
                            return true;
                        }
                        // Fallback for multi-join: the left key may be from any accumulated table,
                        // not just the original FROM table. One side must be rightAlias; the other
                        // must be qualified (contains ".") but not belong to rightAlias.
                        if (rid.Name.StartsWith(rightAlias + ".", StringComparison.OrdinalIgnoreCase)
                            && lid.Name.Contains('.') && !lid.Name.StartsWith(rightAlias + ".", StringComparison.OrdinalIgnoreCase))
                        {
                            leftKeys.Add(lid.Name); rightKeys.Add(rid.Name);
                            return true;
                        }
                        if (lid.Name.StartsWith(rightAlias + ".", StringComparison.OrdinalIgnoreCase)
                            && rid.Name.Contains('.') && !rid.Name.StartsWith(rightAlias + ".", StringComparison.OrdinalIgnoreCase))
                        {
                            leftKeys.Add(rid.Name); rightKeys.Add(lid.Name);
                            return true;
                        }
                        // Unqualified identifiers: use bare column sets to determine which side each belongs to.
                        // Use bare names as hash keys — combined rows carry both unqualified and
                        // qualified column names, so bare lookups work correctly across multi-join
                        // accumulated rows where leftAlias is always the FROM table, not the owning table.
                        if (!lid.Name.Contains('.') && !rid.Name.Contains('.') && leftCols != null && rightCols != null)
                        {
                            bool lidInLeft = leftCols.Contains(lid.Name);
                            bool lidInRight = rightCols.Contains(lid.Name);
                            bool ridInLeft = leftCols.Contains(rid.Name);
                            bool ridInRight = rightCols.Contains(rid.Name);
                            if (lidInLeft && ridInRight && !lidInRight && !ridInLeft)
                            {
                                leftKeys.Add(lid.Name);
                                rightKeys.Add(rid.Name);
                                return true;
                            }
                            if (ridInLeft && lidInRight && !ridInRight && !lidInLeft)
                            {
                                leftKeys.Add(rid.Name);
                                rightKeys.Add(lid.Name);
                                return true;
                            }
                        }
                    }
                }
                else if (bin.Operator == TokenType.AND)
                {
                    bool left = TryExtractEqualityKeys(bin.Left, leftAlias, rightAlias, leftKeys, rightKeys, leftCols, rightCols);
                    bool right = TryExtractEqualityKeys(bin.Right, leftAlias, rightAlias, leftKeys, rightKeys, leftCols, rightCols);
                    return left || right;
                }
            }
            return false;
        }

        private CompoundKey GetHashKey(Row row, List<string> keys)
        {
            var values = new object?[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                values[i] = row[keys[i]];
            }
            return new CompoundKey(values);
        }
    }
}
