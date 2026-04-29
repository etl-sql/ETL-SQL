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

                bool isLocal = false;
                if (id.Name.Contains("."))
                {
                    var parts = id.Name.Split('.');
                    var qualifier = string.Join(".", parts.Take(parts.Length - 1));
                    foreach (var scope in _localAliasStack)
                    {
                        if (scope.Contains(qualifier)) { isLocal = true; break; }
                    }
                }
                else
                {
                    foreach (var scope in _localAliasStack)
                    {
                        if (scope.Contains(id.Name)) { isLocal = true; break; }
                    }
                }

                if (!isLocal) outerRefs.Add(id.Name);
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
            else if (node is ListExpression list)
            {
                foreach (var item in list.Items) CollectOuterReferences(item, outerRefs);
            }
            else if (node is SubqueryExpression subq)
            {
                CollectOuterReferences(subq.Query, outerRefs);
            }
        }
    }
}
