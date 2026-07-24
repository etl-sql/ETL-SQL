using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Quality;

namespace ETL_SQL.Analysis.Linting.Rules;

/// <summary>
/// DQ-1: Runs the real <see cref="ColumnRuleParser"/> over every column carrying
/// <c>@expect</c>/<c>@fail</c> tags and reports parse failures as Errors — malformed rules,
/// invalid or NonBacktracking-incompatible regexes, <c>UNIQUE_FIRST/LAST</c> without <c>BY</c>,
/// unknown actions, and <c>@fail</c> keys without a matching <c>@expect</c>. Malformed rules are
/// hard errors, never silently ignored (design decision 5). Complements
/// <see cref="TagValueValidationRule"/>, which covers only catalog value kinds.
/// </summary>
public class ColumnRuleValidationRule : ILintRule
{
    public string Name => "ColumnRule";
    public string Description => "Validates @expect/@fail data-quality rule tags against the rule grammar.";

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
            if (column.Metadata == null || !ColumnRuleParser.HasRuleTags(column.Metadata)) continue;
            try
            {
                ColumnRuleParser.ParseBindings(column.Metadata);
            }
            catch (ColumnRuleParseException ex)
            {
                results.Add(new LintResult
                {
                    RuleName = Name,
                    Severity = LintSeverity.Error,
                    Message = ex.Message,
                    LineNumber = column.Line,
                    ColumnNumber = column.Column,
                });
            }
        }

        if (select.FromTable?.Subquery != null) AnalyzeStatement(select.FromTable.Subquery, results);
        foreach (var join in select.Joins)
            if (join.Table?.Subquery != null) AnalyzeStatement(join.Table.Subquery, results);
        foreach (var cte in select.Ctes ?? [])
            AnalyzeStatement(cte.Query, results);
    }
}
