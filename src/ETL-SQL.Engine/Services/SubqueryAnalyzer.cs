using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Services
{
    /// <summary>
    /// Analyzes subqueries to identify references to identifiers in outer scopes (correlation).
    /// </summary>
    public class SubqueryAnalyzer
    {
        private readonly Stack<HashSet<string>> _localAliasStack = new();

        public List<string> GetOuterReferences(SelectStatement subquery)
        {
            _localAliasStack.Clear();
            var outerRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            CollectOuterReferences(subquery, outerRefs);

            return outerRefs.OrderBy(s => s).ToList();
        }

        private void CollectOuterReferences(AstNode? node, HashSet<string> outerRefs)
        {
            if (node == null) return;

            if (node is SelectStatement sel)
            {
                var localAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (sel.FromTable != null) localAliases.Add(sel.FromTable.Alias ?? sel.FromTable.TableName);
                foreach (var join in sel.Joins) localAliases.Add(join.Table.Alias ?? join.Table.TableName);

                _localAliasStack.Push(localAliases);
                try
                {
                    // Walk FROM and JOINs for outer references (e.g. correlated table functions)
                    if (sel.FromTable != null) CollectOuterReferences(sel.FromTable, outerRefs);
                    foreach (var join in sel.Joins) CollectOuterReferences(join, outerRefs);

                    foreach (var col in sel.Columns) CollectOuterReferences(col.Expression, outerRefs);
                    CollectOuterReferences(sel.WhereClause, outerRefs);
                    if (sel.GroupBy != null) foreach (var g in sel.GroupBy) CollectOuterReferences(g, outerRefs);
                    CollectOuterReferences(sel.HavingClause, outerRefs);
                    if (sel.OrderBy != null) foreach (var o in sel.OrderBy) CollectOuterReferences(o.Expression, outerRefs);
                }
                finally
                {
                    _localAliasStack.Pop();
                }
                return;
            }

            if (node is IdentifierExpression id)
            {
                if (id.Name == "*") return;

                // Only qualified identifiers (e.g. "t1.col") can be outer references.
                // Unqualified identifiers resolve against the current row and are never outer references —
                // checking them against the table-alias stack (which contains alias names, not column names)
                // produces false positives that break scalar subquery caching.
                if (!id.Name.Contains(".")) return;

                var parts = id.Name.Split('.');
                var qualifier = string.Join(".", parts.Take(parts.Length - 1));
                foreach (var scope in _localAliasStack)
                {
                    if (scope.Contains(qualifier)) return;
                }
                outerRefs.Add(id.Name);
                return;
            }

            if (node is BinaryExpression bin)
            {
                CollectOuterReferences(bin.Left, outerRefs);
                CollectOuterReferences(bin.Right, outerRefs);
            }
            else if (node is UnaryExpression un)
            {
                CollectOuterReferences(un.Expression, outerRefs);
            }
            else if (node is FunctionCallExpression func)
            {
                foreach (var arg in func.Arguments) CollectOuterReferences(arg, outerRefs);
                if (func.Window != null)
                {
                    foreach (var p in func.Window.PartitionBy) CollectOuterReferences(p, outerRefs);
                    foreach (var o in func.Window.OrderBy) CollectOuterReferences(o.Expression, outerRefs);
                }
            }
            else if (node is CaseExpression caseExpr)
            {
                foreach (var clause in caseExpr.WhenClauses)
                {
                    CollectOuterReferences(clause.Condition, outerRefs);
                    CollectOuterReferences(clause.Result, outerRefs);
                }
                CollectOuterReferences(caseExpr.ElseResult, outerRefs);
            }
            else if (node is InExpression inExpr)
            {
                CollectOuterReferences(inExpr.Left, outerRefs);
                CollectOuterReferences(inExpr.Right, outerRefs);
            }
            else if (node is ExistsExpression exists)
            {
                CollectOuterReferences(exists.Subquery, outerRefs);
            }
            else if (node is IsNullExpression isNull)
            {
                CollectOuterReferences(isNull.Expression, outerRefs);
            }
            else if (node is MemberAccessExpression ma)
            {
                CollectOuterReferences(ma.Expression, outerRefs);
            }
            else if (node is VariableExpression vex)
            {
                outerRefs.Add(vex.Name);
            }
            else if (node is ListExpression list)
            {
                foreach (var item in list.Items) CollectOuterReferences(item, outerRefs);
            }
            else if (node is SubqueryExpression subq)
            {
                CollectOuterReferences(subq.Query, outerRefs);
            }
            else if (node is TableReference tr)
            {
                if (tr.FunctionCall != null) CollectOuterReferences(tr.FunctionCall, outerRefs);
                if (tr.Subquery != null) CollectOuterReferences(tr.Subquery, outerRefs);
                foreach (var op in tr.TableOperators) CollectOuterReferences(op, outerRefs);
            }
            else if (node is JoinClause join)
            {
                CollectOuterReferences(join.Table, outerRefs);
                CollectOuterReferences(join.Condition, outerRefs);
            }
        }
    }
}
