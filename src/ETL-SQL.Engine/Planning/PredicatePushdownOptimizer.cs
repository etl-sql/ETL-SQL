using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Planning
{
    /// <summary>
    /// Logical query optimizer for SELECT predicate pushdown.
    ///
    /// Responsibilities:
    ///   1. Promotes eligible CROSS JOIN → INNER JOIN rewrites by extracting equality
    ///      predicates from the WHERE clause (formerly CrossJoinPredicatePushdown).
    ///   2. Flattens AND-conjuncts from the WHERE clause and classifies each by scope
    ///      (LeftSingle / RightSingle / MultiSource / Conservative), enabling the execution
    ///      engine to decide where to apply each predicate in the join pipeline.
    ///   3. Runs <see cref="RequiredColumnAnalyzer"/> on the rewritten statement.
    ///
    /// Conservative treatment: predicates containing subqueries or whose table references
    /// cannot be matched to known source aliases are left post-join and not pushed.
    /// </summary>
    public static class PredicatePushdownOptimizer
    {
        public static LogicalPlan Optimize(SelectStatement stmt)
        {
            // Apply CROSS JOIN → INNER JOIN rewrite first so the rewritten statement
            // is what gets analyzed for predicate classification.
            var rewritten = RewriteCrossJoins(stmt);

            // Classify WHERE predicates when there is at least one JOIN.
            var predicates = new List<LogicalPredicate>();
            if (rewritten.WhereClause != null && rewritten.Joins != null && rewritten.Joins.Count > 0)
                ClassifyPredicates(rewritten, predicates);

            return new LogicalPlan
            {
                Statement = rewritten,
                Predicates = predicates,
                RequiredColumns = RequiredColumnAnalyzer.Analyze(rewritten),
            };
        }

        // ── Predicate classification ─────────────────────────────────────────────

        private static void ClassifyPredicates(SelectStatement stmt, List<LogicalPredicate> output)
        {
            string leftAlias = stmt.FromTable.Alias ?? stmt.FromTable.TableName;

            // Build the set of right-side aliases (one per JOIN clause).
            var rightAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var join in stmt.Joins)
                rightAliases.Add(join.Table.Alias ?? join.Table.TableName);

            var allAliases = new HashSet<string>(rightAliases, StringComparer.OrdinalIgnoreCase);
            allAliases.Add(leftAlias);

            var andClauses = new List<Expression>();
            FlattenAnds(stmt.WhereClause!, andClauses);

            foreach (var pred in andClauses)
            {
                if (ContainsSubquery(pred))
                {
                    output.Add(new LogicalPredicate(pred, PredicateScope.Conservative, null));
                    continue;
                }

                var sources = pred.GetSourceTables()
                                  .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (sources.Count == 0)
                {
                    // Unqualified — the identifier qualifier could not attribute this predicate to a
                    // specific source (e.g. schema unavailable for the connection type at plan time).
                    // Keep post-join so we don't accidentally pre-filter the FROM-side rows using a
                    // column that actually lives on a right-side table, which would produce 0 rows.
                    output.Add(new LogicalPredicate(pred, PredicateScope.Conservative, null));
                }
                else if (sources.Count == 1 && sources.Contains(leftAlias))
                {
                    output.Add(new LogicalPredicate(pred, PredicateScope.LeftSingle, leftAlias));
                }
                else if (sources.Count == 1 && rightAliases.Contains(sources.First()))
                {
                    output.Add(new LogicalPredicate(pred, PredicateScope.RightSingle, sources.First()));
                }
                else if (sources.IsSubsetOf(allAliases))
                {
                    // References columns from multiple known sources — keep post-join.
                    output.Add(new LogicalPredicate(pred, PredicateScope.MultiSource, null));
                }
                else
                {
                    // References unknown aliases (e.g. outer-scope correlated ref) — be conservative.
                    output.Add(new LogicalPredicate(pred, PredicateScope.Conservative, null));
                }
            }
        }

        // ── CROSS JOIN → INNER JOIN rewrite ──────────────────────────────────────

        /// <summary>
        /// Rewrites comma-join CROSS JOINs (condition = literal true) into INNER JOINs by
        /// extracting matching predicates from the WHERE clause. Prevents O(n^k) Cartesian-product
        /// materialization for k-table comma-join queries, converting them to O(n) hash joins.
        /// Formerly a standalone CrossJoinPredicatePushdown class; inlined here to share FlattenAnds.
        /// </summary>
        private static SelectStatement RewriteCrossJoins(SelectStatement stmt)
        {
            if (stmt.Joins == null || stmt.Joins.Count == 0) return stmt;
            if (stmt.WhereClause == null) return stmt;
            if (!stmt.Joins.Any(IsTrueCrossJoin)) return stmt;

            var predicates = new List<Expression>();
            FlattenAnds(stmt.WhereClause, predicates);
            if (predicates.Count == 0) return stmt;

            var pushed = new HashSet<int>();
            string fromAlias = stmt.FromTable.Alias ?? stmt.FromTable.TableName;
            var leftTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { fromAlias };

            var remainingJoins = new List<JoinClause>(stmt.Joins);
            var newJoins = new List<JoinClause>(stmt.Joins.Count);
            bool anyPushed = false;

            while (remainingJoins.Count > 0)
            {
                JoinClause? bestJoin = null;
                int bestIndex = -1;
                List<Expression>? bestMatchList = null;

                for (int j = 0; j < remainingJoins.Count; j++)
                {
                    var join = remainingJoins[j];
                    if (!IsTrueCrossJoin(join)) continue;

                    string rightAlias = join.Table.Alias ?? join.Table.TableName;
                    var matchList = new List<Expression>();

                    for (int i = 0; i < predicates.Count; i++)
                    {
                        if (pushed.Contains(i)) continue;

                        var tables = predicates[i].GetSourceTables()
                                                  .ToHashSet(StringComparer.OrdinalIgnoreCase);
                        if (!tables.Contains(rightAlias)) continue;

                        // Only promote true cross-table predicates (referencing both sides) to INNER
                        // JOIN conditions. A predicate that only references the right table (e.g.
                        // t7.e7=280) is a single-table filter, not a join predicate — pushing it as
                        // the join condition breaks hash-join key construction and yields 0 rows.
                        // Leave single-table right-side predicates in the WHERE clause.
                        if (!tables.Overlaps(leftTables)) continue;

                        var available = new HashSet<string>(leftTables, StringComparer.OrdinalIgnoreCase) { rightAlias };
                        if (!tables.IsSubsetOf(available)) continue;

                        matchList.Add(predicates[i]);
                    }

                    if (matchList.Count > 0)
                    {
                        bestJoin = join;
                        bestIndex = j;
                        bestMatchList = matchList;
                        break; // Greedily pick the first cross join that can be successfully rewritten
                    }
                }

                if (bestJoin != null)
                {
                    string rightAlias = bestJoin.Table.Alias ?? bestJoin.Table.TableName;

                    // Mark match list expressions as pushed
                    foreach (var m in bestMatchList!)
                    {
                        for (int i = 0; i < predicates.Count; i++)
                        {
                            if (predicates[i] == m) pushed.Add(i);
                        }
                    }

                    Expression cond = bestMatchList.Count == 1
                        ? bestMatchList[0]
                        : bestMatchList.Skip(1).Aggregate(
                            bestMatchList[0],
                            (acc, p) => (Expression)new BinaryExpression(acc, TokenType.AND, p));

                    newJoins.Add(new JoinClause("INNER JOIN", bestJoin.Table, cond, bestJoin.Hint, bestJoin.KeepBest));
                    leftTables.Add(rightAlias);
                    remainingJoins.RemoveAt(bestIndex);
                    anyPushed = true;
                }
                else
                {
                    // No cross join could be rewritten with current leftTables. We must pull the next join in sequence.
                    var join = remainingJoins[0];
                    string rightAlias = join.Table.Alias ?? join.Table.TableName;

                    newJoins.Add(join);
                    leftTables.Add(rightAlias);
                    remainingJoins.RemoveAt(0);
                }
            }

            if (!anyPushed) return stmt;

            Expression? newWhere = null;
            for (int i = 0; i < predicates.Count; i++)
            {
                if (pushed.Contains(i)) continue;
                newWhere = newWhere == null
                    ? predicates[i]
                    : new BinaryExpression(newWhere, TokenType.AND, predicates[i]);
            }

            return stmt with { Joins = newJoins, WhereClause = newWhere };
        }

        private static bool IsTrueCrossJoin(JoinClause j) =>
            string.Equals(j.JoinType, "CROSS JOIN", StringComparison.OrdinalIgnoreCase)
            && j.Condition is LiteralExpression lit && true.Equals(lit.Value);

        // ── Helpers ──────────────────────────────────────────────────────────────

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

        /// <summary>
        /// Returns true if the expression tree contains any subquery node
        /// (<see cref="SubqueryExpression"/> or <see cref="ExistsExpression"/>).
        /// Predicates with subqueries are kept post-join (Conservative scope) to avoid
        /// evaluating them against partially-constructed rows.
        /// </summary>
        private static bool ContainsSubquery(Expression? expr)
        {
            if (expr == null) return false;
            return expr switch
            {
                SubqueryExpression => true,
                ExistsExpression => true,
                BinaryExpression bin => ContainsSubquery(bin.Left) || ContainsSubquery(bin.Right),
                FunctionCallExpression func =>
                    func.Arguments.Any(ContainsSubquery)
                    || (func.Window != null && (
                        func.Window.PartitionBy.Any(ContainsSubquery)
                        || func.Window.OrderBy.Any(o => ContainsSubquery(o.Expression)))),
                CaseExpression caseExpr =>
                    ContainsSubquery(caseExpr.InputExpression)
                    || caseExpr.WhenClauses.Any(w => ContainsSubquery(w.Condition) || ContainsSubquery(w.Result))
                    || ContainsSubquery(caseExpr.ElseResult),
                IsNullExpression isNull => ContainsSubquery(isNull.Expression),
                IsDistinctFromExpression idf => ContainsSubquery(idf.Left) || ContainsSubquery(idf.Right),
                MemberAccessExpression mem => ContainsSubquery(mem.Expression),
                ListExpression list => list.Items.Any(ContainsSubquery),
                InExpression inExpr => ContainsSubquery(inExpr.Left) || ContainsSubquery(inExpr.Right),
                _ => false,
            };
        }

        /// <summary>
        /// Returns single-source predicates for a given alias from a classified plan.
        /// Used by the execution engine to apply pre-join filters.
        /// </summary>
        public static IEnumerable<LogicalPredicate> GetSingleSourcePredicates(
            LogicalPlan plan, string alias)
            => plan.Predicates.Where(p =>
                string.Equals(p.SourceAlias, alias, StringComparison.OrdinalIgnoreCase)
                && (p.Scope == PredicateScope.LeftSingle || p.Scope == PredicateScope.RightSingle));
    }
}
