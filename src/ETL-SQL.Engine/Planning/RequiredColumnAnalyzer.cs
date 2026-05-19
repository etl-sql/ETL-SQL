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
        public static HashSet<string>? Analyze(SelectStatement stmt)
        {
            // Wildcard columns make pruning unsafe — we don't know which columns are needed.
            if (stmt.Columns.Any(c => c.Expression is IdentifierExpression { Name: "*" }
                                   || (c.Expression is IdentifierExpression id2 && id2.Name.EndsWith(".*"))))
                return null;

            var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var col in stmt.Columns)
                Collect(col.Expression, required);

            Collect(stmt.WhereClause, required);
            Collect(stmt.HavingClause, required);
            Collect(stmt.QualifyClause, required);

            foreach (var g in stmt.GroupBy ?? Enumerable.Empty<Expression>())
                Collect(g, required);

            foreach (var o in stmt.OrderBy ?? Enumerable.Empty<OrderByClause>())
                Collect(o.Expression, required);

            foreach (var join in stmt.Joins ?? Enumerable.Empty<JoinClause>())
                Collect(join.Condition, required);

            return required;
        }

        private static void Collect(Expression? expr, HashSet<string> required)
        {
            if (expr == null) return;

            switch (expr)
            {
                case IdentifierExpression id:
                    required.Add(id.Name.Contains('.') ? id.Name.Split('.')[^1] : id.Name);
                    break;

                case BinaryExpression bin:
                    Collect(bin.Left, required);
                    Collect(bin.Right, required);
                    break;

                case MemberAccessExpression mem:
                    required.Add(mem.MemberName);
                    Collect(mem.Expression, required);
                    break;

                case CaseExpression caseExpr:
                    Collect(caseExpr.InputExpression, required);
                    foreach (var (cond, result) in caseExpr.WhenClauses)
                    {
                        Collect(cond, required);
                        Collect(result, required);
                    }
                    Collect(caseExpr.ElseResult, required);
                    break;

                case IsNullExpression isNull:
                    Collect(isNull.Expression, required);
                    break;

                case ListExpression list:
                    foreach (var item in list.Items) Collect(item, required);
                    break;

                case AtTimeZoneExpression atz:
                    Collect(atz.Left, required);
                    break;

                case FunctionCallExpression func:
                    foreach (var arg in func.Arguments) Collect(arg, required);
                    if (func.Filter != null) Collect(func.Filter, required);
                    if (func.Window != null)
                    {
                        foreach (var p in func.Window.PartitionBy) Collect(p, required);
                        foreach (var o in func.Window.OrderBy) Collect(o.Expression, required);
                    }
                    if (func.WithinGroupOrderBy != null)
                        foreach (var o in func.WithinGroupOrderBy) Collect(o.Expression, required);
                    break;

                // Subqueries, literals, variables, parameters reference their own scope
                // or carry no column names — skip.
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
