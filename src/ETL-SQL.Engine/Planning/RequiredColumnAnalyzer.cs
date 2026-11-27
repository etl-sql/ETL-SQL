using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Planning
{
    /// <summary>
    /// Walks a SelectStatement AST and returns the set of unqualified column names
    /// (short names, e.g. "id" not "t.id") referenced across every clause.
    /// Used by SelectExecutionEngine to drop unreferenced columns from rows after
    /// the join phase, reducing in-memory working set before GROUP BY / WINDOW / ORDER BY.
    /// Returns null when analysis is not safe (e.g. SELECT *).
    /// </summary>
    public static class RequiredColumnAnalyzer
    {
        public static HashSet<string>? Analyze(SelectStatement stmt, bool includeWhere = true)
        {
            // Wildcard columns make pruning unsafe — we don't know which columns are needed.
            if (stmt.Columns.Any(c => c.Expression is StarExpression
                                   || c.Expression is IdentifierExpression { Name: "*" }
                                   || (c.Expression is IdentifierExpression id2 && id2.Name.EndsWith(".*"))))
                return null;

            // APPLY / LATERAL joins evaluate correlated right-side sources that can reference any left column.
            if (stmt.Joins != null && stmt.Joins.Any(j => j.IsApply))
                return null;

            var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool hasUnsafeNode = false;

            foreach (var col in stmt.Columns)
                Collect(col.Expression, required, ref hasUnsafeNode);

            if (includeWhere)
                Collect(stmt.WhereClause, required, ref hasUnsafeNode);
            Collect(stmt.HavingClause, required, ref hasUnsafeNode);
            Collect(stmt.QualifyClause, required, ref hasUnsafeNode);

            foreach (var g in stmt.GroupBy ?? Enumerable.Empty<Expression>())
                Collect(g, required, ref hasUnsafeNode);

            if (stmt.GroupingSet != null)
            {
                foreach (var set in stmt.GroupingSet.GroupSets)
                {
                    foreach (var g in set)
                        Collect(g, required, ref hasUnsafeNode);
                }
            }

            foreach (var o in stmt.OrderBy ?? Enumerable.Empty<OrderByClause>())
                Collect(o.Expression, required, ref hasUnsafeNode);

            foreach (var join in stmt.Joins ?? Enumerable.Empty<JoinClause>())
                Collect(join.Condition, required, ref hasUnsafeNode);

            return hasUnsafeNode ? null : required;
        }

        private static void Collect(Expression? expr, HashSet<string> required, ref bool hasUnsafeNode)
        {
            if (expr == null || hasUnsafeNode) return;

            switch (expr)
            {
                case StarExpression:
                case SubqueryExpression:
                case ExistsExpression:
                    // Correlated subqueries or nested wildcards can reference arbitrary outer columns.
                    hasUnsafeNode = true;
                    break;

                case IdentifierExpression id:
                    required.Add(id.Name.Contains('.') ? id.Name.Split('.')[^1] : id.Name);
                    break;

                case BinaryExpression bin:
                    Collect(bin.Left, required, ref hasUnsafeNode);
                    Collect(bin.Right, required, ref hasUnsafeNode);
                    break;

                case MemberAccessExpression mem:
                    required.Add(mem.MemberName);
                    Collect(mem.Expression, required, ref hasUnsafeNode);
                    break;

                case CaseExpression caseExpr:
                    Collect(caseExpr.InputExpression, required, ref hasUnsafeNode);
                    foreach (var (cond, result) in caseExpr.WhenClauses)
                    {
                        Collect(cond, required, ref hasUnsafeNode);
                        Collect(result, required, ref hasUnsafeNode);
                    }
                    Collect(caseExpr.ElseResult, required, ref hasUnsafeNode);
                    break;

                case IsNullExpression isNull:
                    Collect(isNull.Expression, required, ref hasUnsafeNode);
                    break;

                case IsDistinctFromExpression idf:
                    Collect(idf.Left, required, ref hasUnsafeNode);
                    Collect(idf.Right, required, ref hasUnsafeNode);
                    break;

                case InExpression inExp:
                    Collect(inExp.Left, required, ref hasUnsafeNode);
                    Collect(inExp.Right, required, ref hasUnsafeNode);
                    break;

                case BetweenExpression bet:
                    Collect(bet.Left, required, ref hasUnsafeNode);
                    Collect(bet.Start, required, ref hasUnsafeNode);
                    Collect(bet.End, required, ref hasUnsafeNode);
                    break;

                case LikeExpression like:
                    Collect(like.Left, required, ref hasUnsafeNode);
                    Collect(like.Pattern, required, ref hasUnsafeNode);
                    Collect(like.EscapeChar, required, ref hasUnsafeNode);
                    break;

                case UnaryExpression un:
                    Collect(un.Expression, required, ref hasUnsafeNode);
                    break;

                case ListExpression list:
                    foreach (var item in list.Items) Collect(item, required, ref hasUnsafeNode);
                    break;

                case AtTimeZoneExpression atz:
                    Collect(atz.Left, required, ref hasUnsafeNode);
                    Collect(atz.TimeZone, required, ref hasUnsafeNode);
                    break;

                case FunctionCallExpression func:
                    foreach (var arg in func.Arguments)
                    {
                        // COUNT(*) does not require all columns.
                        if (func.FunctionName.Equals("COUNT", StringComparison.OrdinalIgnoreCase)
                            && (arg is StarExpression || arg is IdentifierExpression { Name: "*" }))
                            continue;
                        Collect(arg, required, ref hasUnsafeNode);
                    }
                    if (func.Filter != null) Collect(func.Filter, required, ref hasUnsafeNode);
                    if (func.Window != null)
                    {
                        foreach (var p in func.Window.PartitionBy) Collect(p, required, ref hasUnsafeNode);
                        foreach (var o in func.Window.OrderBy) Collect(o.Expression, required, ref hasUnsafeNode);
                    }
                    if (func.WithinGroupOrderBy != null)
                        foreach (var o in func.WithinGroupOrderBy) Collect(o.Expression, required, ref hasUnsafeNode);
                    break;

                    // Literals, variables, and parameters carry no column names.
            }
        }

        /// <summary>
        /// Returns true if the column (as it appears on a Row key) should be kept
        /// given the required column set. Handles both qualified ("t.id") and
        /// unqualified ("id") key forms.
        /// </summary>
        public static bool IsRequired(string columnKey, HashSet<string> required)
        {
            if (required.Contains(columnKey)) return true;
            if (columnKey.Contains('.'))
            {
                var shortName = columnKey.Split('.')[^1];
                return required.Contains(shortName);
            }
            return false;
        }
    }
}
