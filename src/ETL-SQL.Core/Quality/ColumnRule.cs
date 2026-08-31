using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ETL_SQL.Core.Quality;

/// <summary>Uniqueness variants for <see cref="UniqueRule"/>.</summary>
public enum UniqueMode { All, First, Last }

/// <summary>
/// What happens when a data-quality check fails. One vocabulary across the whole feature: a column
/// rule elects one of these, the statement's trailing <c>ON FAILURE</c> blocks route them, and
/// <c>ASSERT JOB</c> declares them the same way. Each surface accepts the subset that is meaningful
/// there and rejects the rest with a message naming the surface that does accept it.
/// </summary>
public enum FailAction
{
    /// <summary>Raise an error and abort. The only action that fails a run.</summary>
    Throw,

    /// <summary>
    /// Record and continue — the default everywhere an action can be omitted, so an author who
    /// writes a rule and forgets the action gets fail-safe behaviour rather than silence.
    /// </summary>
    Warn,

    /// <summary>Remove the row from the output and write it to the routing target. Column rules only.</summary>
    Quarantine,

    /// <summary>
    /// Post a summary through a named Orchestrator notification. Non-fatal on its own — "worth
    /// telling someone about, not worth stopping for". <c>ASSERT JOB</c> only: a per-statement
    /// notification would fire once per materializing statement, which is an alert-storm generator.
    /// </summary>
    Notify
}

/// <summary>
/// Who owns a quarantined row, selected by <c>WITH (HANDLING = …)</c> on the
/// <c>ON FAILURE QUARANTINE</c> clause.
/// </summary>
public enum QuarantineHandling
{
    /// <summary>
    /// The default. Rows outlive the run as durable evidence: a replay manifest is persisted and
    /// the rows become a Portal steward-queue item to be corrected and replayed later.
    /// </summary>
    Steward,

    /// <summary>
    /// The script remediates, reroutes, or discards the rows during this run. They still leave the
    /// main output and still carry their <c>__dq_*</c> context, so later statements can read them —
    /// but nothing is published for a human to act on afterwards, because by the end of the run
    /// there is nothing left to act on. Recording a steward queue item here would ask someone to
    /// remediate rows the script already handled.
    /// </summary>
    Script
}

/// <summary>Comparison operators supported by numeric rules.</summary>
public enum CompareOp { GreaterOrEqual, LessOrEqual, Greater, Less, Equal }

/// <summary>
/// One parsed rule from a column's <c>EXPECT</c> clause. Rules validate the projected
/// (post-expression) value; NULL values skip every rule except <see cref="NotNullRule"/>.
/// <see cref="Text"/> preserves the original rule segment for diagnostics and the
/// <c>__dq_rule</c> quarantine column.
/// </summary>
public abstract record ColumnRule
{
    /// <summary>The original rule text as written in the tag (trimmed), e.g. "UNIQUE_FIRST BY LoadedAt".</summary>
    public required string Text { get; init; }
}

/// <summary><c>NOT NULL</c> — the only rule that fails on NULL.</summary>
public sealed record NotNullRule : ColumnRule;

/// <summary>
/// <c>UNIQUE</c>, <c>UNIQUE WITH (col, …)</c> (composite key over the tuple), or
/// <c>UNIQUE_FIRST/UNIQUE_LAST BY &lt;expr&gt;</c> (keep one row per duplicate group by order key).
/// </summary>
public sealed record UniqueRule(
    UniqueMode Mode,
    Expression? OrderKey,
    IReadOnlyList<string>? CompositeColumns) : ColumnRule;

/// <summary>
/// <c>MATCHES &lt;regex&gt;</c>, or <c>NOT MATCHES &lt;regex&gt;</c> for the value that must *not*
/// contain a pattern. Patterns compile with <see cref="RegexOptions.NonBacktracking"/> —
/// a per-row user-supplied regex is otherwise a ReDoS vector — so constructs NonBacktracking
/// cannot compile (backreferences, lookaround) are rejected at parse/lint time.
/// </summary>
/// <param name="Pattern">The regular expression, unanchored: it searches rather than matches whole.</param>
/// <param name="Negated">True for <c>NOT MATCHES</c>, inverting the verdict.</param>
public sealed record MatchesRule(string Pattern, bool Negated = false) : ColumnRule
{
    /// <summary>
    /// Compiles the pattern with NonBacktracking (plus IgnoreCase when <paramref name="caseSensitive"/>
    /// is false). Throws <see cref="ColumnRuleParseException"/> when the pattern is invalid or uses
    /// constructs NonBacktracking does not support.
    /// </summary>
    public Regex Compile(bool caseSensitive)
    {
        var options = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant;
        if (!caseSensitive) options |= RegexOptions.IgnoreCase;
        try
        {
            return new Regex(Pattern, options, TimeSpan.FromSeconds(5));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw new ColumnRuleParseException(
                $"MATCHES pattern '{Pattern}' is not valid: {ex.Message} " +
                "(patterns compile with RegexOptions.NonBacktracking, which does not support backreferences or lookaround).");
        }
    }
}

/// <summary>
/// <c>NOT BLANK</c> — the value must contain a non-whitespace character. Like every rule except
/// <see cref="NotNullRule"/> it skips NULL, so <c>'NOT NULL, NOT BLANK'</c> is the full
/// "a name is required" check. Distinct from a <c>MATCHES</c> pattern only in that the intent is
/// legible in diagnostics, autocomplete and policy review.
/// </summary>
public sealed record NotBlankRule : ColumnRule;

/// <summary>
/// <c>LENGTH BETWEEN &lt;min&gt; AND &lt;max&gt;</c>, or a <c>LENGTH</c> comparison such as
/// <c>LENGTH &gt;= 5</c>. Every accepted form lowers to this inclusive character-count range —
/// <c>LENGTH &gt; 5</c> becomes a minimum of 6 — so the runtime has one predicate rather than one
/// per operator. Length is the rendered value's character count, matching the <c>LEN</c> function.
/// </summary>
/// <param name="MinLength">Inclusive lower bound; 0 when the form set no lower bound.</param>
/// <param name="MaxLength">Inclusive upper bound, or null when the form set no upper bound.</param>
public sealed record LengthRule(int MinLength, int? MaxLength) : ColumnRule;

/// <summary>
/// <c>CASTABLE AS &lt;type&gt;</c> — the value must convert to the named type. Conversion is the
/// engine's own, the one behind <c>TRY_CAST</c>, so a rule and a later cast of the same value agree
/// by construction rather than by two implementations happening to match.
///
/// A declared width is enforced on top of that conversion, because the shared converter ignores it:
/// <c>DECIMAL(18,2)</c> and <c>VARCHAR(50)</c> would otherwise read as constraints while checking
/// nothing beyond "is a number" and "is a string".
/// </summary>
/// <param name="DeclaredType">The type as written, passed to the converter verbatim so forms it
/// does interpret — <c>DATETIME(3)</c> — keep working.</param>
/// <param name="BaseType">The type name without its width, upper-cased.</param>
/// <param name="Precision">Total digits for DECIMAL/NUMERIC, or maximum characters for the string
/// types; null when no width was declared.</param>
/// <param name="Scale">Digits after the decimal point; null unless declared.</param>
public sealed record CastableRule(
    string DeclaredType,
    string BaseType,
    int? Precision,
    int? Scale) : ColumnRule;

/// <summary>Numeric comparison (<c>&gt;= 0</c>, <c>&lt;= 120</c>, …). Compares as decimal at runtime.</summary>
public sealed record ComparisonRule(CompareOp Op, decimal Value) : ColumnRule;

/// <summary>
/// <c>IN ('NA','EMEA',…)</c> — membership in a literal list — or <c>NOT IN (…)</c> for the
/// placeholder values a column must never carry, such as <c>'UNKNOWN'</c> or <c>'N/A'</c>.
/// </summary>
/// <param name="Negated">True for <c>NOT IN</c>, inverting the verdict.</param>
public sealed record InListRule(IReadOnlyList<object?> Values, bool Negated = false) : ColumnRule;

/// <summary>
/// <c>EXISTS IN table(column)</c> — relationship/FK check against a reference table's key set — or
/// the composite form <c>EXISTS WITH (a, b) IN table(x, y)</c>, which checks the tuple of projected
/// columns against the same-arity tuple in the reference table.
///
/// The composite form is what makes a scoped foreign key expressible. A single-column check on
/// CustomerId accepts a customer that exists under some *other* TenantId, so on a multi-tenant
/// table the single-column rule reports as passing exactly the rows a tenant boundary is supposed
/// to catch.
/// </summary>
/// <param name="Table">The reference table.</param>
/// <param name="KeyColumns">The reference table's key columns, in tuple order.</param>
/// <param name="SourceColumns">
/// The projected columns forming the probe tuple, in the same order — null for the single-column
/// form, which probes with the declaring column's own projected value.
/// </param>
public sealed record ExistsInRule(
    string Table,
    IReadOnlyList<string> KeyColumns,
    IReadOnlyList<string>? SourceColumns = null) : ColumnRule
{
    /// <summary>Single-column convenience over <see cref="KeyColumns"/>.</summary>
    public ExistsInRule(string table, string keyColumn) : this(table, [keyColumn]) { }

    /// <summary>True for the composite <c>EXISTS WITH (…) IN table(…)</c> form.</summary>
    public bool IsComposite => SourceColumns is { Count: > 0 };
}

/// <summary>
/// <c>BETWEEN &lt;lower&gt; AND &lt;upper&gt;</c> — an inclusive range whose bounds are full
/// expressions, so a rule can be typed (<c>BETWEEN '2020-01-01' AND '2030-01-01'</c>) or relative
/// (<c>BETWEEN DATEADD(DAY, -30, @RunDate) AND @RunDate</c>). The numeric comparison rules accept
/// only decimal literals, which cannot express either.
///
/// Bounds are evaluated per row against the projected row, and comparison is the engine's
/// type-aware one — the same used to order UNIQUE keys — so dates compare as dates.
/// </summary>
public sealed record BetweenRule(Expression Lower, Expression Upper) : ColumnRule;

/// <summary><c>EXPR &lt;predicate&gt;</c> — cross-column boolean evaluated over the full projected row.</summary>
public sealed record ExprRule(Expression Predicate) : ColumnRule;

/// <summary>
/// Compound rule combining sub-rules with logical AND (<c>rule1 AND rule2</c>).
/// Evaluates to true if all operand rules pass.
/// </summary>
public sealed record AndRule(IReadOnlyList<ColumnRule> Operands) : ColumnRule;

/// <summary>
/// Compound rule combining sub-rules with logical OR (<c>rule1 OR rule2</c>).
/// Evaluates to true if any operand rule passes.
/// </summary>
public sealed record OrRule(IReadOnlyList<ColumnRule> Operands) : ColumnRule;

/// <summary>
/// Helper extensions for working with nested compound rules.
/// </summary>
public static class ColumnRuleExtensions
{
    /// <summary>Recursively enumerates all atomic and composite rules in the tree.</summary>
    public static IEnumerable<ColumnRule> Flatten(this ColumnRule rule)
    {
        yield return rule;
        if (rule is AndRule and)
        {
            foreach (var op in and.Operands)
                foreach (var nested in op.Flatten())
                    yield return nested;
        }
        else if (rule is OrRule or)
        {
            foreach (var op in or.Operands)
                foreach (var nested in op.Flatten())
                    yield return nested;
        }
    }

    /// <summary>Recursively flattens a collection of rules.</summary>
    public static IEnumerable<ColumnRule> FlattenAll(this IEnumerable<ColumnRule> rules) =>
        rules.SelectMany(r => r.Flatten());
}

/// <summary>
/// One rule/action pair: the parsed rules,
/// the bound action (default <see cref="FailAction.Warn"/> when none is written —
/// fail-safe, not silent), and the metadata key it came from (<c>expect</c>, <c>expect_1</c>, …).
/// </summary>
/// <summary>
/// One <c>EXPECT &lt;rule&gt; [ON FAILURE &lt;action&gt;]</c> clause as written on a select column.
/// A column carries these in written order; repetition is how a column declares several distinct
/// rule/action pairs, replacing the numbered <c>@expect_N</c>/<c>@fail_N</c> tag pairing.
/// </summary>
/// <param name="Rules">The rules in this clause. Several only when combined with AND/OR.</param>
/// <param name="Action">
/// What happens to a failing row. Defaults to <see cref="FailAction.Warn"/> when the author wrote
/// no <c>ON FAILURE</c> — fail-safe, not silent.
/// </param>
/// <param name="ActionExplicit">
/// False when the action was defaulted. Kept so diagnostics and the serializer can tell a written
/// <c>ON FAILURE WARN</c> from an omitted one.
/// </param>
/// <param name="Text">The clause's rule text as written, for diagnostics and the tag projection.</param>
public sealed record ColumnExpectClause(
    IReadOnlyList<ColumnRule> Rules,
    FailAction Action,
    bool ActionExplicit,
    string Text) : AstNode;

public sealed record ColumnRuleBinding(
    string ExpectKey,
    IReadOnlyList<ColumnRule> Rules,
    FailAction Action,
    bool ActionExplicit)
{
    /// <summary>
    /// How this rule's clause is named on read-side surfaces: <c>EXPECT</c> for a column's first
    /// clause, <c>EXPECT #2</c> for its second, and so on. The projection key stays
    /// <c>expect</c>/<c>expect_1</c> because that is the tag lineage stores; what a steward is
    /// shown is the clause they would find in the script.
    /// </summary>
    public string ClauseLabel =>
        ExpectKey.IndexOf('_') is var underscore && underscore < 0
            ? "EXPECT"
            : $"EXPECT #{(int.TryParse(ExpectKey[(underscore + 1)..], out var n) ? n + 1 : 1)}";
}

/// <summary>Raised when a projected rule tag value cannot be parsed. The read side
/// surfaces the message as a <c>Diagnostic(Error)</c>; the runtime treats it as a hard error.</summary>
public sealed class ColumnRuleParseException(string message) : Exception(message);
