using ETL_SQL.Core;
using ETL_SQL.Core.Quality;

namespace ETL_SQL.Analysis.Services;

/// <summary>
/// One <c>EXPECT &lt;rule&gt; [ON FAILURE &lt;action&gt;]</c> clause as written on a column.
/// </summary>
/// <param name="Index">
/// Its position among that column's clauses. A column may carry several, and repetition is how it
/// declares distinct rule/action pairs, so the index is what an edit addresses.
/// </param>
/// <param name="ActionExplicit">
/// False when the clause wrote no action. The effective action is still <c>WARN</c> — fail-safe, not
/// silent — and the panel says which of the two it is looking at, because "records and continues by
/// default" and "somebody chose to record and continue" are different facts about a pipeline.
/// </param>
public sealed record QualityRuleClause(
    int Index,
    string Rule,
    string Action,
    bool ActionExplicit,
    int Line);

/// <param name="ScopeId">
/// The same id the governance projection uses for this column, so one panel addresses a column's
/// tags and its rules with one identity rather than two that can disagree.
/// </param>
public sealed record QualityColumnRules(
    string ScopeId,
    string Column,
    string Table,
    int Line,
    IReadOnlyList<QualityRuleClause> Clauses);

/// <param name="Target">Where diverted rows go. Required for QUARANTINE and absent for THROW.</param>
/// <param name="Handling">
/// steward — rows outlive the run as a Portal queue item to correct and replay; script — the script
/// deals with them during this run and nothing is published for a person to act on.
/// </param>
public sealed record QualityRouting(
    string Action,
    string? Target,
    string? Retention,
    string Handling,
    int Line);

/// <param name="Id">The table the statement writes; that is what makes it addressable.</param>
/// <param name="MissingQuarantineTarget">
/// True when a column elects QUARANTINE and the statement routes nowhere. The parser refuses the
/// routing clause without a target, but nothing refuses a column rule whose action has no route —
/// the rows have nowhere to go, and the run says so only when it happens.
/// </param>
public sealed record QualityStatement(
    string Id,
    string Target,
    string Kind,
    int Line,
    IReadOnlyList<QualityColumnRules> Columns,
    IReadOnlyList<QualityRouting> Routing,
    bool MissingQuarantineTarget);

public sealed record ScriptQuality(
    bool Parsed,
    string? Error,
    IReadOnlyList<QualityStatement> Statements)
{
    public static ScriptQuality Failed(string error) => new(false, error, []);
}

public sealed record QualityEditResult(bool Applied, string Script, string? Error = null)
{
    public static QualityEditResult Ok(string script) => new(true, script);
    public static QualityEditResult Refused(string script, string error) => new(false, script, error);
}

/// <summary>
/// Reads and edits the data-quality rules a script declares.
///
/// <para><b>Rules are grammar, not metadata.</b> An <c>EXPECT</c> clause decides which rows leave a
/// statement, so it is written into the select list where a formatter or a comment stripper cannot
/// remove it — and so this service edits the clause itself rather than the <c>@expect</c> tag the
/// engine projects from it. A hand-written rule tag is inert and looks enforced, which is why the
/// tag surface refuses to author one and this surface exists instead.</para>
///
/// <para><b>Rule text is the author's, checked by the parser.</b> The panel composes a rule from a
/// picker, but what is written is the clause as typed and the verdict comes from reparsing the whole
/// script. Nothing here re-implements the rule grammar: a service that decided for itself what
/// <c>MATCHES</c> accepts would diverge from the parser the moment either changed, and the
/// divergence would show up as a rule that lints clean and never runs.</para>
///
/// <para><b>Routing is a statement-level decision, deliberately.</b> A column elects an action; the
/// statement says where those rows go. Declaring a target per column would let two columns of one
/// statement disagree about where the same run's rows land, which is why the language puts
/// <c>TO</c> and <c>WITH</c> on the statement — and why this surface reports a column electing
/// QUARANTINE with no route as a problem rather than quietly writing a route the author did not
/// choose.</para>
/// </summary>
public sealed class ScriptQualityRuleService
{
    /// <summary>Actions a column rule may elect. NOTIFY is <c>ASSERT JOB</c>'s alone.</summary>
    public static IReadOnlyList<string> ColumnActions { get; } = ["WARN", "THROW", "QUARANTINE"];

    /// <summary>Actions a statement may route. Only these three appear on a rule-carrying SELECT.</summary>
    public static IReadOnlyList<string> RoutingActions { get; } = ["WARN", "THROW", "QUARANTINE"];

    public static IReadOnlyList<string> HandlingModes { get; } = ["STEWARD", "SCRIPT"];

    /// <summary>The rules this script declares, in script order.</summary>
    public ScriptQuality Read(string? scriptText)
    {
        var source = scriptText ?? string.Empty;
        if (!ScriptTextEditing.TryParse(source, out var ast, out var parseError))
            return ScriptQuality.Failed(parseError);

        return new ScriptQuality(true, null, ReadStatements(ast));
    }

    /// <summary>
    /// Writes one <c>EXPECT</c> clause on one column: appended when <paramref name="index"/> is
    /// negative, replacing the clause at that index otherwise.
    /// </summary>
    public QualityEditResult SetRule(string? scriptText, string scopeId, int index, string? rule, string? action)
    {
        var source = scriptText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rule))
            return QualityEditResult.Refused(source, "A rule needs something to check.");

        return EditColumn(source, scopeId, (script, column, located) =>
        {
            var clauses = column.Expectations ?? [];
            var text = RenderClause(rule!.Trim(), action);

            if (index < 0)
            {
                // Appended at the end of the column's own clauses so written order — which is the
                // order they are reported and evaluated in — matches the order they were added.
                var insertAt = clauses.Count == 0
                    ? located.End
                    : ScriptTextEditing.Offset(script, clauses[^1].EndLine, clauses[^1].EndColumn);
                return insertAt < 0 ? null : ScriptTextEditing.Splice(script, insertAt, insertAt, " " + text);
            }

            if (index >= clauses.Count) return null;
            var clause = clauses[index];
            var start = ScriptTextEditing.Offset(script, clause.Line, clause.Column);
            var end = ScriptTextEditing.Offset(script, clause.EndLine, clause.EndColumn);
            return start < 0 || end < start ? null : ScriptTextEditing.Splice(script, start, end, text);
        });
    }

    /// <summary>Removes one <c>EXPECT</c> clause from a column.</summary>
    public QualityEditResult RemoveRule(string? scriptText, string scopeId, int index)
    {
        var source = scriptText ?? string.Empty;
        return EditColumn(source, scopeId, (script, column, _) =>
        {
            var clauses = column.Expectations ?? [];
            if (index < 0 || index >= clauses.Count) return null;

            var clause = clauses[index];
            var start = ScriptTextEditing.Offset(script, clause.Line, clause.Column);
            var end = ScriptTextEditing.Offset(script, clause.EndLine, clause.EndColumn);
            if (start < 0 || end < start) return null;

            // Take the space that separated it from what came before, so removing the only rule on a
            // column leaves the column exactly as it was written before the rule was added.
            while (start > 0 && script[start - 1] == ' ') start--;
            return ScriptTextEditing.Splice(script, start, end, string.Empty);
        });
    }

    /// <summary>
    /// Sets or clears one statement-level <c>ON FAILURE</c> routing clause. A null
    /// <paramref name="target"/> with a null <paramref name="retention"/> and the action already
    /// present removes it.
    /// </summary>
    public QualityEditResult SetRouting(
        string? scriptText,
        string statementId,
        string action,
        string? target,
        string? retention,
        string? handling,
        bool remove = false)
    {
        var source = scriptText ?? string.Empty;
        if (!ScriptTextEditing.TryParse(source, out var ast, out var parseError))
            return QualityEditResult.Refused(source, parseError);

        if (!Enum.TryParse<FailAction>(action, ignoreCase: true, out var parsedAction)
            || !RoutingActions.Contains(action, StringComparer.OrdinalIgnoreCase))
        {
            return QualityEditResult.Refused(source, $"'{action}' is not a routing action. Use WARN, THROW, or QUARANTINE.");
        }

        var located = FindStatement(ast, statementId);
        if (located is null)
            return QualityEditResult.Refused(source, $"This script has no statement writing '{statementId}'.");

        var (statement, select) = located.Value;
        var existing = (select.OnFailureActions ?? []).FirstOrDefault(clause => clause.Action == parsedAction);

        if (remove)
        {
            if (existing is null) return QualityEditResult.Ok(source);
            var start = ScriptTextEditing.Offset(source, existing.Line, existing.Column);
            var end = ScriptTextEditing.Offset(source, existing.EndLine, existing.EndColumn);
            if (start < 0 || end < start) return QualityEditResult.Refused(source, "That routing clause could not be located.");
            while (start > 0 && (source[start - 1] == ' ' || source[start - 1] == '\n' || source[start - 1] == '\r')) start--;
            return Commit(source, ScriptTextEditing.Splice(source, start, end, string.Empty));
        }

        if (parsedAction == FailAction.Throw && !string.IsNullOrWhiteSpace(target))
            return QualityEditResult.Refused(source, "ON FAILURE THROW does not take a target: the run stops, so no rows are routed anywhere.");
        if (parsedAction == FailAction.Quarantine && string.IsNullOrWhiteSpace(target))
            return QualityEditResult.Refused(source, "ON FAILURE QUARANTINE needs a target table — quarantined rows have nowhere else to go.");
        if (!string.IsNullOrWhiteSpace(retention) && !RetentionInterval.TryParse(retention!, out _))
            return QualityEditResult.Refused(source, $"'{retention}' is not a retention interval. Use '<n> MINUTES|HOURS|DAYS|WEEKS'.");
        if (!string.IsNullOrWhiteSpace(handling) && parsedAction != FailAction.Quarantine)
            return QualityEditResult.Refused(source, "HANDLING applies only to QUARANTINE — it says who owns the diverted rows.");
        if (!string.IsNullOrWhiteSpace(handling) && !HandlingModes.Contains(handling, StringComparer.OrdinalIgnoreCase))
            return QualityEditResult.Refused(source, $"HANDLING '{handling}' is not recognised — use STEWARD or SCRIPT.");

        var text = RenderRouting(parsedAction, target, retention, handling);

        if (existing is not null)
        {
            var start = ScriptTextEditing.Offset(source, existing.Line, existing.Column);
            var end = ScriptTextEditing.Offset(source, existing.EndLine, existing.EndColumn);
            if (start < 0 || end < start) return QualityEditResult.Refused(source, "That routing clause could not be located.");
            return Commit(source, ScriptTextEditing.Splice(source, start, end, text));
        }

        // A routing clause belongs at the end of the statement it routes, before its semicolon.
        var insertAt = SemicolonBefore(source, statement.EndOffset);
        if (insertAt < 0) return QualityEditResult.Refused(source, "That statement has no end to append the routing clause to.");
        var lineEnding = ScriptTextEditing.DetectLineEnding(source);
        return Commit(source, ScriptTextEditing.Splice(source, insertAt, insertAt, lineEnding + text));
    }

    // ── Reading ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<QualityStatement> ReadStatements(Script ast)
    {
        var statements = new List<QualityStatement>();
        foreach (var statement in ScriptTextEditing.Flatten(ast.Statements))
        {
            var (select, target, kind) = Addressable(statement);
            if (select is null || target is null) continue;

            var columns = new List<QualityColumnRules>();
            foreach (var column in select.Columns)
            {
                var name = OutputName(column);
                if (name is null) continue;
                var clauses = column.Expectations ?? [];
                if (clauses.Count == 0) continue;

                columns.Add(new QualityColumnRules(
                    $"column:{target}.{name}",
                    name,
                    target,
                    column.Line,
                    clauses.Select((clause, index) => new QualityRuleClause(
                        index,
                        clause.Text,
                        clause.Action.ToString().ToUpperInvariant(),
                        clause.ActionExplicit,
                        clause.Line)).ToArray()));
            }

            var routing = (select.OnFailureActions ?? [])
                .Select(clause => new QualityRouting(
                    clause.Action.ToString().ToUpperInvariant(),
                    clause.Target,
                    clause.Retention?.ToString(),
                    clause.Handling.ToString().ToUpperInvariant(),
                    clause.Line))
                .ToArray();

            var electsQuarantine = columns
                .SelectMany(column => column.Clauses)
                .Any(clause => clause.Action == "QUARANTINE");
            var routesQuarantine = routing.Any(clause => clause.Action == "QUARANTINE" && !string.IsNullOrEmpty(clause.Target));

            statements.Add(new QualityStatement(
                target, target, kind!, select.Line, columns, routing,
                electsQuarantine && !routesQuarantine));
        }
        return statements;
    }

    /// <summary>
    /// The statements this surface edits: the ones that name what they produce.
    ///
    /// <para>A rule on a statement with no output name has nothing to address it by, and quarantine
    /// routing on a query whose rows go straight to a reader has nowhere meaningful to route to. Both
    /// are left alone rather than given an identity that shifts when a line is inserted above.</para>
    /// </summary>
    private static (SelectStatement? Select, string? Target, string? Kind) Addressable(Statement statement) =>
        statement switch
        {
            SelectStatement { IntoTable: not null } select =>
                (select, select.IntoTable.TableName, select.IntoTable.TableName.StartsWith('#') ? "temp" : "table"),
            CreateDatasetStatement { SourceQuery: SelectStatement inner } dataset =>
                (inner, dataset.TempTableName, "dataset"),
            _ => (null, null, null),
        };

    private static string? OutputName(SelectColumn column)
    {
        if (!string.IsNullOrWhiteSpace(column.Alias)) return column.Alias;
        if (column.Expression is not IdentifierExpression identifier) return null;
        var name = identifier.Name;
        if (string.IsNullOrEmpty(name) || name.Contains('*')) return null;
        return name.Split('.')[^1];
    }

    // ── Writing ──────────────────────────────────────────────────────────────

    private QualityEditResult EditColumn(
        string source,
        string scopeId,
        Func<string, SelectColumn, ScriptSpan, string?> edit)
    {
        if (!ScriptTextEditing.TryParse(source, out var ast, out var parseError))
            return QualityEditResult.Refused(source, parseError);

        foreach (var statement in ScriptTextEditing.Flatten(ast.Statements))
        {
            var (select, target, _) = Addressable(statement);
            if (select is null || target is null) continue;

            foreach (var column in select.Columns)
            {
                var name = OutputName(column);
                if (name is null) continue;
                if (!string.Equals($"column:{target}.{name}", scopeId, StringComparison.OrdinalIgnoreCase)) continue;

                var start = ScriptTextEditing.Offset(source, column.Line, column.Column);
                var end = ScriptTextEditing.Offset(source, column.EndLine, column.EndColumn);
                if (start < 0 || end < start)
                    return QualityEditResult.Refused(source, $"'{scopeId}' could not be located in the script.");

                var edited = edit(source, column, new ScriptSpan(start, end));
                return edited is null
                    ? QualityEditResult.Refused(source, "That rule could not be placed.")
                    : Commit(source, edited);
            }
        }

        return QualityEditResult.Refused(source, $"This script projects no column called '{scopeId}'.");
    }

    private readonly record struct ScriptSpan(int Start, int End);

    private static string RenderClause(string rule, string? action)
    {
        var trimmed = rule.Trim();
        if (trimmed.StartsWith("EXPECT ", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[7..].Trim();

        return string.IsNullOrWhiteSpace(action)
            ? $"EXPECT {trimmed}"
            : $"EXPECT {trimmed} ON FAILURE {action!.Trim().ToUpperInvariant()}";
    }

    private static string RenderRouting(FailAction action, string? target, string? retention, string? handling)
    {
        var text = $"ON FAILURE {action.ToString().ToUpperInvariant()}";
        if (!string.IsNullOrWhiteSpace(target)) text += $" TO {target!.Trim()}";

        var options = new List<string>();
        if (!string.IsNullOrWhiteSpace(retention)) options.Add($"RETENTION = '{retention!.Trim().Replace("'", "''")}'");
        if (!string.IsNullOrWhiteSpace(handling)) options.Add($"HANDLING = {handling!.Trim().ToUpperInvariant()}");
        if (options.Count > 0) text += $" WITH ({string.Join(", ", options)})";

        return text;
    }

    private static (Statement Statement, SelectStatement Select)? FindStatement(Script ast, string statementId)
    {
        foreach (var statement in ScriptTextEditing.Flatten(ast.Statements))
        {
            var (select, target, _) = Addressable(statement);
            if (select is not null && string.Equals(target, statementId, StringComparison.OrdinalIgnoreCase))
                return (statement, select);
        }
        return null;
    }

    /// <summary>
    /// The offset of the semicolon that ends this statement, so a routing clause is appended inside
    /// the statement rather than after it — where the parser would read it as the start of the next.
    /// </summary>
    private static int SemicolonBefore(string source, int endOffset)
    {
        var index = Math.Clamp(endOffset, 0, source.Length) - 1;
        while (index >= 0 && char.IsWhiteSpace(source[index])) index--;
        return index >= 0 && source[index] == ';' ? index : -1;
    }

    private static QualityEditResult Commit(string original, string edited) =>
        ScriptTextEditing.TryParse(edited, out _, out var error)
            ? QualityEditResult.Ok(edited)
            : QualityEditResult.Refused(original, $"That rule would not parse: {error}");
}
