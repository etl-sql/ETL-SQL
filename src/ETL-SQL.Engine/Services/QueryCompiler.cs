using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Engine.Services;
/// <summary>
/// Encapsulates the logic for compiling ETL-SQL AST nodes (expressions, queries) 
/// back into provider-specific SQL strings.
/// </summary>
public class QueryCompiler(Evaluator evaluator)
{
    private readonly Evaluator _evaluator = evaluator;
    private int _paramCounter = 0;
    private Dictionary<string, object?> _currentParams = new();

    /// <summary>
    /// Compiles a scalar expression back into a provider-specific SQL string.
    /// </summary>
    public CompiledSql CompileExpression(Expression e, string d = "MSSQL")
    {
        _paramCounter = 0;
        _currentParams = new Dictionary<string, object?>();
        var sql = CompileExpressionInternal(e, d);
        return new CompiledSql(sql, _currentParams);
    }

    private string CompileExpressionInternal(Expression e, string d)
    {
        if (e is IdentifierExpression id)
        {
            if (id.Name.StartsWith("@"))
            {
                var val = _evaluator.GetVariable(id.Name);
                var pName = "@p" + _paramCounter++;
                _currentParams[pName] = val;
                return pName;
            }

            var name = id.Name.ToUpperInvariant();
            if (d.Equals("MSSQL", StringComparison.OrdinalIgnoreCase))
            {
                if (name == "SYSDATE") return "GETDATE()";
                if (name == "NOW") return "GETDATE()";
            }
            else if (d.Equals("POSTGRES", StringComparison.OrdinalIgnoreCase))
            {
                if (name == "SYSDATE") return "NOW()";
                if (name == "GETDATE") return "NOW()";
            }
            else if (d.Equals("ORACLE", StringComparison.OrdinalIgnoreCase))
            {
                if (name == "NOW") return "SYSDATE";
                if (name == "GETDATE") return "SYSDATE";
            }

            return id.Name;
        }
        if (e is LiteralExpression lit)
        {
            var pName = "@p" + _paramCounter++;
            _currentParams[pName] = lit.Value;
            return pName;
        }
        if (e is BinaryExpression bin)
        {
            var op = bin.Operator switch
            {
                TokenType.EQUALS => "=",
                TokenType.NOT_EQUALS => "!=",
                TokenType.LESS_THAN => "<",
                TokenType.GREATER_THAN => ">",
                TokenType.LESS_EQUALS => "<=",
                TokenType.GREATER_EQUALS => ">=",
                TokenType.PLUS => "+",
                TokenType.MINUS => "-",
                TokenType.STAR => "*",
                TokenType.SLASH => "/",
                TokenType.LSHIFT => "<<",
                TokenType.RSHIFT => ">>",
                _ => bin.Operator.ToString()
            };
            return $"({CompileExpressionInternal(bin.Left, d)} {op} {CompileExpressionInternal(bin.Right, d)})";
        }
        if (e is FunctionCallExpression f)
        {
            var funcName = f.FunctionName.ToUpperInvariant();

            // Dialect-specific function rewriting
            if (d.Equals("MSSQL", StringComparison.OrdinalIgnoreCase))
            {
                if (funcName == "SYSDATE")
                {
                    return "GETDATE()";
                }
                if (funcName == "NOW")
                {
                    return "GETDATE()";
                }
                if (funcName == "TRUNC" && f.Arguments.Count == 1)
                {
                    var arg = CompileExpressionInternal(f.Arguments[0], d);
                    return $"CAST({arg} AS DATE)";
                }
                if (funcName == "TRUNC" && f.Arguments.Count == 2)
                {
                    var arg = CompileExpressionInternal(f.Arguments[0], d);
                    var part = f.Arguments[1] is LiteralExpression litVal && litVal.Value != null ? (litVal.Value.ToString() ?? "") : "";
                    if (part.Equals("MM", StringComparison.OrdinalIgnoreCase) || part.Equals("MONTH", StringComparison.OrdinalIgnoreCase))
                    {
                        return $"DATEADD(month, DATEDIFF(month, 0, {arg}), 0)";
                    }
                    if (part.Equals("YY", StringComparison.OrdinalIgnoreCase) || part.Equals("YEAR", StringComparison.OrdinalIgnoreCase))
                    {
                        return $"DATEADD(year, DATEDIFF(year, 0, {arg}), 0)";
                    }
                }
            }
            else if (d.Equals("POSTGRES", StringComparison.OrdinalIgnoreCase))
            {
                if (funcName == "SYSDATE")
                {
                    return "NOW()";
                }
                if (funcName == "GETDATE")
                {
                    return "NOW()";
                }
                if (funcName == "TRUNC" && f.Arguments.Count == 1)
                {
                    var arg = CompileExpressionInternal(f.Arguments[0], d);
                    return $"DATE_TRUNC('day', {arg})";
                }
                if (funcName == "TRUNC" && f.Arguments.Count == 2)
                {
                    var arg = CompileExpressionInternal(f.Arguments[0], d);
                    var part = f.Arguments[1] is LiteralExpression litVal2 && litVal2.Value != null ? (litVal2.Value.ToString() ?? "day") : "day";
                    return $"DATE_TRUNC('{part}', {arg})";
                }
            }
            else if (d.Equals("ORACLE", StringComparison.OrdinalIgnoreCase))
            {
                if (funcName == "NOW")
                {
                    return "SYSDATE";
                }
                if (funcName == "GETDATE")
                {
                    return "SYSDATE";
                }
            }

            if (f.FunctionName.Equals("CAST", StringComparison.OrdinalIgnoreCase) ||
                f.FunctionName.Equals("TRY_CAST", StringComparison.OrdinalIgnoreCase))
            {
                if (f.Arguments.Count >= 2)
                {
                    var castExpr = CompileExpressionInternal(f.Arguments[0], d);
                    string typeStr = "";
                    if (f.Arguments[1] is LiteralExpression litType)
                    {
                        typeStr = litType.Value?.ToString() ?? "";
                    }
                    else
                    {
                        typeStr = CompileExpressionInternal(f.Arguments[1], d);
                    }
                    return $"{f.FunctionName.ToUpperInvariant()}({castExpr} AS {typeStr})";
                }
            }
            var args = string.Join(", ", f.Arguments.Select(a => CompileExpressionInternal(a, d)));
            return $"{f.FunctionName}({args})";
        }
        if (e is VariableExpression v)
        {
            var val = _evaluator.GetVariable(v.Name);
            var pName = "@p" + _paramCounter++;
            _currentParams[pName] = val;
            return pName;
        }
        if (e is InExpression inExp)
        {
            var not = inExp.IsNot ? "NOT " : "";
            if (inExp.Subquery != null)
            {
                return $"{CompileExpressionInternal(inExp.Left, d)} {not}IN ({CompileQueryInternal(inExp.Subquery, d)})";
            }
            return $"{CompileExpressionInternal(inExp.Left, d)} {not}IN {CompileExpressionInternal(inExp.Right, d)}";
        }
        if (e is ListExpression list)
        {
            return "(" + string.Join(", ", list.Items.Select(item => CompileExpressionInternal(item, d))) + ")";
        }
        return e?.ToSql() ?? "";
    }

    /// <summary>
    /// Compiles a full SELECT or MERGE statement back into a provider-specific SQL string.
    /// </summary>
    public CompiledSql CompileQuery(Statement s, string d = "MSSQL")
    {
        _paramCounter = 0;
        _currentParams = new Dictionary<string, object?>();
        var sql = CompileQueryInternal(s, d);
        return new CompiledSql(sql, _currentParams);
    }

    private string CompileQueryInternal(Statement s, string d)
    {
        if (s is SelectStatement sel)
        {
            var selectParts = new List<string>();
            if (sel.TopCount != null && d == "MSSQL")
            {
                var percent = sel.IsTopPercent ? " PERCENT" : "";
                var ties = sel.WithTies ? " WITH TIES" : "";
                selectParts.Add($"TOP ({CompileExpressionInternal(sel.TopCount, d)}){percent}{ties}");
            }

            if (sel.IsDistinct) selectParts.Add("DISTINCT");

            var cols = (sel.Columns.Count == 1 && sel.Columns[0].Expression is IdentifierExpression id && id.Name == "*")
                ? "*"
                : string.Join(", ", sel.Columns.Select(c => CompileExpressionInternal(c.Expression, d) + (c.Alias != null ? $" AS {c.Alias}" : "")));
            selectParts.Add(cols);

            var sql = "SELECT " + string.Join(" ", selectParts);

            if (sel.FromTable != null)
            {
                sql += $" FROM {CompileTableReferenceInternal(sel.FromTable, d)}";
            }

            if (sel.Joins != null)
            {
                foreach (var join in sel.Joins)
                {
                    var jt = join.JoinType;
                    if (!jt.Contains("JOIN") && !jt.Contains("APPLY")) jt += " JOIN";
                    sql += $" {jt} {CompileTableReferenceInternal(join.Table, d)}";
                    if (!join.IsApply) sql += $" ON {CompileExpressionInternal(join.Condition, d)}";
                }
            }

            if (sel.WhereClause != null) sql += " WHERE " + CompileExpressionInternal(sel.WhereClause, d);

            if (sel.GroupBy != null && sel.GroupBy.Count > 0)
            {
                sql += " GROUP BY " + string.Join(", ", sel.GroupBy.Select(g => CompileExpressionInternal(g, d)));
            }

            if (sel.HavingClause != null) sql += " HAVING " + CompileExpressionInternal(sel.HavingClause, d);

            if (sel.OrderBy != null && sel.OrderBy.Count > 0)
            {
                sql += " ORDER BY " + string.Join(", ", sel.OrderBy.Select(o => CompileExpressionInternal(o.Expression, d) + (o.Descending ? " DESC" : " ASC")));
            }

            if (sel.Offset != null)
            {
                if (d.Equals("MSSQL", StringComparison.OrdinalIgnoreCase))
                {
                    sql += $" OFFSET {CompileExpressionInternal(sel.Offset, d)} ROWS";
                    if (sel.LimitCount != null) sql += $" FETCH NEXT {CompileExpressionInternal(sel.LimitCount, d)} ROWS ONLY";
                }
                else
                {
                    sql += $" OFFSET {CompileExpressionInternal(sel.Offset, d)}";
                    if (sel.LimitCount != null && !d.Equals("ORACLE", StringComparison.OrdinalIgnoreCase))
                    {
                        sql += $" LIMIT {CompileExpressionInternal(sel.LimitCount, d)}";
                    }
                }
            }
            else if (sel.LimitCount != null)
            {
                if (!d.Equals("MSSQL", StringComparison.OrdinalIgnoreCase) && !d.Equals("ORACLE", StringComparison.OrdinalIgnoreCase))
                {
                    sql += $" LIMIT {CompileExpressionInternal(sel.LimitCount, d)}";
                }
            }

            return sql;
        }
        if (s is SetOperationStatement setOp)
        {
            string op = setOp.Operation switch
            {
                SetOpType.UNION => "UNION",
                SetOpType.UNION_ALL => "UNION ALL",
                SetOpType.EXCEPT => "EXCEPT",
                SetOpType.INTERSECT => "INTERSECT",
                _ => "UNION"
            };
            return $"{CompileQueryInternal(setOp.Left, d)} {op} {CompileQueryInternal(setOp.Right, d)}";
        }
        if (s is MergeStatement m)
        {
            return CompileMergeInternal(m, d);
        }
        return s.ToSql();
    }

    private string CompileTableReferenceInternal(TableReference t, string d)
    {
        if (t == null) return "";
        string sql;
        if (t.Subquery != null)
        {
            sql = $"({CompileQueryInternal(t.Subquery, d)})";
        }
        else if (t.FunctionCall != null)
        {
            var args = string.Join(", ", t.FunctionCall.Arguments.Select(a => CompileExpressionInternal(a, d)));
            sql = $"{t.FunctionCall.FunctionName}({args})";
        }
        else
        {
            sql = _evaluator.GetSqlTableName(t, d);
        }

        if (t.Alias != null)
        {
            if (d.Equals("ORACLE", StringComparison.OrdinalIgnoreCase))
                sql += " " + t.Alias;
            else
                sql += " AS " + t.Alias;
        }
        return sql;
    }

    private string CompileMergeInternal(MergeStatement m, string d)
    {
        var targetTable = _evaluator.GetSqlTableName(m.TargetTable, d);
        var sql = d.Equals("ORACLE", StringComparison.OrdinalIgnoreCase)
            ? $"MERGE INTO {targetTable} T"
            : $"MERGE INTO {targetTable} AS T";

        sql += $" USING {CompileTableReferenceInternal(m.SourceTable, d)}";

        // If the source table reference didn't have an alias, we explicitly add one for the ON clause (S and T are standard)
        if (m.SourceTable.Alias == null)
        {
            sql += d.Equals("ORACLE", StringComparison.OrdinalIgnoreCase) ? " S" : " AS S";
        }

        sql += $" ON {CompileExpressionInternal(m.OnCondition, d)}";

        foreach (var clause in m.MatchedClauses)
        {
            sql += "\n WHEN MATCHED";
            if (clause.Condition != null) sql += " AND " + CompileExpressionInternal(clause.Condition, d);
            sql += " THEN " + CompileMergeActionInternal(clause, d);
        }

        foreach (var clause in m.NotMatchedClauses.Where(c => c.Option == MergeSourceOrTarget.Target))
        {
            sql += "\n WHEN NOT MATCHED";
            if (clause.Condition != null) sql += " AND " + CompileExpressionInternal(clause.Condition, d);
            sql += " THEN " + CompileMergeActionInternal(clause, d);
        }

        foreach (var clause in m.NotMatchedClauses.Where(c => c.Option == MergeSourceOrTarget.Source))
        {
            sql += "\n WHEN NOT MATCHED BY SOURCE";
            if (clause.Condition != null) sql += " AND " + CompileExpressionInternal(clause.Condition, d);
            sql += " THEN " + CompileMergeActionInternal(clause, d);
        }

        return sql + ";";
    }

    private string CompileMergeActionInternal(MergeActionClause clause, string d)
    {
        switch (clause.ActionType)
        {
            case MergeActionType.UPDATE:
                return "UPDATE SET " + string.Join(", ", clause.UpdateAssignments!.Select(a => $"{a.ColumnName} = {CompileExpressionInternal(a.Value, d)}"));
            case MergeActionType.INSERT:
                var cols = clause.InsertColumns != null ? "(" + string.Join(", ", clause.InsertColumns) + ")" : "";
                return $"INSERT {cols} VALUES (" + string.Join(", ", clause.InsertValues!.Select(v => CompileExpressionInternal(v, d))) + ")";
            case MergeActionType.DELETE:
                return "DELETE";
            default:
                return "";
        }
    }
}
