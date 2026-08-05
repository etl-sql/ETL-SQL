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
            ruleSets.Add(new ColumnRuleSet(i, name, bindings, isPii, ResolveOwner(column.Metadata!)));
        }

        if (ruleSets.Count == 0) return null;

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
                    "@fail: 'QUARANTINE' requires a matching ON FAILURE QUARANTINE TO <table> clause on the statement.");
        }

        return new ColumnQualityValidator(context, logger, statement, ruleSets, routing);
    }

    /// <summary>
    /// Prepares per-statement state (EXISTS IN key sets). Call once before the first row.
    /// </summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        BuildExistsInKeySetsAsync(cancellationToken);

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
            var identity = RowIdentity(projected, rowOrdinal);
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
            foreach (var rule in ruleSet.Bindings.SelectMany(b => b.Rules).OfType<UniqueRule>())
                entries.Add(new UniqueRuleEntry(entries.Count, ruleSet, rule));
        }
        _uniqueRuleEntries = entries;
        return entries;
    }

    private UniqueRuleEntry FindUniqueEntry(ColumnRuleSet ruleSet, UniqueRule rule) =>
        UniqueRules().First(entry =>
            ReferenceEquals(entry.RuleSet, ruleSet)
            && (ReferenceEquals(entry.Rule, rule) || entry.Rule.Equals(rule)));

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

        var builder = new StringBuilder();
        foreach (var column in composite)
        {
            var value = projected[column];
            if (value is null or DBNull) return null;
            builder.Append(Stringify(value)).Append('');
        }
        return builder.ToString();
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
            builder.Append(name).Append('=').Append(Stringify(value)).Append('');
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
    /// True when this row violates the UNIQUE rule: for plain <c>UNIQUE</c>, any key seen more than
    /// once; for <c>UNIQUE_FIRST/LAST</c>, every row in a duplicated group except the keeper.
    /// </summary>
    private bool ViolatesUnique(UniqueRule rule, ColumnRuleSet ruleSet, Row projected, long? rowOrdinal)
    {
        if (!_uniquePrePassComplete) return false;
        var key = BuildUniqueKey(rule, ruleSet, projected);
        if (key == null) return false;
        var entry = FindUniqueEntry(ruleSet, rule);
        if (!_uniqueGroups.TryGetValue(entry.Id, out var groups) || !groups.TryGetValue(key, out var group)) return false;

        return rule.Mode == UniqueMode.All || rowOrdinal == null || RowIdentity(projected, rowOrdinal.Value) != group.KeeperIdentity;
    }

    /// <summary>
    /// Validates one row. Returns <c>false</c> when the row was quarantined — the caller must
    /// drop it from the output. Warned rows return <c>true</c> (they still reach the target);
    /// a THROW failure raises <see cref="ExecutionException"/> and aborts the statement.
    /// </summary>
    public async Task<bool> TryAcceptRowAsync(Row input, Row projected, CancellationToken cancellationToken = default)
        => await TryAcceptRowAsync(input, projected, rowOrdinal: null, cancellationToken);

    public async Task<bool> TryAcceptRowAsync(Row input, Row projected, long? rowOrdinal, CancellationToken cancellationToken = default)
    {
        // Timed only while profiling — and note that IsProfiling defaults to true, so these two
        // timestamp reads per row are the normal case rather than the exception. They are cheap
        // (tens of nanoseconds each) but not free; `SET PROFILE OFF` removes them, which is the
        // lever to reach for if the row pipeline is ever measured down to that level.
        var profiling = _context.Telemetry.IsProfiling;
        var startTicks = profiling ? Stopwatch.GetTimestamp() : 0L;
        try
        {
            return await ValidateRowAsync(input, projected, rowOrdinal, cancellationToken);
        }
        finally
        {
            if (profiling)
                _context.Telemetry.DataQualityValidationTicks += Stopwatch.GetTimestamp() - startTicks;
        }
    }

    private async Task<bool> ValidateRowAsync(Row input, Row projected, long? rowOrdinal, CancellationToken cancellationToken)
    {
        _context.DataQuality.RecordRowValidated();
        var failure = await EvaluateRowAsync(input, projected, rowOrdinal, cancellationToken);

        // Dry run: the failure has already been counted into the report, which is the whole point —
        // the steward gets the impact numbers. Enforcement is skipped, so no row leaves the output,
        // no capture table is written, and the load behaves exactly as it would without the rule.
        if (_context.DataQualityDryRun)
        {
            if (failure is not null) _context.DataQuality.RecordRowDryRunAffected();
            return true;
        }

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
        if (RequiresUniquePrePass)
            throw new InvalidOperationException("ValidateAsync does not support UNIQUE rules; use the select pipeline pre-pass.");

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
    private async Task<RowFailure?> EvaluateRowAsync(Row input, Row projected, long? rowOrdinal, CancellationToken cancellationToken)
    {
        RowFailure? decided = null;

        foreach (var ruleSet in _ruleSets)
        {
            var value = projected[ruleSet.OutputIndex];
            foreach (var binding in ruleSet.Bindings)
            {
                foreach (var rule in binding.Rules)
                {
                    // UNIQUE is decided by the pre-pass over the whole spilled stream; every other
                    // rule is a pure per-row predicate.
                    bool passed = rule is UniqueRule unique
                        ? !ViolatesUnique(unique, ruleSet, projected, rowOrdinal)
                        : await RulePassesAsync(rule, value, projected, cancellationToken);
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
            await provider.SaveQuarantineReplayManifestAsync(manifest, cancellationToken);
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
