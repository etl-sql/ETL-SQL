using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Quality;

namespace ETL_SQL.Analysis.Linting.Rules;

/// <summary>
/// DQ-3: reports an <c>ASSERT JOB</c> predicate whose column no sink in the script writes.
/// <para>
/// At runtime a metric that was never observed is skipped with a warning and the assertion passes,
/// which is right for a run that legitimately produced no rows and wrong for a typo:
/// <c>NULL_PERCENT(clean_users.Emial) &lt; 0.02</c> would report green forever, and a guard that
/// cannot fail is worse than no guard, because someone is relying on it. The column-rule side
/// already rules this way for composite rules — a rule naming a column the statement does not
/// project is an error, precisely so a typo cannot "report clean because it never ran". This rule
/// applies the same standard to job predicates, statically, where the script's own sinks say what
/// is knowable.
/// </para>
/// <para>
/// Only what the script itself can settle is reported. A script whose sinks cannot be resolved
/// (dynamic SQL, an <c>EXECUTE</c>d block) contributes no column names and is left alone rather
/// than guessed at — a false Error on a correct script would train authors to ignore this rule.
/// </para>
/// </summary>
public class JobMetricColumnRule : ILintRule
{
    public string Name => "JobMetricColumn";

    public string Description =>
        "Reports ASSERT JOB predicates naming a column no sink in the script writes.";

    public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
    {
        var results = new List<LintResult>();
        var asserts = new List<AssertJobStatement>();
        var sinks = new List<SinkColumns>();

        foreach (var stmt in script.Statements)
            Collect(stmt, asserts, sinks);

        if (asserts.Count == 0 || sinks.Count == 0)
            return Task.FromResult<IEnumerable<LintResult>>(results);

        foreach (var assert in asserts)
            foreach (var predicate in assert.Predicates)
                CheckPredicate(assert, predicate, sinks, results);

        return Task.FromResult<IEnumerable<LintResult>>(results);
    }

    private void CheckPredicate(
        AssertJobStatement assert,
        JobMetricPredicate predicate,
        List<SinkColumns> sinks,
        List<LintResult> results)
    {
        if (predicate.ColumnName is not { Length: > 0 } column) return;

        var candidates = predicate.TargetName is { Length: > 0 } target
            ? sinks.Where(s => NameMatches(s.Target, target)).ToList()
            : sinks;

        if (predicate.TargetName is { Length: > 0 } && candidates.Count == 0)
        {
            Report(results, assert, predicate,
                $"targets '{predicate.TargetName}', which no statement in this script writes.");
            return;
        }

        // A sink whose columns could not be enumerated (SELECT *, a subquery shape the analyzer
        // does not resolve) makes every name plausible — say nothing rather than guess.
        if (candidates.Any(c => c.Unresolved)) return;

        if (!candidates.Any(c => c.Columns.Contains(column, StringComparer.OrdinalIgnoreCase)))
        {
            var scope = predicate.TargetName is { Length: > 0 } target2
                ? $"'{target2}'"
                : "any target this script writes";
            Report(results, assert, predicate,
                $"names column '{column}', which is not written to {scope}. "
                + "The metric would never be observed, so the predicate would be skipped and the "
                + "assertion would pass.");
        }
    }

    private void Report(List<LintResult> results, AssertJobStatement assert, JobMetricPredicate predicate, string detail)
    {
        results.Add(new LintResult
        {
            RuleName = Name,
            Severity = LintSeverity.Error,
            Message = $"ASSERT JOB {assert.JobName}: '{predicate.Describe()}' {detail}",
            LineNumber = assert.Line,
            ColumnNumber = assert.Column,
        });
    }

    private static bool NameMatches(string a, string b) =>
        Unqualify(a).Equals(Unqualify(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Compares on the bare object name so <c>dbo.clean_users</c>, <c>clean_users</c>, and a
    /// connection-qualified write all resolve to the same sink. <c>#temp</c> prefixes are part of
    /// the name and are kept.
    /// </summary>
    private static string Unqualify(string name)
    {
        var lastDot = name.LastIndexOf('.');
        return lastDot >= 0 ? name[(lastDot + 1)..] : name;
    }

    private void Collect(Statement? stmt, List<AssertJobStatement> asserts, List<SinkColumns> sinks)
    {
        switch (stmt)
        {
            case null:
                return;
            case AssertJobStatement assert:
                asserts.Add(assert);
                break;
            case SelectStatement { IntoTable: not null } select:
                sinks.Add(Describe(select.IntoTable.TableName, select));
                break;
            case InsertStatement { SelectQuery: SelectStatement inner } insert:
                sinks.Add(Describe(insert.TargetTable.TableName, inner));
                break;
            case CreateDatasetStatement { SourceQuery: SelectStatement datasetQuery } dataset:
                sinks.Add(Describe(dataset.TempTableName, datasetQuery));
                break;
            case BlockStatement block:
                foreach (var s in block.Statements ?? []) Collect(s, asserts, sinks);
                break;
            case IfStatement ifStmt:
                Collect(ifStmt.IfBody, asserts, sinks);
                foreach (var ei in ifStmt.ElseIfClauses ?? []) Collect(ei.Body, asserts, sinks);
                Collect(ifStmt.ElseBody, asserts, sinks);
                break;
            case WhileStatement w:
                Collect(w.Body, asserts, sinks);
                break;
            case ForStatement f:
                Collect(f.Body, asserts, sinks);
                break;
            case ForeachStatement fe:
                Collect(fe.Body, asserts, sinks);
                break;
            case TryCatchStatement tc:
                Collect(tc.TryBody, asserts, sinks);
                Collect(tc.CatchBody, asserts, sinks);
                break;
        }
    }

    private static SinkColumns Describe(string target, SelectStatement select)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolved = false;

        foreach (var column in select.Columns)
        {
            if (column.Alias is { Length: > 0 } alias)
            {
                columns.Add(alias);
                continue;
            }
            if (column.Expression is IdentifierExpression id)
            {
                var name = id.Name.Split('.').Last();
                if (name == "*") unresolved = true;
                else columns.Add(name);
                continue;
            }
            // A star selector or a computed column with no alias: the written name is decided
            // downstream, so this sink cannot rule any name out.
            unresolved = true;
        }

        return new SinkColumns(target, columns, unresolved);
    }

    private sealed record SinkColumns(string Target, HashSet<string> Columns, bool Unresolved);
}
