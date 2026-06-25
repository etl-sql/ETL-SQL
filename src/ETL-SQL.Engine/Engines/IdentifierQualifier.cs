using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Engines;
/// <summary>
/// Rewrites unqualified identifier references in the WHERE clause of a SELECT statement,
/// turning bare names (e.g. <c>e8</c>) into table-qualified names (e.g. <c>t8.e8</c>)
/// by consulting each table's column schema.
///
/// This runs before <see cref="CrossJoinPredicatePushdown"/>, enabling the pushdown
/// optimizer to recognize column ownership in unqualified comma-join predicates.
///
/// Columns that appear in more than one source table are left unqualified (ambiguous).
/// Tables whose schemas cannot be resolved (e.g. subqueries, VALUES) are skipped.
/// </summary>
internal static class IdentifierQualifier
{
    public static async Task<SelectStatement> QualifyAsync(SelectStatement stmt, IExecutionContext context)
    {
        if (stmt.WhereClause == null) return stmt;
        if (stmt.Joins == null || stmt.Joins.Count == 0) return stmt;

        // Build bare-name → table-alias map. Skip ambiguous names (same bare col in 2+ tables).
        var colToAlias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await TryAddTableColumns(stmt.FromTable, context, colToAlias, ambiguous);
        foreach (var join in stmt.Joins)
            await TryAddTableColumns(join.Table, context, colToAlias, ambiguous);

        if (colToAlias.Count == 0) return stmt;

        var newWhere = QualifyExpression(stmt.WhereClause, colToAlias);
        if (ReferenceEquals(newWhere, stmt.WhereClause)) return stmt;

        return stmt with { WhereClause = newWhere };
    }

    private static async Task TryAddTableColumns(
        TableReference table,
        IExecutionContext context,
        Dictionary<string, string> colToAlias,
        HashSet<string> ambiguous)
    {
        // Subqueries and VALUES rows have no static schema we can consult cheaply.
        if (table.Subquery != null || table.ValuesRows != null) return;

        string alias = table.Alias ?? table.TableName;

        IEnumerable<string> columns;
        try
        {
            var ds = await context.ResolveDataSourceAsync(table);
            columns = await ds.GetColumnsAsync();
        }
        catch
        {
            return;
        }

        foreach (var col in columns)
        {
            if (ambiguous.Contains(col)) continue;

            if (colToAlias.ContainsKey(col))
            {
                // Same bare name exists in a prior table — mark ambiguous and remove.
                ambiguous.Add(col);
                colToAlias.Remove(col);
            }
            else
            {
                colToAlias[col] = alias;
            }
        }
    }

    private static Expression QualifyExpression(Expression expr, Dictionary<string, string> colToAlias)
    {
        switch (expr)
        {
            case IdentifierExpression id when !id.Name.Contains('.'):
                if (colToAlias.TryGetValue(id.Name, out var tableAlias))
                    return new IdentifierExpression($"{tableAlias}.{id.Name}");
                return expr;

            case BinaryExpression bin:
                {
                    var l = QualifyExpression(bin.Left, colToAlias);
                    var r = QualifyExpression(bin.Right, colToAlias);
                    return ReferenceEquals(l, bin.Left) && ReferenceEquals(r, bin.Right)
                        ? expr
                        : new BinaryExpression(l, bin.Operator, r);
                }

            case UnaryExpression un:
                {
                    var inner = QualifyExpression(un.Expression, colToAlias);
                    return ReferenceEquals(inner, un.Expression)
                        ? expr
                        : new UnaryExpression(un.Operator, inner);
                }

            case FunctionCallExpression fn:
                {
                    bool changed = false;
                    var newArgs = new List<Expression>(fn.Arguments.Count);
                    foreach (var arg in fn.Arguments)
                    {
                        var q = QualifyExpression(arg, colToAlias);
                        newArgs.Add(q);
                        if (!ReferenceEquals(q, arg)) changed = true;
                    }
                    if (!changed) return expr;
                    return new FunctionCallExpression(fn.FunctionName, newArgs)
                    {
                        IsDistinct = fn.IsDistinct,
                        Window = fn.Window,
                        WithinGroupOrderBy = fn.WithinGroupOrderBy,
                        Filter = fn.Filter,
                        JsonTable = fn.JsonTable,
                    };
                }

            case InExpression inExpr when inExpr.Subquery == null:
                {
                    var left = QualifyExpression(inExpr.Left, colToAlias);
                    var right = QualifyExpression(inExpr.Right, colToAlias);
                    return ReferenceEquals(left, inExpr.Left) && ReferenceEquals(right, inExpr.Right)
                        ? expr
                        : new InExpression(left, right, inExpr.IsNot);
                }

            case BetweenExpression between:
                {
                    var left = QualifyExpression(between.Left, colToAlias);
                    var start = QualifyExpression(between.Start, colToAlias);
                    var end = QualifyExpression(between.End, colToAlias);
                    return ReferenceEquals(left, between.Left) && ReferenceEquals(start, between.Start) && ReferenceEquals(end, between.End)
                        ? expr
                        : new BetweenExpression(left, start, end, between.IsNot);
                }

            case IsNullExpression isNull:
                {
                    var inner = QualifyExpression(isNull.Expression, colToAlias);
                    return ReferenceEquals(inner, isNull.Expression)
                        ? expr
                        : new IsNullExpression(inner, isNull.Not);
                }

            case LikeExpression like:
                {
                    var left = QualifyExpression(like.Left, colToAlias);
                    return ReferenceEquals(left, like.Left)
                        ? expr
                        : new LikeExpression(left, like.Pattern, like.IsNot, like.EscapeChar, like.IsCaseInsensitive);
                }

            case CaseExpression caseExpr:
                {
                    bool changed = false;
                    var newWhens = new List<(Expression Condition, Expression Result)>(caseExpr.WhenClauses.Count);
                    foreach (var (cond, result) in caseExpr.WhenClauses)
                    {
                        var qc = QualifyExpression(cond, colToAlias);
                        var qr = QualifyExpression(result, colToAlias);
                        newWhens.Add((qc, qr));
                        if (!ReferenceEquals(qc, cond) || !ReferenceEquals(qr, result)) changed = true;
                    }
                    var newInput = caseExpr.InputExpression != null ? QualifyExpression(caseExpr.InputExpression, colToAlias) : null;
                    var newElse = caseExpr.ElseResult != null ? QualifyExpression(caseExpr.ElseResult, colToAlias) : null;
                    if (!changed && ReferenceEquals(newInput, caseExpr.InputExpression) && ReferenceEquals(newElse, caseExpr.ElseResult))
                        return expr;
                    return new CaseExpression(newWhens, newElse, newInput);
                }

            default:
                return expr;
        }
    }
}
