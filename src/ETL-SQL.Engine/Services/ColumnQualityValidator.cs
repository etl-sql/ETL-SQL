using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Quality;
using ETL_SQL.Core.Spill;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Services;

/// <summary>
/// Runtime enforcement of <c>EXPECT</c> column rules. Built once per statement that carries
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
    private readonly bool _hasExpressionRules;
    private readonly Dictionary<string, HashSet<string>> _existsInKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<BetweenRule> _rowInvariantBetweenRules = new();
    private readonly Dictionary<BetweenRule, (object? Lower, object? Upper)> _hoistedBetweenBounds = new();

    /// <summary>Delimits parts of a composite key; a unit separator cannot appear in a rendered value.</summary>
    private const char KeyPartSeparator = '\u001F';
    private QuarantineWriter? _quarantineWriter;
    private QuarantineWriter? _warnWriter;
    private bool _quarantineManifestRecorded;

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
        // The rules that need the evaluator, and so force the async validation path. Everything
        // else is a pure per-row predicate that costs no state machine.
        foreach (var rule in ruleSets.SelectMany(rs => rs.Bindings).SelectMany(b => b.Rules).FlattenAll())
        {
            if (rule is BetweenRule between && IsRowInvariant(between.Lower) && IsRowInvariant(between.Upper))
            {
                _rowInvariantBetweenRules.Add(between);
            }
        }

        _hasExpressionRules = ruleSets.Any(rs => rs.Bindings.Any(binding => binding.Rules.FlattenAll().Any(rule =>
            rule is ExprRule || rule is BetweenRule between && !_rowInvariantBetweenRules.Contains(between))));
    }

    /// <summary>True when at least one rule needs the whole-input UNIQUE pre-pass.</summary>
    public bool RequiresUniquePrePass => _ruleSets.Any(rs => rs.Bindings.Any(b => b.Rules.FlattenAll().Any(r => r is UniqueRule)));

    /// <summary>
    /// Builds a validator for <paramref name="statement"/>, or returns null when no column carries
    /// rule tags — the caller then leaves the projection stream untouched.
    /// </summary>
    public static ColumnQualityValidator? TryCreate(
        IExecutionContext context, ILogger logger, SelectStatement statement, IReadOnlyList<string> outputColumnNames)
    {
        if (!statement.Columns.Any(ColumnExpectProjection.HasRules))
            return null;

        var ruleSets = new List<ColumnRuleSet>();
        for (int i = 0; i < statement.Columns.Count; i++)
        {
            var column = statement.Columns[i];
            if (!ColumnExpectProjection.HasRules(column)) continue;

            // Rules are parsed with the statement, so a malformed one never reaches here — it is a
            // SyntaxException at parse time, with a position, for every caller including this one.
            var bindings = ColumnExpectProjection.ToBindings(column);

            var name = i < outputColumnNames.Count ? outputColumnNames[i] : column.Alias ?? $"Column{i + 1}";
            bool isPii = IsPiiTagged(column.Metadata!);
            ruleSets.Add(new ColumnRuleSet(i, name, bindings, isPii, ResolveOwner(column.Metadata!)));
        }

        if (ruleSets.Count == 0) return null;

        ValidateCompositeColumns(ruleSets, outputColumnNames);

        var routing = (statement.OnFailureActions ?? [])
            .GroupBy(c => c.Action)
            .ToDictionary(g => g.Key, g => g.First());

        // Symmetric validation (design decision 5) — the linter reports these first, but the
        // engine must not silently drop enforcement when lint was skipped. A dry run is exempt:
        // it never routes a row anywhere, so demanding a routing clause would force a steward to
        // author the very wiring they are still deciding whether to adopt.
        foreach (var action in context.DataQualityDryRun
            ? []
            : ruleSets.SelectMany(rs => rs.Bindings).Select(b => b.Action).Distinct())
        {
            if (action == FailAction.Quarantine && !routing.ContainsKey(FailAction.Quarantine))
                throw new ExecutionException(
                    "EXPECT … ON FAILURE QUARANTINE requires a matching ON FAILURE QUARANTINE TO <table> "
                    + "clause on the statement.");
        }

        return new ColumnQualityValidator(context, logger, statement, ruleSets, routing);
    }

    /// <summary>
    /// Rejects a composite rule naming a column the statement does not project. Row lookup by name
    /// yields null for an absent column, and a NULL key part skips the rule — so without this a
    /// single typo turns <c>UNIQUE WITH</c> or <c>EXISTS WITH</c> into a rule that reports clean
    /// because it never ran on any row. Identifier matching is case-insensitive; only *values*
    /// honor SET CASE_SENSITIVE.
    /// </summary>
    private static void ValidateCompositeColumns(
        IReadOnlyList<ColumnRuleSet> ruleSets, IReadOnlyList<string> outputColumnNames)
    {
        if (outputColumnNames.Count == 0) return;
        var projectedNames = new HashSet<string>(outputColumnNames, StringComparer.OrdinalIgnoreCase);

        foreach (var rule in ruleSets.SelectMany(rs => rs.Bindings).SelectMany(b => b.Rules).FlattenAll())
        {
            (IReadOnlyList<string> Columns, string Form)? composite = rule switch
            {
                UniqueRule { CompositeColumns: { Count: > 0 } columns } => (columns, "UNIQUE WITH"),
                ExistsInRule { SourceColumns: { Count: > 0 } columns } => (columns, "EXISTS WITH"),
                _ => null
            };
            if (composite is not { } spec) continue;

            foreach (var column in spec.Columns)
            {
                if (projectedNames.Contains(column)) continue;
                throw new ExecutionException(
                    $"Data-quality rule \"{rule.Text}\": {spec.Form} names column '{column}', which " +
                    "this statement does not project. Add it to the SELECT list — an absent column " +
                    "would make the rule skip every row instead of failing.");
            }
        }
    }

    /// <summary>
    /// Prepares per-statement state (EXISTS IN key sets). Call once before the first row.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await BuildExistsInKeySetsAsync(cancellationToken);

        // Statement-invariant bounds are prepared before the first row so their rules can stay on
        // the synchronous, allocation-free validation path. Unknown nodes and volatile functions
        // never enter this set and continue to evaluate against every projected row.
        foreach (var rule in _rowInvariantBetweenRules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lower = await _context.EvaluateValue(rule.Lower, new Row());
            var upper = await _context.EvaluateValue(rule.Upper, new Row());
            _hoistedBetweenBounds[rule] = (lower, upper);
        }
    }

    // ── UNIQUE pre-pass ────────────────────────────────────────────────────

    private readonly Dictionary<int, Dictionary<string, UniqueGroup>> _uniqueGroups = new();
    private List<UniqueRuleEntry>? _uniqueRuleEntries;
    private UniqueKeySpill? _uniqueKeySpill;
    private bool _uniquePrePassComplete;

    /// <summary>
    /// Pre-pass step: records this row's key (and order-key, for UNIQUE_FIRST/LAST) for every
    /// UNIQUE rule. Called once per row over the spilled stream before validation begins.
    /// </summary>
    public async Task CollectUniqueKeysAsync(Row projected, long rowOrdinal, CancellationToken cancellationToken = default)
    {
        foreach (var entry in UniqueRules())
        {
            var key = BuildUniqueKey(entry.Rule, entry.RuleSet, projected);
            if (key == null) continue; // NULL keys skip UNIQUE, like every non-NOT NULL rule

            var orderKey = entry.Rule.OrderKey != null
                ? await _context.EvaluateValue(entry.Rule.OrderKey, projected)
                : null;

            // Only UNIQUE_FIRST/LAST need a row identity, to break ties on the order key. Plain
            // UNIQUE fails every row of a duplicated group, so it never asks which one to keep —
            // and the identity is a rendering of the whole row, so computing and spilling one per
            // row roughly doubled what the pre-pass writes to disk in exchange for nothing.
            var identity = entry.Rule.Mode == UniqueMode.All
                ? string.Empty
                : RowIdentity(projected, rowOrdinal);

            _uniqueKeySpill ??= await UniqueKeySpill.CreateAsync(_context, _context.ExternalHashPartitions);
            await _uniqueKeySpill.WriteAsync(entry.Id, key, identity, orderKey, _context.CaseSensitiveComparison);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>Closes the pre-pass; rows may now be validated against the collected key groups.</summary>
    public void FinalizeUniquePrePass()
    {
        FinalizeUniquePrePassAsync().GetAwaiter().GetResult();
    }

    public async Task FinalizeUniquePrePassAsync()
    {
        if (_uniquePrePassComplete) return;

        if (_uniqueKeySpill != null)
        {
            await _uniqueKeySpill.CloseWritersAsync();
            try
            {
                await foreach (var partition in _uniqueKeySpill.ReadPartitionsAsync())
                    ReduceUniquePartition(partition);
            }
            finally
            {
                _uniqueKeySpill.Delete();
                _uniqueKeySpill = null;
            }
        }

        _uniquePrePassComplete = true;
        foreach (var entry in UniqueRules())
        {
            if (!_uniqueGroups.TryGetValue(entry.Id, out var groups)) continue;
            int duplicates = groups.Count;
            if (duplicates > 0)
                _logger.Debug("[DQ] UNIQUE pre-pass for \"{Rule}\": {Duplicates} duplicated key(s) across {Keys} distinct key(s).",
                    entry.Rule.Text, duplicates, groups.Count);
        }
    }

    private IReadOnlyList<UniqueRuleEntry> UniqueRules()
    {
        if (_uniqueRuleEntries != null) return _uniqueRuleEntries;

        var entries = new List<UniqueRuleEntry>();
        foreach (var ruleSet in _ruleSets)
        {
            foreach (var rule in ruleSet.Bindings.SelectMany(b => b.Rules).FlattenAll().OfType<UniqueRule>())
                entries.Add(new UniqueRuleEntry(entries.Count, ruleSet, rule));
        }
        _uniqueRuleEntries = entries;
        return entries;
    }

    private Dictionary<object, UniqueRuleEntry>? _uniqueEntryByRule;

    /// <summary>
    /// Resolves a rule back to its pre-pass entry. Keyed on the rule *instance*: this runs once per
    /// row per UNIQUE rule, and the previous linear scan compared records by value, so two rules
    /// that happened to be written identically cost a deep <c>Expression</c> comparison every row.
    /// Parsing yields a distinct instance per column, so reference identity is exact here.
    /// </summary>
    private UniqueRuleEntry FindUniqueEntry(ColumnRuleSet ruleSet, UniqueRule rule)
    {
        if (_uniqueEntryByRule == null)
        {
            _uniqueEntryByRule = new Dictionary<object, UniqueRuleEntry>(ReferenceEqualityComparer.Instance);
            foreach (var entry in UniqueRules())
                _uniqueEntryByRule[entry.Rule] = entry;
        }
        return _uniqueEntryByRule[rule];
    }

    private void ReduceUniquePartition(IReadOnlyList<Row> records)
    {
        var local = new Dictionary<int, Dictionary<string, UniqueGroup>>();
        foreach (var record in records)
        {
            var ruleId = Convert.ToInt32(record["RuleId"], CultureInfo.InvariantCulture);
            var entry = UniqueRules()[ruleId];
            var key = Stringify(record["Key"]);
            if (!local.TryGetValue(ruleId, out var groups))
                local[ruleId] = groups = new Dictionary<string, UniqueGroup>(KeyComparer(_context.CaseSensitiveComparison));
            if (!groups.TryGetValue(key, out var group))
                groups[key] = group = new UniqueGroup();

            group.Count++;
            if (entry.Rule.Mode == UniqueMode.All) continue;

            group.ConsiderKeeper(
                entry.Rule.Mode,
                record["OrderKey"],
                Stringify(record["Identity"]),
                _context.CompareConstants);
        }

        foreach (var (ruleId, groups) in local)
        {
            foreach (var (key, group) in groups)
            {
                if (group.Count <= 1) continue;
                if (!_uniqueGroups.TryGetValue(ruleId, out var duplicateGroups))
                    _uniqueGroups[ruleId] = duplicateGroups = new Dictionary<string, UniqueGroup>(KeyComparer(_context.CaseSensitiveComparison));
                duplicateGroups[key] = group;
            }
        }
    }

    /// <summary>
    /// The uniqueness key for one row: the column's own projected value, or — for
    /// <c>UNIQUE WITH (a, b)</c> — the tuple of the named projected columns.
    /// Returns null when any key part is NULL (UNIQUE skips NULLs like every non-NOT NULL rule).
    /// </summary>
    private static string? BuildUniqueKey(UniqueRule rule, ColumnRuleSet ruleSet, Row projected)
    {
        if (rule.CompositeColumns is not { Count: > 0 } composite)
        {
            var single = projected[ruleSet.OutputIndex];
            return single is null or DBNull ? null : Stringify(single);
        }

        return BuildKeyTuple(composite, column => projected[column]);
    }

    /// <summary>
    /// Stable per-row identity used to break ties on the UNIQUE_FIRST/LAST order key: the full
    /// projected row content. When two rows share both the key and the order-key value, the
    /// deterministic winner is the lexicographically smallest identity, so repeated runs over the
    /// same data always keep the same row.
    /// </summary>
    private static string RowIdentity(Row projected, long rowOrdinal)
    {
        var builder = new StringBuilder();
        foreach (var (name, value) in projected.Columns.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            builder.Append(name).Append('=').Append(Stringify(value)).Append(' ');
        builder.Append("__dq_ordinal=").Append(rowOrdinal.ToString("D20", CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    /// <summary>Per-key state gathered by the pre-pass.</summary>
    private sealed class UniqueGroup
    {
        public int Count;
        public object? KeeperOrderKey;
        public string? KeeperIdentity;

        public void ConsiderKeeper(UniqueMode mode, object? orderKey, string identity, Func<object?, object?, int> compare)
        {
            if (KeeperIdentity == null)
            {
                KeeperOrderKey = orderKey;
                KeeperIdentity = identity;
                return;
            }

            int comparison = compare(orderKey, KeeperOrderKey);
            bool wins = mode == UniqueMode.First ? comparison < 0 : comparison > 0;
            // Deterministic tiebreak when the order key does not separate the rows.
            if (comparison == 0)
                wins = string.CompareOrdinal(identity, KeeperIdentity) < 0;
            if (wins)
            {
                KeeperOrderKey = orderKey;
                KeeperIdentity = identity;
            }
        }
    }

    /// <summary>
    /// True when this row violates <paramref name="rule"/>. Must run only after
    /// <see cref="FinalizeUniquePrePassAsync"/> has resolved all duplicates.
    /// </summary>
    private bool ViolatesUnique(UniqueRule rule, ColumnRuleSet ruleSet, Row projected, long? rowOrdinal)
    {
        if (!_uniquePrePassComplete)
            throw new InvalidOperationException("UNIQUE rules require a completed pre-pass.");

        var key = BuildUniqueKey(rule, ruleSet, projected);
        if (key == null) return false; // NULL keys skip UNIQUE

        var entry = FindUniqueEntry(ruleSet, rule);
        if (!_uniqueGroups.TryGetValue(entry.Id, out var duplicateGroups)) return false;
        if (!duplicateGroups.TryGetValue(key, out var group)) return false;

        // Mode.All: any duplicate makes every row of that group invalid.
        if (rule.Mode == UniqueMode.All) return true;

        // Mode.First/Last: the winner is the row whose identity matches the group's keeper.
        if (rowOrdinal is not { } ordinal)
            throw new ExecutionException(
                $"UNIQUE_{rule.Mode.ToString().ToUpperInvariant()} requires row ordinals during validation.");

        var identity = RowIdentity(projected, ordinal);
        return !string.Equals(identity, group.KeeperIdentity, StringComparison.Ordinal);
    }

    // ── Pipeline wrapping ──────────────────────────────────────────────────

    private long _processedRowCount;

    /// <summary>
    /// Validates a single row as it streams through. Returns true if the row is accepted (clean or warned),
    /// or false if the row was diverted to quarantine. Aborts if THROW action was specified.
    /// </summary>
    public ValueTask<bool> TryAcceptRowAsync(
        Row input,
        Row projected,
        long? rowOrdinal = null,
        CancellationToken cancellationToken = default)
    {
        _context.DataQuality.RecordRowValidated();
        long ordinal = rowOrdinal ?? _processedRowCount++;
        if (!_hasExpressionRules)
        {
            var failure = EvaluateRowSynchronously(input, projected, ordinal);
            if (failure == null || _context.DataQualityDryRun) return ValueTask.FromResult(true);
            return HandleFailureAsync(input, failure, cancellationToken);
        }

        return EvaluateAndHandleAsync(input, projected, ordinal, cancellationToken);
    }

    private async ValueTask<bool> EvaluateAndHandleAsync(
        Row input,
        Row projected,
        long ordinal,
        CancellationToken cancellationToken)
    {
        var failure = await EvaluateRowAsync(input, projected, ordinal, cancellationToken);
        if (failure == null || _context.DataQualityDryRun) return true;
        return await HandleFailureAsync(input, failure, cancellationToken);
    }

    private async ValueTask<bool> HandleFailureAsync(Row input, RowFailure failure, CancellationToken cancellationToken)
    {
        switch (failure.Action)
        {
            case FailAction.Warn:
                await WarnAsync(input, failure, cancellationToken);
                return true;
            case FailAction.Quarantine:
                await QuarantineAsync(input, failure, cancellationToken);
                return false;
            case FailAction.Throw:
                // Thrown inside EvaluateRow* unless DryRun was set; DryRun records it and lets the row pass
                return true;
            default:
                return true;
        }
    }

    public ValueTask<bool> TryAcceptRowAsync(Row input, Row projected, CancellationToken cancellationToken) =>
        TryAcceptRowAsync(input, projected, null, cancellationToken);

    /// <summary>
    /// Completes validation after all rows have been processed, flushing buffers and emitting aggregated diagnostics.
    /// </summary>
    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        await FlushAsync(cancellationToken);
        EmitAggregatedDiagnostics();
    }

    /// <summary>
    /// Validates every projected row, applying the statement's failure routing (WARNs recorded,
    /// QUARANTINEs diverted to the target, THROWs aborting). Clean and WARNed rows continue down
    /// the returned stream.
    /// </summary>
    public async IAsyncEnumerable<Row> WrapAsync(
        IAsyncEnumerable<Row> source,
        Func<Row, Row> projectRow,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        long rowOrdinal = 0;
        await foreach (var row in source.WithCancellation(cancellationToken))
        {
            _context.DataQuality.RecordRowValidated();
            var projected = projectRow(row);
            var failure = _hasExpressionRules
                ? await EvaluateRowAsync(row, projected, rowOrdinal, cancellationToken)
                : EvaluateRowSynchronously(row, projected, rowOrdinal);

            rowOrdinal++;
            if (failure == null || _context.DataQualityDryRun)
            {
                yield return projected;
                continue;
            }

            switch (failure.Action)
            {
                case FailAction.Warn:
                    await WarnAsync(row, failure, cancellationToken);
                    yield return projected;
                    break;
                case FailAction.Quarantine:
                    await QuarantineAsync(row, failure, cancellationToken);
                    break;
                case FailAction.Throw:
                    // Thrown inside EvaluateRow* unless DryRun was set; DryRun records it and lets the row pass
                    yield return projected;
                    break;
            }
        }

        await FlushAsync(cancellationToken);
        EmitAggregatedDiagnostics();
    }

    /// <summary>
    /// Synchronous row check — the fast path taken when every rule is a pure per-row predicate
    /// (NOT NULL, MATCHES, IN, length, castable, comparison, or row-invariant BETWEEN).
    /// </summary>
    private RowFailure? EvaluateRowSynchronously(Row input, Row projected, long? rowOrdinal)
    {
        RowFailure? decided = null;
        for (var ruleSetIndex = 0; ruleSetIndex < _ruleSets.Count; ruleSetIndex++)
        {
            var ruleSet = _ruleSets[ruleSetIndex];
            var value = projected[ruleSet.OutputIndex];
            for (var bindingIndex = 0; bindingIndex < ruleSet.Bindings.Count; bindingIndex++)
            {
                var binding = ruleSet.Bindings[bindingIndex];
                for (var ruleIndex = 0; ruleIndex < binding.Rules.Count; ruleIndex++)
                {
                    var rule = binding.Rules[ruleIndex];
                    var passed = RulePassesSynchronously(rule, value, projected, ruleSet, rowOrdinal);
                    if (passed) continue;

                    _context.DataQuality.RecordFailure(
                        _statement.IntoTable?.ToString(), ruleSet.ColumnName, rule.Text, binding.Action,
                        value, ruleSet.IsPii, ruleSet.Owner);

                    var reason = DescribeFailure(rule, value, ruleSet);
                    if (binding.Action == FailAction.Throw && !_context.DataQualityDryRun)
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
        if (decided != null && _context.DataQualityDryRun)
            _context.DataQuality.RecordRowDryRunAffected();
        return decided;
    }

    private async ValueTask<RowFailure?> EvaluateRowAsync(
        Row input,
        Row projected,
        long? rowOrdinal,
        CancellationToken cancellationToken)
    {
        RowFailure? decided = null;

        // These collections are exposed as IReadOnlyList. foreach would obtain their interface
        // enumerators and box the List<T>.Enumerator once per nesting level, per row. Indexing
        // keeps the passing-row path allocation-free while preserving declaration order.
        for (var ruleSetIndex = 0; ruleSetIndex < _ruleSets.Count; ruleSetIndex++)
        {
            var ruleSet = _ruleSets[ruleSetIndex];
            var value = projected[ruleSet.OutputIndex];
            for (var bindingIndex = 0; bindingIndex < ruleSet.Bindings.Count; bindingIndex++)
            {
                var binding = ruleSet.Bindings[bindingIndex];
                for (var ruleIndex = 0; ruleIndex < binding.Rules.Count; ruleIndex++)
                {
                    var rule = binding.Rules[ruleIndex];
                    bool passed = await RulePassesAsync(rule, value, projected, ruleSet, rowOrdinal);
                    if (passed) continue;

                    _context.DataQuality.RecordFailure(
                        _statement.IntoTable?.ToString(), ruleSet.ColumnName, rule.Text, binding.Action,
                        value, ruleSet.IsPii, ruleSet.Owner);

                    var reason = DescribeFailure(rule, value, ruleSet);
                    // A dry run must not abort the load — the point is to learn the impact of a
                    // rule that is not trusted yet.
                    if (binding.Action == FailAction.Throw && !_context.DataQualityDryRun)
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
        if (decided != null && _context.DataQualityDryRun)
            _context.DataQuality.RecordRowDryRunAffected();
        return decided;
    }

    /// <summary>
    /// The per-row verdict for every rule except UNIQUE, which the whole-input pre-pass decides.
    ///
    /// <para>NOT NULL is the only rule that fails on NULL; every other rule skips NULL values (the
    /// SQL CHECK-constraint convention) -- pair with NOT NULL explicitly to reject them.</para>
    /// </summary>
    private bool RulePassesSynchronously(ColumnRule rule, object? value, Row projected, ColumnRuleSet? ruleSet = null, long? rowOrdinal = null)
    {
        if (rule is NotNullRule) return value is not null and not DBNull;
        if (value is null or DBNull)
        {
            if (rule is AndRule andRule)
            {
                for (int i = 0; i < andRule.Operands.Count; i++)
                {
                    if (!RulePassesSynchronously(andRule.Operands[i], value, projected, ruleSet, rowOrdinal))
                        return false;
                }
                return true;
            }
            if (rule is OrRule orRule)
            {
                for (int i = 0; i < orRule.Operands.Count; i++)
                {
                    if (RulePassesSynchronously(orRule.Operands[i], value, projected, ruleSet, rowOrdinal))
                        return true;
                }
                return false;
            }
            return true;
        }

        switch (rule)
        {
            case AndRule andRule:
                for (int i = 0; i < andRule.Operands.Count; i++)
                {
                    if (!RulePassesSynchronously(andRule.Operands[i], value, projected, ruleSet, rowOrdinal))
                        return false;
                }
                return true;

            case OrRule orRule:
                for (int i = 0; i < orRule.Operands.Count; i++)
                {
                    if (RulePassesSynchronously(orRule.Operands[i], value, projected, ruleSet, rowOrdinal))
                        return true;
                }
                return false;

            case UniqueRule unique when ruleSet != null:
                return !ViolatesUnique(unique, ruleSet, projected, rowOrdinal);

            case NotBlankRule:
                return !string.IsNullOrWhiteSpace(Stringify(value));

            case LengthRule length:
                {
                    var characters = Stringify(value).Length;
                    return characters >= length.MinLength
                        && (length.MaxLength is not { } max || characters <= max);
                }

            case CastableRule castable:
                return CastablePasses(castable, value);

            case MatchesRule matches:
                return GetRegex(matches).IsMatch(Stringify(value)) != matches.Negated;

            case ComparisonRule comparison:
                if (!TryToDecimal(value, out var numeric)) return false;
                return comparison.Op switch
                {
                    CompareOp.GreaterOrEqual => numeric >= comparison.Value,
                    CompareOp.LessOrEqual => numeric <= comparison.Value,
                    CompareOp.Greater => numeric > comparison.Value,
                    CompareOp.Less => numeric < comparison.Value,
                    _ => numeric == comparison.Value
                };

            case InListRule inList:
                return InListContains(inList, value) != inList.Negated;

            case ExistsInRule existsIn:
                return ExistsInPasses(existsIn, value, projected);

            case BetweenRule between when _hoistedBetweenBounds.TryGetValue(between, out var bounds):
                return BetweenPasses(value, bounds.Lower, bounds.Upper);

            // A rule form the runtime does not implement must not report the data as clean. This
            // is unreachable through the parser; it exists so that adding a ColumnRule record and
            // forgetting this switch fails loudly instead of passing every row.
            default:
                throw new ExecutionException(
                    $"Data-quality rule \"{rule.Text}\" parsed as {rule.GetType().Name}, which this "
                    + "engine version does not enforce.");
        }
    }

    /// <summary>
    /// EXPR and BETWEEN are the only rules that need the evaluator, so this path handles those two
    /// forms and defers the rest to <see cref="RulePassesSynchronously"/> rather than restating
    /// them — two copies of the rule semantics is two places for them to drift.
    /// </summary>
    private async ValueTask<bool> RulePassesAsync(ColumnRule rule, object? value, Row projected, ColumnRuleSet? ruleSet = null, long? rowOrdinal = null)
    {
        switch (rule)
        {
            case AndRule andRule:
                for (int i = 0; i < andRule.Operands.Count; i++)
                {
                    if (!await RulePassesAsync(andRule.Operands[i], value, projected, ruleSet, rowOrdinal))
                        return false;
                }
                return true;

            case OrRule orRule:
                for (int i = 0; i < orRule.Operands.Count; i++)
                {
                    if (await RulePassesAsync(orRule.Operands[i], value, projected, ruleSet, rowOrdinal))
                        return true;
                }
                return false;

            case UniqueRule unique when ruleSet != null:
                return !ViolatesUnique(unique, ruleSet, projected, rowOrdinal);

            case ExprRule expr:
                return await _context.EvaluateCondition(expr.Predicate, projected);

            case BetweenRule between when value is not null and not DBNull:
                return await BetweenPassesAsync(between, value, projected);

            case BetweenRule:
                return true; // NULL skips it, like every rule but NOT NULL

            default:
                return RulePassesSynchronously(rule, value, projected, ruleSet, rowOrdinal);
        }
    }

    /// <summary>
    /// Evaluates both bounds against the projected row and compares with the engine's type-aware
    /// comparison, so a date range compares as dates rather than as rendered text. A NULL bound
    /// makes the range unknown and the rule skips the row, which is how SQL's own BETWEEN behaves
    /// — a rule that failed every row because <c>@RunDate</c> was unset would report the data as
    /// broken when the script is.
    /// </summary>
    private async ValueTask<bool> BetweenPassesAsync(BetweenRule rule, object? value, Row projected)
    {
        object? lower;
        object? upper;

        if (!_hoistedBetweenBounds.TryGetValue(rule, out var bounds))
        {
            lower = await _context.EvaluateValue(rule.Lower, projected);
            upper = await _context.EvaluateValue(rule.Upper, projected);
        }
        else
        {
            lower = bounds.Lower;
            upper = bounds.Upper;
        }

        return BetweenPasses(value, lower, upper);
    }

    private bool BetweenPasses(object? value, object? lower, object? upper)
    {
        if (lower is null or DBNull || upper is null or DBNull) return true;
        return _context.CompareConstants(value, lower) >= 0
            && _context.CompareConstants(value, upper) <= 0;
    }

    internal static bool IsRowInvariant(Expression expr) => expr switch
    {
        LiteralExpression => true,
        ParameterExpression => true,
        VariableExpression => true,
        UnaryExpression u => IsRowInvariant(u.Expression),
        BinaryExpression b => IsRowInvariant(b.Left) && IsRowInvariant(b.Right),
        ListExpression l => l.Items.All(IsRowInvariant),
        FunctionCallExpression f => IsDeterministicFunction(f.FunctionName)
            && f.Arguments.Select((argument, index) => IsRowInvariantFunctionArgument(f, argument, index)).All(x => x),
        IsNullExpression n => IsRowInvariant(n.Expression),
        IsDistinctFromExpression d => IsRowInvariant(d.Left) && IsRowInvariant(d.Right),
        _ => false // IdentifierExpression, SubqueryExpression, and any unrecognized nodes are not hoistable
    };

    private static bool IsRowInvariantFunctionArgument(
        FunctionCallExpression function,
        Expression argument,
        int index)
    {
        if (IsRowInvariant(argument)) return true;
        if (index != 0 || argument is not IdentifierExpression) return false;

        // DATEADD(DAY, ...), DATEDIFF(DAY, ...), etc. parse the date-part keyword as an
        // identifier-shaped expression. It names no row column, but only in this exact slot.
        return function.FunctionName.ToUpperInvariant() is
            "DATEADD" or "DATEDIFF" or "DATE_TRUNC" or "DATE_PART" or "DATENAME";
    }

    internal static bool IsDeterministicFunction(string functionName) => functionName.ToUpperInvariant() switch
    {
        // Safe to hoist once per statement per the row-invariant design decision
        "GETDATE" or "SYSDATETIME" or "SYSUTCDATETIME" => true,
        // Common deterministic dates
        "DATEADD" or "DATEDIFF" or "DATE_TRUNC" or "DATE_PART" or "DAYOFWEEK" or "DAYOFYEAR" or "DATENAME" or "EOMONTH" or "DATEFROMPARTS" => true,
        // Null checks
        "ISNULL" or "COALESCE" or "NULLIF" => true,
        // Casts
        "CAST" or "CONVERT" or "TRY_CAST" or "TRY_CONVERT" => true,
        // Strings
        "UPPER" or "LOWER" or "LEN" or "SUBSTRING" or "TRIM" or "LTRIM" or "RTRIM" or "REPLACE" or "CHARINDEX" or "LEFT" or "RIGHT" or "CONCAT" => true,
        // Math
        "ABS" or "ROUND" or "CEILING" or "FLOOR" => true,
        _ => false
    };

    // ── CASTABLE AS ────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the engine's own conversion — the one behind <c>TRY_CAST</c> — and treats a throw or a
    /// null result as a failure, then checks the declared width. Sharing the conversion is the
    /// point: a rule that accepted a value a later <c>CAST</c> rejects would be worse than no rule.
    /// </summary>
    private bool CastablePasses(CastableRule rule, object? value)
    {
        object? converted;
        try
        {
            converted = _context.CastToType(value, rule.DeclaredType);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }

        if (converted is null or DBNull) return false;
        if (rule.Precision is not { } precision) return true;

        // The shared converter ignores DECIMAL(p,s) and VARCHAR(n) widths, so they are checked here.
        // Leaving them unchecked would let a declaration that reads as a constraint verify nothing.
        return rule.Scale is { } scale
            ? converted is decimal number && FitsPrecisionScale(number, precision, scale)
            : Stringify(converted).Length <= precision;
    }

    /// <summary>
    /// SQL's <c>DECIMAL(p, s)</c>: at most <paramref name="scale"/> digits after the point and at
    /// most <c>p - s</c> before it. Trailing zeros do not count — the DECIMAL converter normalizes
    /// 123 to 123.0, so counting the stored scale would reject a whole number against a scale of 0.
    /// </summary>
    private static bool FitsPrecisionScale(decimal value, int precision, int scale)
    {
        var text = Math.Abs(StripTrailingZeros(value)).ToString(CultureInfo.InvariantCulture);
        var point = text.IndexOf('.');

        var fractionDigits = point < 0 ? 0 : text.Length - point - 1;
        var integerText = point < 0 ? text : text[..point];
        var integerDigits = integerText == "0" ? 0 : integerText.Length;

        return fractionDigits <= scale && integerDigits <= precision - scale;
    }

    /// <summary>Dividing by one at maximum scale drops insignificant trailing zeros.</summary>
    private static decimal StripTrailingZeros(decimal value) =>
        value / 1.000000000000000000000000000000m;

    // ── EXISTS IN key sets ─────────────────────────────────────────────────

    /// <summary>
    /// Builds each referenced table's key set once per statement, then probes per row. Reference
    /// tables are dimension-sized by nature; the build honors SET CASE_SENSITIVE. Composite
    /// (<c>EXISTS WITH</c>) references build a tuple key over the reference columns in order.
    /// </summary>
    private async Task BuildExistsInKeySetsAsync(CancellationToken cancellationToken)
    {
        var references = _ruleSets
            .SelectMany(rs => rs.Bindings)
            .SelectMany(b => b.Rules)
            .FlattenAll()
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
                    // A reference tuple with a NULL part can never match a probe (a probe with a
                    // NULL part skips the rule), so it never belongs in the key set.
                    var referenceKey = BuildKeyTuple(reference.KeyColumns, column => row[column]);
                    if (referenceKey != null) keys.Add(referenceKey);
                }
            }
            _existsInKeys[key] = keys;
            _logger.Debug("EXISTS IN {Table}({Columns}): built key set of {Count} tuple(s).",
                reference.Table, string.Join(", ", reference.KeyColumns), keys.Count);
        }
    }

    /// <summary>
    /// Probes one row against the reference key set. Both the single-column and composite forms
    /// skip the rule when any probe part is NULL, like every rule except <c>NOT NULL</c>.
    /// </summary>
    private bool ExistsInPasses(ExistsInRule rule, object? value, Row projected)
    {
        var probe = rule.SourceColumns is { Count: > 0 } sources
            ? BuildKeyTuple(sources, column => projected[column])
            : Stringify(value);
        if (probe == null) return true;

        // Probe the HashSet's own comparer. Enumerable.Contains with an explicit comparer bypasses
        // the set and scans linearly, which turns a dimension lookup into O(rows × keys).
        return _existsInKeys.TryGetValue(ExistsInKey(rule), out var keys) && keys.Contains(probe);
    }

    /// <summary>
    /// Joins the named columns' values into one tuple key, or returns null when any part is NULL.
    /// Parts are separated rather than concatenated, so ("ab", "c") and ("a", "bc") cannot collide.
    /// A one-column tuple renders as the bare value, which is what lets the single-column and
    /// composite EXISTS forms share one key set and one probe path.
    /// </summary>
    private static string? BuildKeyTuple(IReadOnlyList<string> columns, Func<string, object?> read)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < columns.Count; i++)
        {
            var part = read(columns[i]);
            if (part is null or DBNull) return null;
            if (i > 0) builder.Append(KeyPartSeparator);
            builder.Append(Stringify(part));
        }
        return builder.ToString();
    }

    private static string ExistsInKey(ExistsInRule rule) =>
        $"{rule.Table}({string.Join(",", rule.KeyColumns)})";

    // ── Routing ────────────────────────────────────────────────────────────

    private async Task QuarantineAsync(Row input, RowFailure failure, CancellationToken cancellationToken)
    {
        var clause = _routing[FailAction.Quarantine];
        var captureInput = input.DataQualityReplayProvenance?.SourceRow ?? input;
        _quarantineWriter ??= new QuarantineWriter(_context, clause.Target!, DataQualityColumns.QuarantinedStatus, includeTargetWritten: false);
        await RecordQuarantineManifestAsync(clause.Target!, input, cancellationToken);
        await _quarantineWriter.WriteAsync(captureInput, failure, cancellationToken);
        _context.DataQuality.RecordRowQuarantined();
    }

    private async Task RecordQuarantineManifestAsync(string target, Row input, CancellationToken cancellationToken)
    {
        if (_quarantineManifestRecorded) return;
        _quarantineManifestRecorded = true;

        // HANDLING = SCRIPT: the script deals with these rows inside this run, so there is nothing
        // for a steward to pick up afterwards. The manifest is what makes a quarantine target
        // visible to the Portal queue and replayable by REPLAY QUARANTINE, so not writing one is
        // how the mode stays out of both. Counts still reach the run's quality metrics.
        if (_routing[FailAction.Quarantine].Handling == QuarantineHandling.Script)
        {
            _logger.Debug(
                "[DQ] Quarantine target '{Target}' is script-handled; no replay manifest recorded.", target);
            return;
        }

        var provider = _context.JobMetrics;
        if (provider == null || string.IsNullOrWhiteSpace(_context.JobName)) return;

        var sourceTables = _statement.GetSourceTables()
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var replayProvenance = input.DataQualityReplayProvenance;
        var manifestInput = replayProvenance?.SourceRow ?? input;
        var inputColumns = manifestInput.Columns.Keys
            .Where(name => !DataQualityColumns.IsDataQualityColumn(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string? nonReplayableReason = null;
        string replayMode = replayProvenance == null ? "single-table" : "probe-join";
        string? probeSourceTable = replayProvenance?.SourceTable;
        string? joinBuildTable = replayProvenance?.JoinBuildTable
            ?? (replayProvenance != null && _statement.Joins.Count == 1
            ? _statement.Joins[0].Table.GetSourceTables().FirstOrDefault() ?? _statement.Joins[0].Table.ToSql()
            : null);
        bool? joinObservedN1 = replayProvenance == null ? null : replayProvenance.JoinObservedN1;
        string? joinNonReplayableReason = null;

        if (string.IsNullOrWhiteSpace(_context.CurrentSectionLabel))
            nonReplayableReason = "quarantine replay requires an enclosing section label";
        else if (replayProvenance == null && sourceTables.Count == 0)
            nonReplayableReason = "quarantine source table could not be resolved";
        else if (replayProvenance != null)
        {
            joinNonReplayableReason = _statement.Joins.Count == 1 && replayProvenance.JoinObservedN1
                ? null
                : _statement.Joins.Count == 1
                    ? replayProvenance.JoinNonReplayableReason ?? "join replay requires an observed N:1 join gate"
                    : "join replay supports exactly one build-side join in this version";
            nonReplayableReason = joinNonReplayableReason;
        }
        else if (sourceTables.Count != 1)
            nonReplayableReason = "quarantine source spans a join; replay requires a single-table input in this version";

        // Target provenance: only an alias the run created from the governed catalog can be
        // reopened later through the same governed path. Anything else stays unknown, which the
        // Portal classifies as view-only rather than guessing.
        string? targetAlias = null;
        string? targetConnectorType = null;
        bool? targetIsCatalogBacked = null;
        var aliasSeparator = target.IndexOf('.');
        if (aliasSeparator > 0 && target[0] is not ('#' or '&'))
        {
            targetAlias = target[..aliasSeparator];
            if (_context.CatalogBackedConnections.TryGetValue(targetAlias, out var connectorType))
            {
                targetConnectorType = connectorType;
                targetIsCatalogBacked = true;
            }
            else
            {
                // Named a connection, but not one this run took from the catalog.
                targetIsCatalogBacked = false;
            }
        }

        var manifest = new QuarantineReplayManifest(
            JobName: _context.JobName!,
            ScriptPath: _context.CurrentScriptPath,
            SectionLabel: _context.CurrentSectionLabel,
            SourceTable: probeSourceTable ?? (sourceTables.Count == 1 ? sourceTables[0] : string.Join(",", sourceTables)),
            QuarantineTarget: target,
            IsReplayable: nonReplayableReason == null,
            NonReplayableReason: nonReplayableReason,
            InputColumns: inputColumns,
            InputSchemaFingerprint: ComputeInputSchemaFingerprint(inputColumns),
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            ReplayMode: replayMode,
            ProbeSourceTable: probeSourceTable,
            JoinBuildTable: joinBuildTable,
            JoinObservedN1: joinObservedN1,
            JoinNonReplayableReason: joinNonReplayableReason,
            TargetConnectionAlias: targetAlias,
            TargetConnectorType: targetConnectorType,
            TargetIsCatalogBacked: targetIsCatalogBacked);

        try
        {
            await provider.SaveQuarantineReplayManifestAsync(
                _context.ExecutionIdentity?.TenantId, manifest, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warning(
                "Data-quality quarantine manifest for '{Target}' was not persisted: {Message}",
                target,
                ex.Message);
        }
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

    /// <summary>
    /// Compiled patterns, keyed by rule <i>instance</i>. Keying on the record hashed the whole
    /// pattern string on every lookup — that is, once per row per MATCHES rule — to find an entry
    /// that never moves.
    /// </summary>
    private readonly Dictionary<object, Regex> _regexCache = new(ReferenceEqualityComparer.Instance);

    private Regex GetRegex(MatchesRule rule)
    {
        if (_regexCache.TryGetValue(rule, out var cached)) return cached;
        var compiled = rule.Compile(_context.CaseSensitiveComparison);
        _regexCache[rule] = compiled;
        return compiled;
    }

    /// <summary>
    /// The candidate list of an <c>IN</c> rule, prepared once: each literal's rendered text and its
    /// decimal form. Both are properties of the rule, not of the row, but the naive comparison
    /// recomputed them per candidate *per row* — so an N-item list rendered the row's value N times.
    /// </summary>
    private sealed record InListCandidates(object?[] Values, string[] Text, decimal?[] Numbers, bool[] IsNumericLiteral);

    private Dictionary<object, InListCandidates>? _inListCandidates;

    private InListCandidates PrepareCandidates(InListRule rule)
    {
        _inListCandidates ??= new Dictionary<object, InListCandidates>(ReferenceEqualityComparer.Instance);
        if (_inListCandidates.TryGetValue(rule, out var cached)) return cached;

        var values = rule.Values.ToArray();
        var text = new string[values.Length];
        var numbers = new decimal?[values.Length];
        var isNumericLiteral = new bool[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            text[i] = Stringify(values[i]);
            numbers[i] = TryToDecimal(values[i], out var number) ? number : null;
            isNumericLiteral[i] = values[i] is decimal || IsNumeric(values[i]);
        }

        var prepared = new InListCandidates(values, text, numbers, isNumericLiteral);
        _inListCandidates[rule] = prepared;
        return prepared;
    }

    /// <summary>
    /// Membership for <c>IN</c>/<c>NOT IN</c>, preserving the original pairwise rule: when either
    /// side is a number the two are compared as decimals, and any pair that cannot both convert
    /// falls back to a string comparison honoring SET CASE_SENSITIVE. The row's own rendered text
    /// is materialized at most once, and only if some pair actually reaches the string path.
    /// </summary>
    private bool InListContains(InListRule rule, object? value)
    {
        var candidates = PrepareCandidates(rule);
        var comparison = _context.CaseSensitiveComparison
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        bool valueIsNumeric = value is decimal || IsNumeric(value);
        bool valueIsDecimal = TryToDecimal(value, out var valueNumber);
        string? valueText = null;

        for (var i = 0; i < candidates.Values.Length; i++)
        {
            if ((candidates.IsNumericLiteral[i] || valueIsNumeric)
                && candidates.Numbers[i] is { } candidateNumber
                && valueIsDecimal)
            {
                if (candidateNumber == valueNumber) return true;
                continue;
            }

            valueText ??= Stringify(value);
            if (string.Equals(candidates.Text[i], valueText, comparison)) return true;
        }
        return false;
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

    private static string ComputeInputSchemaFingerprint(IReadOnlyList<string> inputColumns)
    {
        var schema = string.Join('\n', inputColumns.Select(c => c.ToLowerInvariant()));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(schema)));
    }

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
        bool IsPii,
        string? Owner = null);

    /// <summary>
    /// The column's accountable person, preferring the most specific tag available. These already
    /// exist and already propagate through lineage; alerting just needs to use them so a failure
    /// reaches whoever can fix it rather than only a shared channel.
    /// </summary>
    private static string? ResolveOwner(IReadOnlyDictionary<string, string> metadata)
    {
        foreach (var tag in new[] { "steward", "owner", "contact" })
        {
            if (metadata.TryGetValue(tag, out var raw))
            {
                var value = ColumnRuleParser.Unquote(raw).Trim();
                if (value.Length > 0) return value;
            }
        }
        return null;
    }

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

    private sealed record UniqueRuleEntry(int Id, ColumnRuleSet RuleSet, UniqueRule Rule);

    private sealed class UniqueKeySpill
    {
        private readonly IExecutionContext _context;
        private readonly string[] _names;
        private readonly ISpillWriter[] _writers;
        private bool _closed;

        private UniqueKeySpill(IExecutionContext context, string[] names, ISpillWriter[] writers)
        {
            _context = context;
            _names = names;
            _writers = writers;
        }

        public static async Task<UniqueKeySpill> CreateAsync(IExecutionContext context, int requestedPartitions)
        {
            var partitionCount = Math.Max(1, requestedPartitions);
            var operationId = Guid.NewGuid().ToString("N");
            var names = new string[partitionCount];
            var writers = new ISpillWriter[partitionCount];
            for (var i = 0; i < partitionCount; i++)
            {
                names[i] = $"dq_unique_{operationId}_{i}.tmp";
                writers[i] = await context.SpillStore.CreateWriterAsync(names[i]);
            }
            return new UniqueKeySpill(context, names, writers);
        }

        public async Task WriteAsync(int ruleId, string key, string identity, object? orderKey, bool caseSensitive)
        {
            var routeKey = caseSensitive ? key : key.ToUpperInvariant();
            var partition = (HashCode.Combine(ruleId, routeKey) & 0x7fffffff) % _writers.Length;
            var record = new Row
            {
                ["RuleId"] = ruleId,
                ["Key"] = key,
                ["Identity"] = identity,
                ["OrderKey"] = orderKey
            };
            await _writers[partition].WriteRowAsync(record);
        }

        public async Task CloseWritersAsync()
        {
            if (_closed) return;
            _closed = true;
            foreach (var writer in _writers)
                await writer.DisposeAsync();
        }

        public async IAsyncEnumerable<IReadOnlyList<Row>> ReadPartitionsAsync()
        {
            await CloseWritersAsync();
            foreach (var name in _names)
            {
                var rows = new List<Row>();
                await using var reader = await _context.SpillStore.CreateReaderAsync(name);
                await foreach (var row in reader.AsEnumerableAsync())
                    rows.Add(row);
                yield return rows;
            }
        }

        public void Delete()
        {
            foreach (var name in _names)
            {
                try { _context.SpillStore.DeleteChunk(name); }
                catch { /* best-effort cleanup */ }
            }
        }
    }
}
