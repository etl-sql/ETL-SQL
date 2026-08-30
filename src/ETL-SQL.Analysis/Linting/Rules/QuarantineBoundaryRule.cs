using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Quality;

namespace ETL_SQL.Analysis.Linting.Rules;

/// <summary>
/// DQ-2: Enforces the structural contract around QUARANTINE and the trailing <c>ON FAILURE</c>
/// clauses (design decisions 4, 5, 11):
/// <list type="bullet">
/// <item>QUARANTINE is legal only at a sink/materialization boundary (top-level SELECT,
/// <c>INSERT … SELECT</c>, <c>SELECT … INTO</c>) — Error on nested subquery/CTE/set-operation columns.</item>
/// <item>A column electing <c>ON FAILURE QUARANTINE</c> without a matching statement-level
/// <c>ON FAILURE QUARANTINE TO</c> clause — Error.</item>
/// <item>An <c>ON FAILURE</c> clause elected by no column — Error (the
/// comment-stripping tripwire: a tool that strips comment tags breaks the script loudly).</item>
/// <item>A quarantining statement with no enclosing section label — Error (the label is the
/// v2 replay re-entry point, required from v1).</item>
/// <item>Quarantining to a <c>#temp</c> target — Info (recommend a durable target).</item>
/// <item><c>WARN TO</c> target without a <c>RETENTION</c> option — Info (warn tables have no
/// lifecycle pruning).</item>
/// </list>
/// </summary>
public class QuarantineBoundaryRule : ILintRule
{
    public string Name => "QuarantineBoundary";
    public string Description =>
        "Enforces QUARANTINE sink boundaries, symmetric ON FAILURE clause/rule matching, and section-label requirements.";

    public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
    {
        var results = new List<LintResult>();
        string? currentLabel = null;
        AnalyzeStatements(script.Statements, ref currentLabel, results);
        return Task.FromResult<IEnumerable<LintResult>>(results);
    }

    private void AnalyzeStatements(IEnumerable<Statement?>? statements, ref string? currentLabel, List<LintResult> results)
    {
        foreach (var stmt in statements ?? [])
        {
            if (stmt is SectionLabelStatement label)
            {
                currentLabel = label.LabelName;
                continue;
            }
            AnalyzeStatement(stmt, ref currentLabel, results);
        }
    }

    private void AnalyzeStatement(Statement? stmt, ref string? currentLabel, List<LintResult> results)
    {
        switch (stmt)
        {
            case null:
                return;
            case SelectStatement select:
                AnalyzeSinkSelect(select, currentLabel, results);
                break;
            case InsertStatement { SelectQuery: SelectStatement insertSelect }:
                AnalyzeSinkSelect(insertSelect, currentLabel, results);
                break;
            case InsertStatement { SelectQuery: not null } insert:
                // e.g. INSERT ... SELECT ... UNION ...: set-operation arms are not a single
                // materialization boundary — quarantine inside them is a boundary error.
                CheckNonSinkQuery(insert.SelectQuery, results);
                break;
            case SetOperationStatement setOp:
                CheckNonSinkQuery(setOp, results);
                break;
            case CreateDatasetStatement dataset:
                CheckNonSinkQuery(dataset.SourceQuery, results);
                break;
            case BlockStatement block:
                AnalyzeStatements(block.Statements, ref currentLabel, results);
                break;
            case IfStatement ifStmt:
                AnalyzeStatement(ifStmt.IfBody, ref currentLabel, results);
                foreach (var ei in ifStmt.ElseIfClauses ?? []) AnalyzeStatement(ei.Body, ref currentLabel, results);
                if (ifStmt.ElseBody != null) AnalyzeStatement(ifStmt.ElseBody, ref currentLabel, results);
                break;
            case WhileStatement w:
                AnalyzeStatement(w.Body, ref currentLabel, results);
                break;
            case ForStatement f:
                AnalyzeStatement(f.Body, ref currentLabel, results);
                break;
            case ForeachStatement fe:
                AnalyzeStatement(fe.Body, ref currentLabel, results);
                break;
            case TryCatchStatement tc:
                AnalyzeStatement(tc.TryBody, ref currentLabel, results);
                AnalyzeStatement(tc.CatchBody, ref currentLabel, results);
                break;
        }
    }

    private void AnalyzeSinkSelect(SelectStatement select, string? currentLabel, List<LintResult> results)
    {
        var actions = CollectActions(select, out var hasRules);
        var clauses = select.OnFailureActions ?? [];

        if (actions.Contains(FailAction.Quarantine)
            && !clauses.Any(c => c.Action == FailAction.Quarantine))
        {
            Report(results, LintSeverity.Error, select,
                "A column electing ON FAILURE QUARANTINE requires a matching ON FAILURE QUARANTINE TO <table> clause on the statement — quarantined rows have nowhere else to go.");
        }

        foreach (var clause in clauses)
        {
            if (hasRules && actions.Contains(clause.Action)) continue;
            Report(results, LintSeverity.Error, select,
                $"ON FAILURE {ActionName(clause.Action)} clause is elected by no column in this statement. " +
                "Either a column should declare EXPECT … ON FAILURE " + ActionName(clause.Action) +
                ", or the routing clause should be removed — routing that nothing uses reads as enforcement that is not happening.");
        }

        // Both requirements below exist to serve remediation *after* the run: the label is the
        // replay re-entry point, and a durable target is what lets rows survive to be replayed.
        // HANDLING = SCRIPT says there is no "after" — the script handles the rows now — so
        // demanding either would be asking the author to prepare for a hand-off that never happens.
        bool scriptHandled = clauses.Any(c =>
            c.Action == FailAction.Quarantine && c.Handling == QuarantineHandling.Script);

        bool quarantines = actions.Contains(FailAction.Quarantine)
                           || clauses.Any(c => c.Action == FailAction.Quarantine);
        if (quarantines && !scriptHandled && string.IsNullOrEmpty(currentLabel))
        {
            Report(results, LintSeverity.Error, select,
                "A quarantining statement must sit inside a section label (e.g. 'import_users:') — the label is the replay re-entry point for quarantine remediation.");
        }

        foreach (var clause in clauses)
        {
            if (clause.Action == FailAction.Quarantine
                && clause.Handling == QuarantineHandling.Steward
                && clause.Target!.StartsWith('#'))
            {
                Report(results, LintSeverity.Info, select,
                    $"Quarantine target '{clause.Target}' is a #temp table that evaporates when the run ends — quarantine to a durable table so rows survive for remediation, or declare WITH (HANDLING = SCRIPT) if this run handles them itself.");
            }
            if (clause.Action == FailAction.Warn && clause.Target != null && clause.Retention == null)
            {
                Report(results, LintSeverity.Info, select,
                    $"WARN TO target '{clause.Target}' has no RETENTION option — warn tables have no lifecycle pruning, so set WITH (RETENTION = '30 DAYS') or similar.");
            }
        }

        // Nested queries under this sink are not materialization boundaries.
        if (select.FromTable?.Subquery != null) CheckNonSinkQuery(select.FromTable.Subquery, results);
        foreach (var join in select.Joins)
            if (join.Table?.Subquery != null) CheckNonSinkQuery(join.Table.Subquery, results);
        foreach (var cte in select.Ctes ?? [])
            CheckNonSinkQuery(cte.Query, results);
    }

    /// <summary>Flags any QUARANTINE rule found below a sink boundary and keeps descending.</summary>
    private void CheckNonSinkQuery(Statement? stmt, List<LintResult> results)
    {
        switch (stmt)
        {
            case null:
                return;
            case SelectStatement select:
                {
                    foreach (var column in select.Columns)
                    {
                        foreach (var binding in SafeBindings(column))
                        {
                            if (binding.Action != FailAction.Quarantine) continue;
                            Report(results, LintSeverity.Error, column,
                                "ON FAILURE QUARANTINE is only legal at a sink/materialization boundary (top-level SELECT, INSERT ... SELECT, SELECT ... INTO) — it is a filter with a side effect that would silently change downstream row counts here.");
                        }
                    }
                    if (select.FromTable?.Subquery != null) CheckNonSinkQuery(select.FromTable.Subquery, results);
                    foreach (var join in select.Joins)
                        if (join.Table?.Subquery != null) CheckNonSinkQuery(join.Table.Subquery, results);
                    foreach (var cte in select.Ctes ?? [])
                        CheckNonSinkQuery(cte.Query, results);
                    break;
                }
            case SetOperationStatement setOp:
                CheckNonSinkQuery(setOp.Left, results);
                CheckNonSinkQuery(setOp.Right, results);
                break;
        }
    }

    private static HashSet<FailAction> CollectActions(SelectStatement select, out bool hasRules)
    {
        var actions = new HashSet<FailAction>();
        hasRules = false;
        foreach (var column in select.Columns)
        {
            foreach (var binding in SafeBindings(column))
            {
                hasRules = true;
                actions.Add(binding.Action);
            }
        }
        return actions;
    }

    // Rules are parsed with the statement, so anything malformed failed before lint ran.
    private static IReadOnlyList<ColumnRuleBinding> SafeBindings(SelectColumn column) =>
        ColumnExpectProjection.ToBindings(column);

    private static string ActionName(FailAction action) => action.ToString().ToUpperInvariant();

    private void Report(List<LintResult> results, LintSeverity severity, AstNode node, string message)
    {
        results.Add(new LintResult
        {
            RuleName = Name,
            Severity = severity,
            Message = message,
            LineNumber = node.Line,
            ColumnNumber = node.Column,
        });
    }
}
