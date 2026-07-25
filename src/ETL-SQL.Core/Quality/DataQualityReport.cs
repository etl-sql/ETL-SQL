using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace ETL_SQL.Core.Quality;

/// <summary>
/// Per-run accumulator for data-quality outcomes. WARN is aggregated, never per-row: per
/// (rule, column) the report keeps a failure count plus the first N sample values, and one
/// <c>Diagnostic(Warning)</c> is emitted per pair at end of stream — per-row diagnostics on a
/// 10M-row load with a high failure rate is a diagnostics DoS. Values from <c>@pii</c>-tagged
/// columns are masked here, at capture time, so they cannot leak into diagnostics, logs, or
/// alert payloads. Also carries the run tallies (<see cref="RowsQuarantined"/>,
/// <see cref="RowsWarned"/>) that persist to job history.
/// </summary>
public sealed class DataQualityReport
{
    /// <summary>Mask substituted for sample values captured from a <c>@pii</c>-tagged column.</summary>
    public const string PiiMask = "***";

    private readonly ConcurrentDictionary<(string Column, string Rule), RuleFailureAccumulator> _failures = new();
    private long _rowsQuarantined;
    private long _rowsWarned;
    private long _rowsValidated;

    // Per-column run metrics, collected ONLY for columns an ASSERT JOB predicate names. A script
    // with no NULL_PERCENT/FRESHNESS predicate registers nothing, so the per-cell check never runs.
    private readonly ConcurrentDictionary<(string Target, string Column), ColumnMetricAccumulator> _columnMetrics =
        new(ColumnMetricKeyComparer.Instance);
    private readonly ConcurrentDictionary<(string? Target, string Column), ColumnMetricRegistration> _columnRegistrations =
        new(ColumnMetricRegistrationComparer.Instance);
    private volatile bool _tracksColumnMetrics;

    /// <summary>Maximum sample values retained per (rule, column) pair.</summary>
    public int MaxSamplesPerRule { get; init; } = 10;

    public long RowsQuarantined => Interlocked.Read(ref _rowsQuarantined);
    public long RowsWarned => Interlocked.Read(ref _rowsWarned);
    public long RowsValidated => Interlocked.Read(ref _rowsValidated);

    /// <summary>True when no rule has failed and no row was validated — lets callers skip all reporting work.</summary>
    public bool IsEmpty => _failures.IsEmpty && RowsValidated == 0 && _columnMetrics.IsEmpty;

    public void RecordRowValidated() => Interlocked.Increment(ref _rowsValidated);
    public void RecordRowQuarantined() => Interlocked.Increment(ref _rowsQuarantined);
    public void RecordRowWarned() => Interlocked.Increment(ref _rowsWarned);

    /// <summary>
    /// True when at least one column is registered for null tracking — the guard callers check
    /// before doing any per-cell work.
    /// </summary>
    public bool TracksNullCounts => _tracksColumnMetrics && _columnRegistrations.Values.Any(r => r.TrackNullPercent);

    public bool TracksColumnMetrics => _tracksColumnMetrics;

    /// <summary>
    /// Registers a column whose null fraction an <c>ASSERT JOB NULL_PERCENT(col)</c> predicate
    /// needs. Called by the pre-execution walk over the script; unregistered columns are never
    /// inspected, so a script without such a predicate pays nothing per cell.
    /// </summary>
    public void RegisterNullTrackedColumn(string columnName)
        => RegisterColumnMetric(null, columnName, trackNullPercent: true, trackFreshness: false);

    public void RegisterColumnMetric(
        string? targetTable,
        string columnName,
        bool trackNullPercent,
        bool trackFreshness)
    {
        var key = (NormalizeTarget(targetTable), columnName);
        _columnRegistrations.AddOrUpdate(
            key,
            _ => new ColumnMetricRegistration(key.Item1, columnName, trackNullPercent, trackFreshness),
            (_, existing) => existing with
            {
                TrackNullPercent = existing.TrackNullPercent || trackNullPercent,
                TrackFreshness = existing.TrackFreshness || trackFreshness
            });
        _tracksColumnMetrics = true;
    }

    /// <summary>
    /// Records one observed value for a tracked column. Ignores columns that were not registered.
    /// </summary>
    public void RecordColumnValue(string columnName, bool isNull)
        => RecordColumnValue(null, columnName, isNull, null);

    public void RecordColumnValue(string? targetTable, string columnName, bool isNull, object? value)
    {
        if (!_tracksColumnMetrics) return;
        var normalizedTarget = NormalizeTarget(targetTable);
        var registrations = MatchingRegistrations(normalizedTarget, columnName).ToList();
        if (registrations.Count == 0) return;

        var trackNullPercent = registrations.Any(r => r.TrackNullPercent);
        var trackFreshness = registrations.Any(r => r.TrackFreshness);
        var accumulator = _columnMetrics.GetOrAdd(
            (normalizedTarget ?? "", columnName),
            _ => new ColumnMetricAccumulator(normalizedTarget, columnName));
        accumulator.Add(
            trackNullPercent ? isNull : null,
            trackFreshness ? TryCoerceDateTimeOffset(value) : null);
    }

    /// <summary>
    /// The observed null fraction (0..1) for a tracked column, or null when the column was never
    /// registered or no rows were observed for it.
    /// </summary>
    public decimal? GetNullPercent(string columnName)
        => GetNullPercent(null, columnName);

    public decimal? GetNullPercent(string? targetTable, string columnName)
    {
        var accumulator = ResolveColumnMetricAccumulator(targetTable, columnName);
        if (accumulator == null) return null;
        var (total, nulls) = accumulator.Snapshot();
        return total == 0 ? null : (decimal)nulls / total;
    }

    public DateTimeOffset? GetMaxTimestamp(string? targetTable, string columnName) =>
        ResolveColumnMetricAccumulator(targetTable, columnName)?.MaxTimestamp;

    /// <summary>Column names registered for null tracking, whether or not any row was seen.</summary>
    public IReadOnlyCollection<string> NullTrackedColumns =>
        _columnRegistrations.Values
            .Where(r => r.TrackNullPercent)
            .Select(r => r.ColumnName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public IReadOnlyCollection<ColumnMetricRegistration> ColumnMetricRegistrations =>
        _columnRegistrations.Values
            .OrderBy(r => r.TargetTable ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.ColumnName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public bool ShouldTrackColumnMetric(string? targetTable, string columnName) =>
        MatchingRegistrations(NormalizeTarget(targetTable), columnName).Any();

    public IReadOnlyList<DataQualityColumnMetric> ColumnMetrics =>
        _columnMetrics.Values
            .Select(a =>
            {
                var (total, nulls) = a.Snapshot();
                return new DataQualityColumnMetric(a.TargetTable, a.ColumnName, total, nulls, a.MaxTimestamp);
            })
            .Where(m => m.TotalRows > 0 || m.MaxTimestampUtc != null)
            .OrderBy(m => m.TargetTable ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.ColumnName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// True when an unqualified column name was written by more than one sink statement in this
    /// run. Qualified <c>NULL_PERCENT(target.col)</c> resolves directly to the target-specific
    /// accumulator.
    /// </summary>
    public bool IsNullTrackedColumnAmbiguous(string columnName) =>
        IsNullTrackedColumnAmbiguous(null, columnName);

    public bool IsNullTrackedColumnAmbiguous(string? targetTable, string columnName) =>
        targetTable == null
            && _columnMetrics.Values.Count(a =>
                a.TrackSinkSeen && a.ColumnName.Equals(columnName, StringComparison.OrdinalIgnoreCase)) > 1;

    /// <summary>Records that a distinct sink statement contributed values for a tracked column.</summary>
    public void RecordNullTrackedSink(string columnName)
        => RecordNullTrackedSink(null, columnName);

    public void RecordNullTrackedSink(string? targetTable, string columnName)
    {
        if (!_tracksColumnMetrics) return;
        var normalizedTarget = NormalizeTarget(targetTable);
        if (!MatchingRegistrations(normalizedTarget, columnName).Any(r => r.TrackNullPercent || r.TrackFreshness)) return;
        var accumulator = _columnMetrics.GetOrAdd(
            (normalizedTarget ?? "", columnName),
            _ => new ColumnMetricAccumulator(normalizedTarget, columnName));
        accumulator.MarkSinkSeen();
    }

    /// <summary>
    /// Records one rule failure. <paramref name="sample"/> is the projected value that failed;
    /// pass <paramref name="isPii"/> so the value is masked before it is ever retained.
    /// </summary>
    public void RecordFailure(string column, string rule, FailAction action, object? sample, bool isPii)
    {
        var accumulator = _failures.GetOrAdd((column, rule), _ => new RuleFailureAccumulator(MaxSamplesPerRule));
        accumulator.Add(action, isPii ? PiiMask : Format(sample));
    }

    /// <summary>Per-(column, rule) failure counts and capped samples, ordered for stable output.</summary>
    public IReadOnlyList<RuleFailureSummary> Failures =>
        _failures
            .Select(kv => new RuleFailureSummary(
                kv.Key.Column, kv.Key.Rule, kv.Value.Action, kv.Value.Count, kv.Value.Samples))
            .OrderBy(f => f.Column, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Rule, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Total failures across every rule (a row failing two rules counts twice).</summary>
    public long TotalFailures => _failures.Values.Sum(v => v.Count);

    /// <summary>
    /// Compact per-rule failure-count payload for the job-history record:
    /// <c>column:rule=count</c> entries joined by <c>;</c>. Sample values are never included —
    /// history rows must not carry data values.
    /// </summary>
    public string ToHistoryPayload() =>
        string.Join(";", Failures.Select(f => $"{f.Column}:{f.Rule}={f.Count}"));

    public void Clear()
    {
        _failures.Clear();
        _columnMetrics.Clear();
        _columnRegistrations.Clear();
        _tracksColumnMetrics = false;
        Interlocked.Exchange(ref _rowsQuarantined, 0);
        Interlocked.Exchange(ref _rowsWarned, 0);
        Interlocked.Exchange(ref _rowsValidated, 0);
    }

    private static string Format(object? value) => value switch
    {
        null => "NULL",
        string s => s,
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "NULL"
    };

    private IEnumerable<ColumnMetricRegistration> MatchingRegistrations(string? targetTable, string columnName) =>
        _columnRegistrations.Values.Where(r =>
            r.ColumnName.Equals(columnName, StringComparison.OrdinalIgnoreCase)
            && (r.TargetTable == null || TargetMatches(r.TargetTable, targetTable)));

    private ColumnMetricAccumulator? ResolveColumnMetricAccumulator(string? targetTable, string columnName)
    {
        var normalizedTarget = NormalizeTarget(targetTable);
        if (normalizedTarget != null)
        {
            return _columnMetrics.TryGetValue((normalizedTarget, columnName), out var exact) ? exact : null;
        }

        var matches = _columnMetrics.Values
            .Where(a => a.ColumnName.Equals(columnName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static string? NormalizeTarget(string? targetTable)
    {
        if (string.IsNullOrWhiteSpace(targetTable)) return null;
        return targetTable.Trim().TrimStart('#');
    }

    private static bool TargetMatches(string registeredTarget, string? observedTarget)
    {
        var observed = NormalizeTarget(observedTarget);
        return observed != null && registeredTarget.Equals(observed, StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset? TryCoerceDateTimeOffset(object? value)
    {
        if (value is null or DBNull) return null;
        if (value is DateTimeOffset dto) return dto.ToUniversalTime();
        if (value is DateTime dt)
        {
            if (dt.Kind == DateTimeKind.Unspecified) dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            return new DateTimeOffset(dt.ToUniversalTime());
        }
        if (DateTimeOffset.TryParse(value.ToString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            return parsed.ToUniversalTime();
        return null;
    }

    private sealed class ColumnMetricAccumulator(string? targetTable, string columnName)
    {
        private long _total;
        private long _nulls;
        private int _sinkSeen;
        private DateTimeOffset? _maxTimestamp;
        private readonly object _timestampGate = new();

        public string? TargetTable { get; } = targetTable;
        public string ColumnName { get; } = columnName;
        public bool TrackSinkSeen => Volatile.Read(ref _sinkSeen) == 1;
        public DateTimeOffset? MaxTimestamp
        {
            get
            {
                lock (_timestampGate) return _maxTimestamp;
            }
        }

        public void Add(bool? isNull, DateTimeOffset? timestamp)
        {
            if (isNull.HasValue)
            {
                Interlocked.Increment(ref _total);
                if (isNull.Value) Interlocked.Increment(ref _nulls);
            }

            if (timestamp.HasValue)
            {
                lock (_timestampGate)
                {
                    if (_maxTimestamp == null || timestamp.Value > _maxTimestamp.Value)
                        _maxTimestamp = timestamp.Value;
                }
            }
        }

        public void MarkSinkSeen() => Volatile.Write(ref _sinkSeen, 1);

        public (long Total, long Nulls) Snapshot() => (Interlocked.Read(ref _total), Interlocked.Read(ref _nulls));
    }

    private sealed class RuleFailureAccumulator(int maxSamples)
    {
        private readonly List<string> _samples = [];
        private readonly object _gate = new();
        private long _count;

        public long Count => Interlocked.Read(ref _count);
        public FailAction Action { get; private set; }

        public IReadOnlyList<string> Samples
        {
            get { lock (_gate) return _samples.ToArray(); }
        }

        public void Add(FailAction action, string sample)
        {
            Interlocked.Increment(ref _count);
            lock (_gate)
            {
                Action = action;
                if (_samples.Count < maxSamples) _samples.Add(sample);
            }
        }
    }

    private sealed class ColumnMetricKeyComparer : IEqualityComparer<(string Target, string Column)>
    {
        public static ColumnMetricKeyComparer Instance { get; } = new();
        public bool Equals((string Target, string Column) x, (string Target, string Column) y) =>
            string.Equals(x.Target, y.Target, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Column, y.Column, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string Target, string Column) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Target),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Column));
    }

    private sealed class ColumnMetricRegistrationComparer : IEqualityComparer<(string? Target, string Column)>
    {
        public static ColumnMetricRegistrationComparer Instance { get; } = new();
        public bool Equals((string? Target, string Column) x, (string? Target, string Column) y) =>
            string.Equals(x.Target, y.Target, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Column, y.Column, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string? Target, string Column) obj) =>
            HashCode.Combine(
                obj.Target == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Target),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Column));
    }
}

public sealed record ColumnMetricRegistration(
    string? TargetTable,
    string ColumnName,
    bool TrackNullPercent,
    bool TrackFreshness);

public sealed record DataQualityColumnMetric(
    string? TargetTable,
    string ColumnName,
    long TotalRows,
    long NullRows,
    DateTimeOffset? MaxTimestampUtc);

/// <summary>Aggregated outcome for one (column, rule) pair.</summary>
public sealed record RuleFailureSummary(
    string Column,
    string Rule,
    FailAction Action,
    long Count,
    IReadOnlyList<string> Samples)
{
    /// <summary>The end-of-stream diagnostic text: count plus capped samples.</summary>
    public string ToMessage() =>
        $"Data quality: {Count:N0} row(s) failed rule \"{Rule}\" on column '{Column}' " +
        $"[{Action.ToString().ToUpperInvariant()}]" +
        (Samples.Count > 0 ? $". Sample values: {string.Join(", ", Samples)}" : ".");
}
