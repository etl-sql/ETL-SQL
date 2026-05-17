using ETL_SQL.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;

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

            // Ensure the initial left rows are qualified with the base table alias
            string baseAlias = stmt.FromTable.Alias ?? stmt.FromTable.TableName;
            if (allBufferedRows.Count > 0)
            {
                foreach (var r in allBufferedRows)
                {
                    foreach (var kv in r.Columns)
                    {
                        if (!kv.Key.Contains(".")) r[$"{baseAlias}.{kv.Key}"] = kv.Value;
                    }
                }
            }

            // Flatten WHERE predicates for progressive pushdown into CROSS JOINs whose predicates use
            // unqualified column names (which CrossJoinPredicatePushdown cannot handle via GetSourceTables).
            var wherePredicates = new List<Expression>();
            if (stmt.WhereClause != null) FlattenAnds(stmt.WhereClause, wherePredicates);

            foreach (var join in joins)
            {
                if (join.IsApply)
                {
                    allBufferedRows = await PerformApplyJoin(allBufferedRows, join);
                    continue;
                }

                _logger.Debug("Joining table {TableName}{Alias} ({JoinType})", join.Table.TableName, join.Table.Alias != null ? $" AS {join.Table.Alias}" : "", join.JoinType);
                var joinRows = await GetJoinRows(join);

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
                    bool hasEquality = TryExtractEqualityKeys(effectiveJoin.Condition, baseAlias, rightAlias, hashKeysLeft, hashKeysRight);

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
                                allBufferedRows = await PerformNestedLoopJoin(allBufferedRows, joinRows, effectiveJoin);
                                break;
                        }
                    }
                }
            }
            return allBufferedRows;
        }

        /// <summary>
        /// For a CROSS JOIN with a literal-true condition, finds WHERE predicates whose referenced columns
        /// are all available in the combined left+right row, and returns a new INNER JOIN using those
        /// predicates as the condition. Falls back to the original join if no predicates are applicable.
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
            return new JoinClause("INNER JOIN", join.Table, cond, join.Hint, join.KeepBest);
        }

        private static HashSet<string> BuildBareColumnSet(Row leftSample, Row rightSample)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in leftSample.GetColumnNames())
                set.Add(name.Contains('.') ? name[(name.IndexOf('.') + 1)..] : name);
            foreach (var name in rightSample.GetColumnNames())
                set.Add(name.Contains('.') ? name[(name.IndexOf('.') + 1)..] : name);
            return set;
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
            await foreach (var jb in joinBatches)
            {
                foreach (var jr in jb.Rows)
                {
                    var r = jr.Clone();
                    foreach (var kv in jr.Columns) r[$"{joinName}.{kv.Key}"] = kv.Value;
                    yield return r;
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
            var results = new List<Row>();
            await foreach (var r in PerformNestedLoopJoinStream(leftRows.ToAsyncEnumerable(), rightRows, join)) results.Add(r);
            return results;
        }

        private async IAsyncEnumerable<Row> PerformNestedLoopJoinStream(IAsyncEnumerable<Row> leftStream, List<Row> rightRows, JoinClause join)
        {
            var matchedRight = new HashSet<Row>();

            await foreach (var left in leftStream)
            {
                bool foundMatch = false;
                foreach (var right in rightRows)
                {
                    var combined = CombineRows(left, right);
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
                    if (!matchedRight.Contains(right)) yield return right.Clone();
                }
            }
        }

        private async Task<List<Row>> PerformHashJoin(List<Row> leftRows, List<Row> rightRows, JoinClause join, List<string> leftKeys, List<string> rightKeys)
        {
            var results = new List<Row>();
            await foreach (var r in PerformHashJoinStream(leftRows.ToAsyncEnumerable(), rightRows, join, leftKeys, rightKeys)) results.Add(r);
            return results;
        }

        private async IAsyncEnumerable<Row> PerformHashJoinStream(IAsyncEnumerable<Row> leftStream, List<Row> rightRows, JoinClause join, List<string> leftKeys, List<string> rightKeys)
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
            var matchedLeft = new HashSet<Row>(); // Needed only for FULL/LEFT OUTER? No, we yield as we go for LEFT.

            await foreach (var left in leftStream)
            {
                var key = GetHashKey(left, leftKeys);
                bool foundMatch = false;
                if (hashTable.TryGetValue(key, out var matches))
                {
                    foreach (var right in matches)
                    {
                        var combined = CombineRows(left, right);
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
                    if (!matchedRight.Contains(right)) yield return right.Clone();
                }
            }
        }

        private async Task<List<Row>> PerformMergeJoin(List<Row> leftRows, List<Row> rightRows, JoinClause join, List<string> leftKeys, List<string> rightKeys)
        {
            _logger.Debug("  Performing Merge Join (Sorting {LeftCount} and {RightCount} rows)", leftRows.Count, rightRows.Count);
            var sortedLeft = leftRows.OrderBy(r => GetHashKey(r, leftKeys)).ToList();
            var sortedRight = rightRows.OrderBy(r => GetHashKey(r, rightKeys)).ToList();

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
                            var combined = CombineRows(sortedLeft[i], sortedRight[j]);
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
                foreach (var right in rightRows) if (!matchedRight.Contains(right)) nextRows.Add(right.Clone());
            }

            return nextRows;
        }

        private Row CombineRows(Row left, Row right)
        {
            var combined = new Row();
            foreach (var kv in left.Columns) combined[kv.Key] = kv.Value;
            foreach (var kv in right.Columns) combined[kv.Key] = kv.Value;
            return combined;
        }

        private bool IsLeftOuter(string type) => type.Contains("LEFT", StringComparison.OrdinalIgnoreCase) || type.Contains("FULL", StringComparison.OrdinalIgnoreCase) || type.Contains("OUTER", StringComparison.OrdinalIgnoreCase);
        private bool IsRightOuter(string type) => type.Contains("RIGHT", StringComparison.OrdinalIgnoreCase) || type.Contains("FULL", StringComparison.OrdinalIgnoreCase);

        public bool TryExtractEqualityKeys(Expression? cond, string leftAlias, string rightAlias, List<string> leftKeys, List<string> rightKeys)
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
                    }
                }
                else if (bin.Operator == TokenType.AND)
                {
                    bool left = TryExtractEqualityKeys(bin.Left, leftAlias, rightAlias, leftKeys, rightKeys);
                    bool right = TryExtractEqualityKeys(bin.Right, leftAlias, rightAlias, leftKeys, rightKeys);
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
