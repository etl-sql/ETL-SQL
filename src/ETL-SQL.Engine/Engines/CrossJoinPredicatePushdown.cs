using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.Engine.Engines
{
    /// <summary>
    /// Rewrites comma-join CROSS JOINs (condition = literal true) into INNER JOINs by
    /// extracting matching predicates from the WHERE clause. Prevents O(n^k) Cartesian-product
    /// materialization for k-table comma-join queries, converting them to O(n) hash joins.
    /// </summary>
    internal static class CrossJoinPredicatePushdown
    {
        public static SelectStatement Optimize(SelectStatement stmt)
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

            var newJoins = new List<JoinClause>(stmt.Joins.Count);
            bool anyPushed = false;

            foreach (var join in stmt.Joins)
            {
                string rightAlias = join.Table.Alias ?? join.Table.TableName;

                if (!IsTrueCrossJoin(join))
                {
                    newJoins.Add(join);
                    leftTables.Add(rightAlias);
                    continue;
                }

                var matchList = new List<Expression>();
                for (int i = 0; i < predicates.Count; i++)
                {
                    if (pushed.Contains(i)) continue;

                    var tables = predicates[i].GetSourceTables()
                                              .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (!tables.Contains(rightAlias)) continue;

                    // All referenced tables must already be available (left set + current right)
                    var available = new HashSet<string>(leftTables, StringComparer.OrdinalIgnoreCase);
                    available.Add(rightAlias);
                    if (!tables.IsSubsetOf(available)) continue;

                    matchList.Add(predicates[i]);
                    pushed.Add(i);
                }

                if (matchList.Count > 0)
                {
                    Expression cond = matchList.Count == 1
                        ? matchList[0]
                        : matchList.Skip(1).Aggregate(
                            matchList[0],
                            (acc, p) => (Expression)new BinaryExpression(acc, TokenType.AND, p));

                    newJoins.Add(new JoinClause("INNER JOIN", join.Table, cond, join.Hint, join.KeepBest));
                    anyPushed = true;
                }
                else
                {
                    newJoins.Add(join);
                }

                leftTables.Add(rightAlias);
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
    }
}
