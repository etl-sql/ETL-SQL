using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Quality;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Services;

/// <summary>
/// Runtime enforcement of <c>@expect</c> column rules. Built once per statement that carries
/// rules (<see cref="TryCreate"/> returns null otherwise, so zero rules costs zero overhead) and
/// applied by wrapping the projection stream: each row is validated against the projected
/// (post-expression) values, then passed through, diverted to a quarantine target, or thrown on.
///
/// NULL values skip every rule except <c>NOT NULL</c> (SQL CHECK-constraint convention) — pair
/// with <c>NOT NULL</c> explicitly to reject them. String comparisons honor
/// <see cref="IExecutionContext.CaseSensitiveComparison"/>; numeric compares are decimal.
/// </summary>
public sealed class ColumnQualityValidator
{
    private readonly IExecutionContext _context;
    private readonly ILogger _logger;
    private readonly SelectStatement _statement;
    private readonly IReadOnlyList<ColumnRuleSet> _ruleSets;
    private readonly IReadOnlyDictionary<FailAction, FailureActionClause> _routing;
    private readonly Dictionary<string, HashSet<string>> _existsInKeys = new(StringComparer.OrdinalIgnoreCase);
    private QuarantineWriter? _quarantineWriter;
    private QuarantineWriter? _warnWriter;

    private ColumnQualityValidator(
        IExecutionContext context,
        ILogger logger,
        SelectStatement statement,
        IReadOnlyList<ColumnRuleSet> ruleSets,
        IReadOnlyDictionary<FailAction, FailureActionClause> routing)
    {
        _context = context;
        _logger = logger;
        _statement = statement;
        _ruleSets = ruleSets;
        _routing = routing;
    }

    /// <summary>True when at least one rule needs the whole-input UNIQUE pre-pass.</summary>
    public bool RequiresUniquePrePass => _ruleSets.Any(rs => rs.Bindings.Any(b => b.Rules.Any(r => r is UniqueRule)));

    /// <summary>
    /// Builds a validator for <paramref name="statement"/>, or returns null when no column carries
    /// rule tags — the caller then leaves the projection stream untouched.
    /// </summary>
    public static ColumnQualityValidator? TryCreate(
        IExecutionContext context, ILogger logger, SelectStatement statement, IReadOnlyList<string> outputColumnNames)
    {
        if (!statement.Columns.Any(c => ColumnRuleParser.HasRuleTags(c.Metadata)))
            return null;

        var ruleSets = new List<ColumnRuleSet>();
        for (int i = 0; i < statement.Columns.Count; i++)
        {
            var column = statement.Columns[i];
            if (!ColumnRuleParser.HasRuleTags(column.Metadata)) continue;

            IReadOnlyList<ColumnRuleBinding> bindings;
            try
            {
                bindings = ColumnRuleParser.ParseBindings(column.Metadata!);
            }
            catch (ColumnRuleParseException ex)
            {
                // The linter reports this as an Error before execution; if a caller bypassed lint,
                // fail loudly rather than silently dropping enforcement.
                throw new ExecutionException($"Invalid data-quality rule on column {i + 1}: {ex.Message}");
            }

            var name = i < outputColumnNames.Count ? outputColumnNames[i] : column.Alias ?? $"Column{i + 1}";
            bool isPii = IsPiiTagged(column.Metadata!);
            ruleSets.Add(new ColumnRuleSet(i, name, bindings, isPii));
        }

        if (ruleSets.Count == 0) return null;

        var routing = (statement.OnFailureActions ?? [])
            .GroupBy(c => c.Action)
            .ToDictionary(g => g.Key, g => g.First());

        // Symmetric validation (design decision 5) — the linter reports these first, but the
        // engine must not silently drop enforcement when lint was skipped.
        foreach (var action in ruleSets.SelectMany(rs => rs.Bindings).Select(b => b.Action).Distinct())
        {
            if (action == FailAction.Quarantine && !routing.ContainsKey(FailAction.Quarantine))
                throw new ExecutionException(
                    "@fail: 'QUARANTINE' requires a matching ON FAILURE QUARANTINE TO <table> clause on the statement.");
        }

        return new ColumnQualityValidator(context, logger, statement, ruleSets, routing);
    }

    /// <summary>
    /// Prepares per-statement state (EXISTS IN key sets). Call once before the first row.
    /// </summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        BuildExistsInKeySetsAsync(cancellationToken);

    /// <summary>
    /// Validates one row. Returns <c>false</c> when the row was quarantined — the caller must
    /// drop it from the output. Warned rows return <c>true</c> (they still reach the target);
    /// a THROW failure raises <see cref="ExecutionException"/> and aborts the statement.
    /// </summary>
    public async Task<bool> TryAcceptRowAsync(Row input, Row projected, CancellationToken cancellationToken = default)
    {
        _context.DataQuality.RecordRowValidated();
        var failure = await EvaluateRowAsync(input, projected, cancellationToken);

        if (failure is { Action: FailAction.Quarantine })
        {
            await QuarantineAsync(input, failure, cancellationToken);
            return false;
        }
        if (failure is { Action: FailAction.Warn })
        {
            await WarnAsync(input, failure, cancellationToken);
        }
        return true;
    }

    /// <summary>
    /// Flushes captured rows, applies retention pruning, and emits the aggregated end-of-stream
    /// diagnostics. Call once after the last row.
    /// </summary>
    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        await FlushAsync(cancellationToken);
        EmitAggregatedDiagnostics();
    }

    /// <summary>
    /// Convenience wrapper enforcing rules over a stream of (input, projected) pairs — quarantined
    /// rows are never yielded.
    /// </summary>
    public async IAsyncEnumerable<Row> ValidateAsync(
        IAsyncEnumerable<(Row Input, Row Projected)> rows,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await foreach (var (input, projected) in rows.WithCancellation(cancellationToken))
        {
            if (await TryAcceptRowAsync(input, projected, cancellationToken))
                yield return projected;
        }
        await CompleteAsync(cancellationToken);
    }

    /// <summary>
    /// Evaluates every rule against one row. Returns the first failure that determines the row's
    /// fate — THROW is raised immediately; otherwise QUARANTINE wins over WARN, because a
    /// quarantined row leaves the output and cannot also be warned into it.
    /// </summary>
    private async Task<RowFailure?> EvaluateRowAsync(Row input, Row projected, CancellationToken cancellationToken)
    {
        RowFailure? decided = null;

        foreach (var ruleSet in _ruleSets)
        {
            var value = projected[ruleSet.OutputIndex];
            foreach (var binding in ruleSet.Bindings)
            {
                foreach (var rule in binding.Rules)
                {
                    if (rule is UniqueRule) continue; // handled by the UNIQUE pre-pass
                    if (await RulePassesAsync(rule, value, projected, cancellationToken)) continue;

                    _context.DataQuality.RecordFailure(
                        ruleSet.ColumnName, rule.Text, binding.Action, value, ruleSet.IsPii);

                    var reason = DescribeFailure(rule, value, ruleSet);
                    if (binding.Action == FailAction.Throw)
                    {
                        throw new ExecutionException(
                            $"Data quality rule failed: column '{ruleSet.ColumnName}' {reason}.",
                            null, _statement.Line, _statement.Column);
                    }

                    var failure = new RowFailure(binding.Action, ruleSet, rule, value, reason);
                    if (decided is null || (decided.Action == FailAction.Warn && failure.Action == FailAction.Quarantine))
                        decided = failure;
                }
            }
        }
        return decided;
    }

    private async Task<bool> RulePassesAsync(ColumnRule rule, object? value, Row projected, CancellationToken cancellationToken)
    {
        // NOT NULL is the only rule that fails on NULL; every other rule skips NULL values
        // (SQL CHECK-constraint convention) — pair with NOT NULL explicitly to reject them.
        if (rule is NotNullRule) return value is not null and not DBNull;
        if (rule is ExprRule expr)
            return await _context.EvaluateCondition(expr.Predicate, projected);
        if (value is null or DBNull) return true;

        switch (rule)
        {
            case MatchesRule matches:
                return GetRegex(matches).IsMatch(Stringify(value));

            case ComparisonRule comparison:
            {
                if (!TryToDecimal(value, out var numeric)) return false;
                return comparison.Op switch
                {
                    CompareOp.GreaterOrEqual => numeric >= comparison.Value,
                    CompareOp.LessOrEqual => numeric <= comparison.Value,
                    CompareOp.Greater => numeric > comparison.Value,
                    CompareOp.Less => numeric < comparison.Value,
                    _ => numeric == comparison.Value
                };
            }

            case InListRule inList:
                return inList.Values.Any(candidate => ValuesEqual(candidate, value));

            case ExistsInRule existsIn:
                return _existsInKeys.TryGetValue(ExistsInKey(existsIn), out var keys)
                       && keys.Contains(Stringify(value), KeyComparer(_context.CaseSensitiveComparison));

            default:
                return true;
        }
    }

    // ── EXISTS IN key sets ─────────────────────────────────────────────────

    /// <summary>
    /// Builds each referenced table's key set once per statement, then probes per row. Reference
    /// tables are dimension-sized by nature; the build honors SET CASE_SENSITIVE.
    /// </summary>
    private async Task BuildExistsInKeySetsAsync(CancellationToken cancellationToken)
    {
        var references = _ruleSets
            .SelectMany(rs => rs.Bindings)
            .SelectMany(b => b.Rules)
            .OfType<ExistsInRule>()
            .GroupBy(ExistsInKey)
            .Select(g => g.First())
            .ToList();

        foreach (var reference in references)
        {
            var key = ExistsInKey(reference);
            if (_existsInKeys.ContainsKey(key)) continue;

            var keys = new HashSet<string>(KeyComparer(_context.CaseSensitiveComparison));
            var source = await _context.ResolveDataSourceAsync(new TableReference(reference.Table));
            await foreach (var batch in source.ReadBatches(_context.EffectiveBatchSize, cancellationToken))
            {
                foreach (var row in batch.Rows)
                {
                    var value = row[reference.KeyColumn];
                    if (value is not null and not DBNull) keys.Add(Stringify(value));
                }
            }
            _existsInKeys[key] = keys;
            _logger.Debug("EXISTS IN {Table}({Column}): built key set of {Count} value(s).",
                reference.Table, reference.KeyColumn, keys.Count);
        }
    }

    private static string ExistsInKey(ExistsInRule rule) => $"{rule.Table}({rule.KeyColumn})";

    // ── Routing ────────────────────────────────────────────────────────────

    private async Task QuarantineAsync(Row input, RowFailure failure, CancellationToken cancellationToken)
    {
        var clause = _routing[FailAction.Quarantine];
        _quarantineWriter ??= new QuarantineWriter(_context, clause.Target!, DataQualityColumns.QuarantinedStatus, includeTargetWritten: false);
        await _quarantineWriter.WriteAsync(input, failure, cancellationToken);
        _context.DataQuality.RecordRowQuarantined();
    }

    private async Task WarnAsync(Row input, RowFailure failure, CancellationToken cancellationToken)
    {
        _context.DataQuality.RecordRowWarned();
        // Diagnostic-only mode: ON FAILURE WARN with no TO target writes no row anywhere; the
        // aggregated end-of-stream diagnostic still fires.
        if (!_routing.TryGetValue(FailAction.Warn, out var clause) || clause.Target == null) return;

        _warnWriter ??= new QuarantineWriter(_context, clause.Target, DataQualityColumns.WarnedStatus, includeTargetWritten: true);
        await _warnWriter.WriteAsync(input, failure, cancellationToken);
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_quarantineWriter != null)
            await _quarantineWriter.FlushAsync(_routing[FailAction.Quarantine].Retention, cancellationToken);
        if (_warnWriter != null)
            await _warnWriter.FlushAsync(_routing[FailAction.Warn].Retention, cancellationToken);
    }

    /// <summary>
    /// Emits one <c>Diagnostic(Warning)</c> per (column, rule) pair that failed under WARN, with
    /// the failure count and capped samples. Per-row detail goes to Debug logging only.
    /// </summary>
    private void EmitAggregatedDiagnostics()
    {
        foreach (var summary in _context.DataQuality.Failures.Where(f => f.Action == FailAction.Warn))
            _logger.Warning("{DataQualityWarning}", summary.ToMessage());

        foreach (var summary in _context.DataQuality.Failures.Where(f => f.Action == FailAction.Quarantine))
            _logger.Info("{DataQualityQuarantine}", summary.ToMessage());
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private readonly Dictionary<MatchesRule, Regex> _regexCache = new();

    private Regex GetRegex(MatchesRule rule)
    {
        if (_regexCache.TryGetValue(rule, out var cached)) return cached;
        var compiled = rule.Compile(_context.CaseSensitiveComparison);
        _regexCache[rule] = compiled;
        return compiled;
    }

    private bool ValuesEqual(object? candidate, object? value)
    {
        if (candidate is decimal || value is decimal || IsNumeric(candidate) || IsNumeric(value))
        {
            if (TryToDecimal(candidate, out var a) && TryToDecimal(value, out var b)) return a == b;
        }
        return string.Equals(Stringify(candidate), Stringify(value),
            _context.CaseSensitiveComparison ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNumeric(object? value) =>
        value is decimal or double or float or int or long or short or byte or sbyte or uint or ulong or ushort;

    private static bool TryToDecimal(object? value, out decimal result)
    {
        switch (value)
        {
            case decimal d: result = d; return true;
            case null or DBNull: result = 0; return false;
            case string s: return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
            case IConvertible:
                try { result = Convert.ToDecimal(value, CultureInfo.InvariantCulture); return true; }
                catch { result = 0; return false; }
            default: result = 0; return false;
        }
    }

    private static string Stringify(object? value) => value switch
    {
        null or DBNull => string.Empty,
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static StringComparer KeyComparer(bool caseSensitive) =>
        caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    private static bool IsPiiTagged(IReadOnlyDictionary<string, string> metadata) =>
        metadata.TryGetValue("pii", out var pii)
        && ColumnRuleParser.Unquote(pii).Equals("true", StringComparison.OrdinalIgnoreCase);

    private static string DescribeFailure(ColumnRule rule, object? value, ColumnRuleSet ruleSet)
    {
        var shown = ruleSet.IsPii ? DataQualityReport.PiiMask : Stringify(value) is { Length: > 0 } s ? s : "NULL";
        return rule is NotNullRule
            ? "is NULL but the rule is NOT NULL"
            : $"value '{shown}' failed rule \"{rule.Text}\"";
    }

    internal sealed record ColumnRuleSet(
        int OutputIndex,
        string ColumnName,
        IReadOnlyList<ColumnRuleBinding> Bindings,
        bool IsPii);

    internal sealed record RowFailure(
        FailAction Action,
        ColumnRuleSet ColumnRules,
        ColumnRule Rule,
        object? Value,
        string Reason)
    {
        public string ColumnName => ColumnRules.ColumnName;
        public bool IsPii => ColumnRules.IsPii;
    }
}
