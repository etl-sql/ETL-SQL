using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Dialects;

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
        var dialect = SqlDialectRegistry.GetDialect(d);
        var sql = CompileExpressionInternal(e, dialect);
        return new CompiledSql(sql, _currentParams);
    }

    private string CompileExpressionInternal(Expression e, ISqlDialect dialect)
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
            return dialect.RewriteIdentifier(id.Name);
        }
        if (e is LiteralExpression lit)
        {
            var pName = "@p" + _paramCounter++;
            _currentParams[pName] = lit.Value;
            return pName;
        }
        if (e is BinaryExpression bin)
        {
            if (bin.Operator == TokenType.CONCAT)
            {
                var left = CompileExpressionInternal(bin.Left, dialect);
                var right = CompileExpressionInternal(bin.Right, dialect);
                return $"({dialect.FormatStringConcat(left, right)})";
            }

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
            return $"({CompileExpressionInternal(bin.Left, dialect)} {op} {CompileExpressionInternal(bin.Right, dialect)})";
        }
        if (e is FunctionCallExpression f)
        {
            return dialect.RewriteFunctionCall(f.FunctionName, f.Arguments, arg => CompileExpressionInternal(arg, dialect));
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
                return $"{CompileExpressionInternal(inExp.Left, dialect)} {not}IN ({CompileQueryInternal(inExp.Subquery, dialect)})";
            }
            return $"{CompileExpressionInternal(inExp.Left, dialect)} {not}IN {CompileExpressionInternal(inExp.Right, dialect)}";
        }
        if (e is ListExpression list)
        {
            return "(" + string.Join(", ", list.Items.Select(item => CompileExpressionInternal(item, dialect))) + ")";
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
        var dialect = SqlDialectRegistry.GetDialect(d);
        var sql = CompileQueryInternal(s, dialect);
        return new CompiledSql(sql, _currentParams);
    }

    private string CompileQueryInternal(Statement s, ISqlDialect dialect)
    {
        if (s is SelectStatement sel)
        {
            var selectParts = new List<string>();
            if (sel.TopCount != null && dialect.SupportsTop)
            {
                selectParts.Add(dialect.FormatTop(CompileExpressionInternal(sel.TopCount, dialect), sel.IsTopPercent, sel.WithTies));
            }

            if (sel.IsDistinct) selectParts.Add("DISTINCT");

            var cols = (sel.Columns.Count == 1 && sel.Columns[0].Expression is IdentifierExpression id && id.Name == "*")
                ? "*"
                : string.Join(", ", sel.Columns.Select(c => CompileExpressionInternal(c.Expression, dialect) + (c.Alias != null ? $" AS {c.Alias}" : "")));
            selectParts.Add(cols);

            var sql = "SELECT " + string.Join(" ", selectParts);

            if (sel.FromTable != null)
            {
                sql += $" FROM {CompileTableReferenceInternal(sel.FromTable, dialect)}";
            }

            if (sel.Joins != null)
            {
                foreach (var join in sel.Joins)
                {
                    var jt = join.JoinType;
                    if (!jt.Contains("JOIN") && !jt.Contains("APPLY")) jt += " JOIN";
                    sql += $" {jt} {CompileTableReferenceInternal(join.Table, dialect)}";
                    if (!join.IsApply) sql += $" ON {CompileExpressionInternal(join.Condition, dialect)}";
                }
            }

            if (sel.WhereClause != null) sql += " WHERE " + CompileExpressionInternal(sel.WhereClause, dialect);

            if (sel.GroupBy != null && sel.GroupBy.Count > 0)
            {
                sql += " GROUP BY " + string.Join(", ", sel.GroupBy.Select(g => CompileExpressionInternal(g, dialect)));
            }

            if (sel.HavingClause != null) sql += " HAVING " + CompileExpressionInternal(sel.HavingClause, dialect);

            if (sel.OrderBy != null && sel.OrderBy.Count > 0)
            {
                sql += " ORDER BY " + string.Join(", ", sel.OrderBy.Select(o => CompileExpressionInternal(o.Expression, dialect) + (o.Descending ? " DESC" : " ASC")));
            }

            string? offset = sel.Offset != null ? CompileExpressionInternal(sel.Offset, dialect) : null;
            string? limit = sel.LimitCount != null ? CompileExpressionInternal(sel.LimitCount, dialect) : null;
            sql += dialect.FormatOffsetLimit(offset, limit);

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
            return $"{CompileQueryInternal(setOp.Left, dialect)} {op} {CompileQueryInternal(setOp.Right, dialect)}";
        }
        if (s is MergeStatement m)
        {
            return CompileMergeInternal(m, dialect);
        }
        return s.ToSql();
    }

    private string CompileTableReferenceInternal(TableReference t, ISqlDialect dialect)
    {
        if (t == null) return "";
        string sql;
        if (t.Subquery != null)
        {
            sql = $"({CompileQueryInternal(t.Subquery, dialect)})";
        }
        else if (t.FunctionCall != null)
        {
            var args = string.Join(", ", t.FunctionCall.Arguments.Select(a => CompileExpressionInternal(a, dialect)));
            sql = $"{t.FunctionCall.FunctionName}({args})";
        }
        else
        {
            sql = _evaluator.GetSqlTableName(t, dialect.Name);
        }

        if (t.Alias != null)
        {
            sql += dialect.FormatTableAlias(t.Alias);
        }
        return sql;
    }

    private string CompileMergeInternal(MergeStatement m, ISqlDialect dialect)
    {
        var targetTable = _evaluator.GetSqlTableName(m.TargetTable, dialect.Name);
        var sql = $"MERGE INTO {targetTable}{dialect.FormatTableAlias("T")}";

        sql += $" USING {CompileTableReferenceInternal(m.SourceTable, dialect)}";

        // If the source table reference didn't have an alias, we explicitly add one for the ON clause (S and T are standard)
        if (m.SourceTable.Alias == null)
        {
            sql += dialect.FormatTableAlias("S");
        }

        sql += $" ON {CompileExpressionInternal(m.OnCondition, dialect)}";

        foreach (var clause in m.MatchedClauses)
        {
            sql += "\n WHEN MATCHED";
            if (clause.Condition != null) sql += " AND " + CompileExpressionInternal(clause.Condition, dialect);
            sql += " THEN " + CompileMergeActionInternal(clause, dialect);
        }

        foreach (var clause in m.NotMatchedClauses.Where(c => c.Option == MergeSourceOrTarget.Target))
        {
            sql += "\n WHEN NOT MATCHED";
            if (clause.Condition != null) sql += " AND " + CompileExpressionInternal(clause.Condition, dialect);
            sql += " THEN " + CompileMergeActionInternal(clause, dialect);
        }

        foreach (var clause in m.NotMatchedClauses.Where(c => c.Option == MergeSourceOrTarget.Source))
        {
            sql += "\n WHEN NOT MATCHED BY SOURCE";
            if (clause.Condition != null) sql += " AND " + CompileExpressionInternal(clause.Condition, dialect);
            sql += " THEN " + CompileMergeActionInternal(clause, dialect);
        }

        return sql + ";";
    }

    private string CompileMergeActionInternal(MergeActionClause clause, ISqlDialect dialect)
    {
        switch (clause.ActionType)
        {
            case MergeActionType.UPDATE:
                return "UPDATE SET " + string.Join(", ", clause.UpdateAssignments!.Select(a => $"{a.ColumnName} = {CompileExpressionInternal(a.Value, dialect)}"));
            case MergeActionType.INSERT:
                var cols = clause.InsertColumns != null ? "(" + string.Join(", ", clause.InsertColumns) + ")" : "";
                return $"INSERT {cols} VALUES (" + string.Join(", ", clause.InsertValues!.Select(v => CompileExpressionInternal(v, dialect))) + ")";
            case MergeActionType.DELETE:
                return "DELETE";
            default:
                return "";
        }
    }
}
