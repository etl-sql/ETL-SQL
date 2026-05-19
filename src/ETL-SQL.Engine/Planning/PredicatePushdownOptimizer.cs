using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Engine.Engines;

namespace ETL_SQL.Engine.Planning
{
    /// <summary>
    /// Logical query optimizer for SELECT predicate pushdown.
    ///
    /// Responsibilities:
    ///   1. Flattens AND-conjuncts from the WHERE clause and classifies each by scope
    ///      (LeftSingle / RightSingle / MultiSource / Conservative), enabling the execution
    ///      engine to decide where to apply each predicate in the join pipeline.
    ///   2. Promotes eligible CROSS JOIN → INNER JOIN rewrites (delegates to
    ///      <see cref="CrossJoinPredicatePushdown"/> for the AST rewrite itself).
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
            var rewritten = CrossJoinPredicatePushdown.Optimize(stmt);

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
                    // No table qualifier — classify as left-single (will be applied to FROM source).
                    output.Add(new LogicalPredicate(pred, PredicateScope.LeftSingle, leftAlias));
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
