using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Quality;

namespace ETL_SQL.Analysis.Linting.Rules;

/// <summary>
/// DQ-1: catches data-quality rules written the old way — as <c>@expect</c>/<c>@fail</c> comment
/// tags — and reports them as Errors pointing at the <c>EXPECT</c> clause.
/// <para>
/// The rule grammar itself is no longer this rule's job: rules are parsed with the statement, so a
/// malformed rule, an invalid or ReDoS-prone regex, a <c>UNIQUE_FIRST</c> without <c>BY</c>, or a
/// <c>CASTABLE AS</c> type the engine cannot convert are all syntax errors with a position, for
/// every caller, before any linter runs. What a parser cannot catch is a rule that never became
/// grammar at all: <c>/* @expect: 'NOT NULL'; */</c> still lexes as an ordinary comment tag, so it
/// would sit in a script looking enforced while doing nothing. That is exactly the silent failure
/// moving rules out of comments was meant to end, so it is an Error rather than a warning.
/// </para>
/// </summary>
public class ColumnRuleValidationRule : ILintRule
{
    public string Name => "ColumnRule";

    public string Description =>
        "Rejects data-quality rules written as @expect/@fail comment tags; rules are EXPECT clauses.";

    public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
    {
        var results = new List<LintResult>();
        foreach (var stmt in script.Statements)
            AnalyzeStatement(stmt, results);
        return Task.FromResult<IEnumerable<LintResult>>(results);
    }

    private void AnalyzeStatement(Statement? stmt, List<LintResult> results)
    {
        if (stmt is null) return;

        switch (stmt)
        {
            case SelectStatement select:
                AnalyzeSelect(select, results);
                break;
            case InsertStatement { SelectQuery: not null } insert:
                AnalyzeStatement(insert.SelectQuery, results);
                break;
            case CreateDatasetStatement dataset:
                AnalyzeStatement(dataset.SourceQuery, results);
                break;
            case SetOperationStatement setOp:
                foreach (var cte in setOp.Ctes ?? []) AnalyzeStatement(cte.Query, results);
                AnalyzeStatement(setOp.Left, results);
                AnalyzeStatement(setOp.Right, results);
                break;
            case BlockStatement block:
                foreach (var s in block.Statements ?? []) AnalyzeStatement(s, results);
                break;
            case IfStatement ifStmt:
                AnalyzeStatement(ifStmt.IfBody, results);
                foreach (var ei in ifStmt.ElseIfClauses ?? []) AnalyzeStatement(ei.Body, results);
                if (ifStmt.ElseBody != null) AnalyzeStatement(ifStmt.ElseBody, results);
                break;
            case WhileStatement w:
                AnalyzeStatement(w.Body, results);
                break;
            case ForStatement f:
                AnalyzeStatement(f.Body, results);
                break;
            case ForeachStatement fe:
                AnalyzeStatement(fe.Body, results);
                break;
            case TryCatchStatement tc:
                AnalyzeStatement(tc.TryBody, results);
                AnalyzeStatement(tc.CatchBody, results);
                break;
        }
    }

    private void AnalyzeSelect(SelectStatement select, List<LintResult> results)
    {
        foreach (var column in select.Columns)
        {
            var legacy = column.Metadata?.Keys.Where(ColumnRuleParser.IsRuleTagKey).ToList();
            if (legacy is not { Count: > 0 }) continue;

            var suggestion = SuggestClause(column);
            results.Add(new LintResult
            {
                RuleName = Name,
                Severity = LintSeverity.Error,
                Message =
                    $"'@{string.Join("', '@", legacy.OrderBy(k => k))}' declares a data-quality rule in a "
                    + "comment, where nothing enforces it. Rules are grammar: write "
                    + $"{suggestion} on the column instead.",
                LineNumber = column.Line,
                ColumnNumber = column.Column,
            });
        }

        if (select.FromTable?.Subquery != null) AnalyzeStatement(select.FromTable.Subquery, results);
        foreach (var join in select.Joins)
        {
            if (join.Table?.Subquery != null) AnalyzeStatement(join.Table.Subquery, results);
            AnalyzeExpression(join.Condition, results);
        }
        foreach (var col in select.Columns) AnalyzeExpression(col.Expression, results);
        AnalyzeExpression(select.WhereClause, results);
        foreach (var g in select.GroupBy ?? []) AnalyzeExpression(g, results);
        AnalyzeExpression(select.HavingClause, results);
        AnalyzeExpression(select.QualifyClause, results);
        foreach (var cte in select.Ctes ?? [])
            AnalyzeStatement(cte.Query, results);
    }

    private void AnalyzeExpression(Expression? expr, List<LintResult> results)
    {
        switch (expr)
        {
            case SubqueryExpression subq: AnalyzeStatement(subq.Query, results); break;
            case ExistsExpression exists: AnalyzeStatement(exists.Subquery, results); break;
            case InExpression inExpr:
                AnalyzeExpression(inExpr.Left, results);
                AnalyzeExpression(inExpr.Right, results);
                break;
            case BinaryExpression bin:
                AnalyzeExpression(bin.Left, results);
                AnalyzeExpression(bin.Right, results);
                break;
            case UnaryExpression un:
                AnalyzeExpression(un.Expression, results);
                break;
            case FunctionCallExpression func:
                foreach (var arg in func.Arguments) AnalyzeExpression(arg, results);
                AnalyzeExpression(func.Filter, results);
                break;
            case CaseExpression c:
                AnalyzeExpression(c.InputExpression, results);
                foreach (var w in c.WhenClauses)
                {
                    AnalyzeExpression(w.Condition, results);
                    AnalyzeExpression(w.Result, results);
                }
                AnalyzeExpression(c.ElseResult, results);
                break;
            case BetweenExpression b:
                AnalyzeExpression(b.Left, results);
                AnalyzeExpression(b.Start, results);
                AnalyzeExpression(b.End, results);
                break;
            case IsNullExpression n: AnalyzeExpression(n.Expression, results); break;
            case IsDistinctFromExpression d:
                AnalyzeExpression(d.Left, results);
                AnalyzeExpression(d.Right, results);
                break;
            case LikeExpression l:
                AnalyzeExpression(l.Left, results);
                AnalyzeExpression(l.Pattern, results);
                AnalyzeExpression(l.EscapeChar, results);
                break;
            case ListExpression list:
                foreach (var item in list.Items) AnalyzeExpression(item, results);
                break;
            case AtTimeZoneExpression atz:
                AnalyzeExpression(atz.Left, results);
                AnalyzeExpression(atz.TimeZone, results);
                break;
        }
    }

    /// <summary>
    /// Rebuilds the clause the author meant from the tag they wrote, so the fix is a copy rather
    /// than a lookup. Falls back to the generic shape when the tag value cannot be read.
    /// </summary>
    private static string SuggestClause(SelectColumn column)
    {
        var metadata = column.Metadata;
        if (metadata != null &&
            metadata.TryGetValue("expect", out var expect) &&
            !string.IsNullOrWhiteSpace(expect))
        {
            var rules = ColumnRuleParser.Unquote(expect).Replace(", ", " AND ");
            var clause = $"EXPECT {rules}";
            if (metadata.TryGetValue("fail", out var fail) && !string.IsNullOrWhiteSpace(fail))
                clause += $" ON FAILURE {ColumnRuleParser.Unquote(fail).ToUpperInvariant()}";
            return $"'{clause}'";
        }
        return "'EXPECT <rule> [ON FAILURE THROW | WARN | QUARANTINE]'";
    }
}
