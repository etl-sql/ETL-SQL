using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Services
{
    /// <summary>
    /// Encapsulates the logic for compiling ETL-SQL AST nodes (expressions, queries) 
    /// back into provider-specific SQL strings.
    /// </summary>
    public class QueryCompiler
    {
        private readonly Evaluator _evaluator;

        public QueryCompiler(Evaluator evaluator)
        {
            _evaluator = evaluator;
        }

        /// <summary>
        /// Compiles a scalar expression back into a provider-specific SQL string.
        /// </summary>
        public string CompileExpression(Expression e, string d = "MSSQL")
        {
            if (e is IdentifierExpression id) 
            {
                if (id.Name.StartsWith("@"))
                {
                    var val = _evaluator.GetVariable(id.Name);
                    if (val is string s) return $"'{s.Replace("'", "''")}'";
                    if (val is DateTime dt) return $"'{dt:yyyy-MM-dd HH:mm:ss}'";
                    if (val is decimal dec) return dec.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    return val?.ToString() ?? "NULL";
                }
                return id.Name;
            }
            if (e is LiteralExpression lit) 
            {
                if (lit.Value is string s) return $"'{s.Replace("'", "''")}'";
                return lit.Value?.ToString() ?? "NULL";
            }
            if (e is BinaryExpression bin)
            {
                var op = bin.Operator switch {
                    TokenType.EQUALS => "=", TokenType.NOT_EQUALS => "!=", TokenType.LESS_THAN => "<",
                    TokenType.GREATER_THAN => ">", TokenType.LESS_EQUALS => "<=", TokenType.GREATER_EQUALS => ">=",
                    TokenType.PLUS => "+", TokenType.MINUS => "-", TokenType.STAR => "*", TokenType.SLASH => "/",
                    _ => bin.Operator.ToString()
                };
                return $"({CompileExpression(bin.Left, d)} {op} {CompileExpression(bin.Right, d)})";
            }
            if (e is FunctionCallExpression f)
            {
                var args = string.Join(", ", f.Arguments.Select(a => CompileExpression(a, d)));
                return $"{f.FunctionName}({args})";
            }
            if (e is VariableExpression v)
            {
                var val = _evaluator.GetVariable(v.Name);
                if (val is string s) return $"'{s.Replace("'", "''")}'";
                if (val is DateTime dt) return $"'{dt:yyyy-MM-dd HH:mm:ss}'";
                if (val is decimal dec) return dec.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return val?.ToString() ?? "NULL";
            }
            return e.ToString() ?? "";
        }

        /// <summary>
        /// Compiles a full SELECT or MERGE statement back into a provider-specific SQL string.
        /// </summary>
        public string CompileQuery(Statement s, string d = "MSSQL")
        {
            if (s is SelectStatement sel)
            {
                var selectParts = new List<string>();
                if (sel.TopCount != null && d == "MSSQL")
                {
                    var percent = sel.IsTopPercent ? " PERCENT" : "";
                    var ties = sel.WithTies ? " WITH TIES" : "";
                    selectParts.Add($"TOP ({CompileExpression(sel.TopCount, d)}){percent}{ties}");
                }

                if (sel.IsDistinct) selectParts.Add("DISTINCT");

                var cols = (sel.Columns.Count == 1 && sel.Columns[0].Expression is IdentifierExpression id && id.Name == "*")
                    ? "*"
                    : string.Join(", ", sel.Columns.Select(c => CompileExpression(c.Expression, d) + (c.Alias != null ? $" AS {c.Alias}" : "")));
                selectParts.Add(cols);

                var sql = "SELECT " + string.Join(" ", selectParts);
                
                if (sel.FromTable != null)
                {
                    sql += $" FROM {CompileTableReference(sel.FromTable, d)}";
                }

                if (sel.Joins != null)
                {
                    foreach (var join in sel.Joins)
                    {
                        var jt = join.JoinType;
                        if (!jt.Contains("JOIN") && !jt.Contains("APPLY")) jt += " JOIN";
                        sql += $" {jt} {CompileTableReference(join.Table, d)}";
                        if (!join.IsApply) sql += $" ON {CompileExpression(join.Condition, d)}";
                    }
                }

                if (sel.WhereClause != null) sql += " WHERE " + CompileExpression(sel.WhereClause, d);

                if (sel.GroupBy != null && sel.GroupBy.Count > 0)
                {
                    sql += " GROUP BY " + string.Join(", ", sel.GroupBy.Select(g => CompileExpression(g, d)));
                }

                if (sel.HavingClause != null) sql += " HAVING " + CompileExpression(sel.HavingClause, d);

                if (sel.OrderBy != null && sel.OrderBy.Count > 0)
                {
                    sql += " ORDER BY " + string.Join(", ", sel.OrderBy.Select(o => CompileExpression(o.Expression, d) + (o.Descending ? " DESC" : " ASC")));
                }

                if (sel.LimitCount != null)
                {
                    if (d != "MSSQL") sql += $" LIMIT {CompileExpression(sel.LimitCount, d)}";
                }

                if (sel.Offset != null)
                {
                    if (d == "MSSQL") 
                    {
                        sql += $" OFFSET {CompileExpression(sel.Offset, d)} ROWS";
                        if (sel.LimitCount != null) sql += $" FETCH NEXT {CompileExpression(sel.LimitCount, d)} ROWS ONLY";
                    }
                    else 
                    {
                        sql += $" OFFSET {CompileExpression(sel.Offset, d)}";
                    }
                }

                return sql;
            }
            if (s is SetOperationStatement setOp) 
            {
                string op = setOp.Operation switch {
                    SetOpType.UNION => "UNION", SetOpType.UNION_ALL => "UNION ALL",
                    SetOpType.EXCEPT => "EXCEPT", SetOpType.INTERSECT => "INTERSECT", _ => "UNION"
                };
                return $"{CompileQuery(setOp.Left, d)} {op} {CompileQuery(setOp.Right, d)}";
            }
            if (s is MergeStatement m)
            {
                return CompileMerge(m, d);
            }
            return s.ToSql();
        }

        /// <summary>
        /// Compiles a table reference (table name, subquery, or function) for a target provider.
        /// </summary>
        public string CompileTableReference(TableReference t, string d)
        {
            if (t == null) return "";
            string sql;
            if (t.Subquery != null)
            {
                sql = $"({CompileQuery(t.Subquery, d)})";
            }
            else if (t.FunctionCall != null)
            {
                var args = string.Join(", ", t.FunctionCall.Arguments.Select(a => CompileExpression(a, d)));
                sql = $"{t.FunctionCall.FunctionName}({args})";
            }
            else
            {
                var parts = new List<string>();
                if (t.SchemaName != null) parts.Add(t.SchemaName);
                parts.Add(t.TableName);
                sql = string.Join(".", parts);
            }

            if (t.Alias != null) sql += " AS " + t.Alias;
            return sql;
        }

        /// <summary>
        /// Compiles a MERGE statement for a target provider.
        /// </summary>
        public string CompileMerge(MergeStatement m, string d)
        {
            var sql = $"MERGE INTO {m.TargetTable.TableName} AS T";
            
            sql += $" USING {m.SourceTable.ToSql()}";
            if (m.SourceTable.Alias == null) sql += " AS S";

            sql += $" ON {CompileExpression(m.OnCondition, d)}";

            foreach (var clause in m.MatchedClauses)
            {
                sql += "\n WHEN MATCHED";
                if (clause.Condition != null) sql += " AND " + CompileExpression(clause.Condition, d);
                sql += " THEN " + CompileMergeAction(clause, d);
            }

            foreach (var clause in m.NotMatchedByTargetClauses)
            {
                sql += "\n WHEN NOT MATCHED";
                if (clause.Condition != null) sql += " AND " + CompileExpression(clause.Condition, d);
                sql += " THEN " + CompileMergeAction(clause, d);
            }

            foreach (var clause in m.NotMatchedBySourceClauses)
            {
                sql += "\n WHEN NOT MATCHED BY SOURCE";
                if (clause.Condition != null) sql += " AND " + CompileExpression(clause.Condition, d);
                sql += " THEN " + CompileMergeAction(clause, d);
            }

            return sql + ";";
        }

        private string CompileMergeAction(MergeActionClause clause, string d)
        {
            switch (clause.ActionType)
            {
                case MergeActionType.UPDATE:
                    return "UPDATE SET " + string.Join(", ", clause.UpdateAssignments!.Select(a => $"{a.ColumnName} = {CompileExpression(a.Value, d)}"));
                case MergeActionType.INSERT:
                    var cols = clause.InsertColumns != null ? "(" + string.Join(", ", clause.InsertColumns) + ")" : "";
                    return $"INSERT {cols} VALUES (" + string.Join(", ", clause.InsertValues!.Select(v => CompileExpression(v, d))) + ")";
                case MergeActionType.DELETE:
                    return "DELETE";
                default:
                    return "";
            }
        }
    }
}
