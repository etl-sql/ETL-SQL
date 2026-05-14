using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine.Functions;

namespace ETL_SQL.Engine.Engines
{
    /// <summary>
    /// Executes FUZZY JOIN and LEFT FUZZY JOIN operations.
    ///
    /// Strategy:
    ///   1. Detect which column pair drives similarity (by inspecting the ON expression for SIMILARITY calls).
    ///   2. Build a trigram inverted index over the right-side table on that column.
    ///   3. For each left row, compute its trigrams, look up candidate right rows from the index,
    ///      score candidates with the full ON expression, apply threshold and KEEP BEST.
    ///   4. If no SIMILARITY call is found in the ON expression, fall back to full nested-loop
    ///      (with a warning) so composite expressions still work.
    ///
    /// __score is always injected into result rows: the numeric value of the similarity expression
    /// for matched rows, NULL for unmatched left rows in LEFT FUZZY JOIN.
    /// </summary>
    internal class FuzzyJoinEngine(IExecutionContext context, ILogger logger)
    {
        private const string ScoreColumn = "__score";

        public async Task<List<Row>> PerformFuzzyJoin(List<Row> leftRows, List<Row> rightRows, JoinClause join)
        {
            bool isLeft = join.JoinType.StartsWith("LEFT", StringComparison.OrdinalIgnoreCase);

            // Detect the similarity expression and right-side column for blocking
            var scoreExpr = ExtractScoreExpression(join.Condition);
            string? rightBlockCol = scoreExpr != null ? ExtractRightBlockingColumn(scoreExpr, join.Table.Alias ?? join.Table.TableName) : null;
            string? leftBlockCol  = scoreExpr != null ? ExtractLeftBlockingColumn(scoreExpr) : null;

            // Build a trigram inverted index on the right side when possible
            Dictionary<string, HashSet<int>>? index = null;
            if (rightBlockCol != null)
            {
                index = BuildTrigramIndex(rightRows, rightBlockCol);
                logger.Debug("FUZZY JOIN: trigram index built on '{RightCol}' ({RightCount} right rows)", rightBlockCol, rightRows.Count);
            }
            else
            {
                logger.WriteLine($"[yellow]FUZZY JOIN: no SIMILARITY column detected in ON expression — falling back to full nested-loop scan ({leftRows.Count} × {rightRows.Count}).[/]");
            }

            var result = new List<Row>(leftRows.Count);

            foreach (var left in leftRows)
            {
                // Determine candidate set via blocking (or full scan)
                IEnumerable<(int idx, Row row)> candidates;
                if (index != null && leftBlockCol != null)
                {
                    string leftVal = left[leftBlockCol]?.ToString() ?? "";
                    var grams = ComputeTrigrams(leftVal.ToLowerInvariant());
                    var candidateIndices = new HashSet<int>();
                    foreach (var gram in grams)
                    {
                        if (index.TryGetValue(gram, out var set))
                            candidateIndices.UnionWith(set);
                    }
                    candidates = candidateIndices.Select(i => (i, rightRows[i]));
                }
                else
                {
                    candidates = rightRows.Select((r, i) => (i, r));
                }

                // Score each candidate against the ON condition
                var matches = new List<(Row combined, decimal score)>();
                foreach (var (_, right) in candidates)
                {
                    var combined = CombineRows(left, right);

                    // Evaluate the full ON condition (threshold check)
                    var condResult = await context.EvaluateValue(join.Condition, combined);
                    bool passes = condResult is bool b ? b : condResult != null && Convert.ToBoolean(condResult);
                    if (!passes) continue;

                    // Extract the actual score for __score column
                    decimal score = 0m;
                    if (scoreExpr != null)
                    {
                        var scoreVal = await context.EvaluateValue(scoreExpr, combined);
                        score = scoreVal == null ? 0m : Convert.ToDecimal(scoreVal);
                    }
                    matches.Add((combined, score));
                }

                if (matches.Count == 0)
                {
                    if (isLeft)
                    {
                        var unmatched = left.Clone();
                        unmatched[ScoreColumn] = null;
                        result.Add(unmatched);
                    }
                    continue;
                }

                // Rank by score descending, apply KEEP BEST
                matches.Sort((a, b) => b.score.CompareTo(a.score));
                int take = join.KeepBest ?? matches.Count;
                foreach (var (combined, score) in matches.Take(take))
                {
                    combined[ScoreColumn] = score;
                    result.Add(combined);
                }
            }

            return result;
        }

        // ── Trigram index ──────────────────────────────────────────────────────────

        private static Dictionary<string, HashSet<int>> BuildTrigramIndex(List<Row> rows, string column)
        {
            var index = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Count; i++)
            {
                string val = rows[i][column]?.ToString()?.ToLowerInvariant() ?? "";
                foreach (var gram in ComputeTrigrams(val))
                {
                    if (!index.TryGetValue(gram, out var set))
                        index[gram] = set = new HashSet<int>();
                    set.Add(i);
                }
            }
            return index;
        }

        // Space-padded trigrams (same as NGRAM_TOKENS)
        private static IEnumerable<string> ComputeTrigrams(string s)
        {
            s = " " + s + " ";
            for (int i = 0; i <= s.Length - 3; i++)
                yield return s.Substring(i, 3);
        }

        // ── Row combination ────────────────────────────────────────────────────────

        private static Row CombineRows(Row left, Row right)
        {
            var r = left.Clone();
            foreach (var kv in right.Columns)
                r[kv.Key] = kv.Value;
            return r;
        }

        // ── Expression inspection — extract blocking columns and score expr ────────

        /// <summary>
        /// If the ON condition is <expr> > <threshold> (or >= or < or <=), returns the left-hand <expr>.
        /// This is the score expression that produces the 0–1 similarity value.
        /// </summary>
        internal static Expression? ExtractScoreExpression(Expression condition)
        {
            if (condition is BinaryExpression bin &&
                (bin.Operator == TokenType.GREATER_THAN || bin.Operator == TokenType.GREATER_EQUALS ||
                 bin.Operator == TokenType.LESS_THAN    || bin.Operator == TokenType.LESS_EQUALS))
            {
                // score > threshold  OR  threshold < score
                if (ContainsSimilarity(bin.Left))  return bin.Left;
                if (ContainsSimilarity(bin.Right)) return bin.Right;
            }
            return null;
        }

        private static bool ContainsSimilarity(Expression expr)
        {
            if (expr is FunctionCallExpression fn &&
                (fn.FunctionName.Equals("SIMILARITY", StringComparison.OrdinalIgnoreCase) ||
                 fn.FunctionName.Equals("LEVENSHTEIN", StringComparison.OrdinalIgnoreCase)))
                return true;
            if (expr is BinaryExpression bin)
                return ContainsSimilarity(bin.Left) || ContainsSimilarity(bin.Right);
            return false;
        }

        /// <summary>
        /// Walks the score expression to find the first SIMILARITY(left_col, right_col, ...) call
        /// and returns the right-side column name (for blocking index lookup).
        /// rightAlias is used to identify which argument is the right-side column.
        /// </summary>
        internal static string? ExtractRightBlockingColumn(Expression expr, string rightAlias)
        {
            if (expr is FunctionCallExpression fn &&
                fn.FunctionName.Equals("SIMILARITY", StringComparison.OrdinalIgnoreCase) &&
                fn.Arguments.Count >= 2)
            {
                // Try arg[1] as right-side column (SIMILARITY(left, right, algo))
                string? col = ResolveColumnName(fn.Arguments[1], rightAlias);
                if (col != null) return col;
                // Try arg[0] in case expression is written right-to-left
                col = ResolveColumnName(fn.Arguments[0], rightAlias);
                if (col != null) return col;
            }
            if (expr is BinaryExpression bin)
            {
                return ExtractRightBlockingColumn(bin.Left, rightAlias)
                    ?? ExtractRightBlockingColumn(bin.Right, rightAlias);
            }
            return null;
        }

        /// <summary>
        /// Returns the left-side column name from the first SIMILARITY call for blocking lookups.
        /// </summary>
        internal static string? ExtractLeftBlockingColumn(Expression expr)
        {
            if (expr is FunctionCallExpression fn &&
                fn.FunctionName.Equals("SIMILARITY", StringComparison.OrdinalIgnoreCase) &&
                fn.Arguments.Count >= 1)
            {
                // arg[0] is typically the left column; fall through to arg[1] if arg[0] is right
                return ResolveColumnName(fn.Arguments[0], null)
                    ?? ResolveColumnName(fn.Arguments[1], null);
            }
            if (expr is BinaryExpression bin)
            {
                return ExtractLeftBlockingColumn(bin.Left)
                    ?? ExtractLeftBlockingColumn(bin.Right);
            }
            return null;
        }

        private static string? ResolveColumnName(Expression expr, string? preferredAlias)
        {
            if (expr is IdentifierExpression id)
            {
                if (preferredAlias == null) return id.Name;
                // Prefer qualified names that match the alias
                if (id.Name.StartsWith(preferredAlias + ".", StringComparison.OrdinalIgnoreCase))
                    return id.Name;
                // Also accept bare names (may be ambiguous but try them)
                if (!id.Name.Contains('.'))
                    return id.Name;
            }
            // Handle NORMALIZE(col, preset) — blocking on the inner column
            if (expr is FunctionCallExpression fn &&
                fn.FunctionName.Equals("NORMALIZE", StringComparison.OrdinalIgnoreCase) &&
                fn.Arguments.Count >= 1)
            {
                return ResolveColumnName(fn.Arguments[0], preferredAlias);
            }
            return null;
        }
    }
}
