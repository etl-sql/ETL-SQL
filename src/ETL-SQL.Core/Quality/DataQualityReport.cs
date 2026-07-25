using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

    // Per-column null tallies, collected ONLY for columns an ASSERT JOB predicate names. A script
    // with no NULL_PERCENT predicate registers nothing, so the per-cell check never runs.
    private readonly ConcurrentDictionary<string, ColumnNullAccumulator> _nullCounts =
        new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _tracksNullCounts;

    /// <summary>Maximum sample values retained per (rule, column) pair.</summary>
    public int MaxSamplesPerRule { get; init; } = 10;

    public long RowsQuarantined => Interlocked.Read(ref _rowsQuarantined);
    public long RowsWarned => Interlocked.Read(ref _rowsWarned);
    public long RowsValidated => Interlocked.Read(ref _rowsValidated);

    /// <summary>True when no rule has failed and no row was validated — lets callers skip all reporting work.</summary>
    public bool IsEmpty => _failures.IsEmpty && RowsValidated == 0;

    public void RecordRowValidated() => Interlocked.Increment(ref _rowsValidated);
    public void RecordRowQuarantined() => Interlocked.Increment(ref _rowsQuarantined);
    public void RecordRowWarned() => Interlocked.Increment(ref _rowsWarned);

    /// <summary>
    /// True when at least one column is registered for null tracking — the guard callers check
    /// before doing any per-cell work.
    /// </summary>
    public bool TracksNullCounts => _tracksNullCounts;

    /// <summary>
    /// Registers a column whose null fraction an <c>ASSERT JOB NULL_PERCENT(col)</c> predicate
    /// needs. Called by the pre-execution walk over the script; unregistered columns are never
    /// inspected, so a script without such a predicate pays nothing per cell.
    /// </summary>
    public void RegisterNullTrackedColumn(string columnName)
    {
        _nullCounts.TryAdd(columnName, new ColumnNullAccumulator());
        _tracksNullCounts = true;
    }

    /// <summary>
    /// Records one observed value for a tracked column. Ignores columns that were not registered.
    /// </summary>
    public void RecordColumnValue(string columnName, bool isNull)
    {
        if (!_tracksNullCounts) return;
        if (!_nullCounts.TryGetValue(columnName, out var accumulator)) return;
        accumulator.Add(isNull);
    }

    /// <summary>
    /// The observed null fraction (0..1) for a tracked column, or null when the column was never
    /// registered or no rows were observed for it.
    /// </summary>
    public decimal? GetNullPercent(string columnName)
    {
        if (!_nullCounts.TryGetValue(columnName, out var accumulator)) return null;
        var (total, nulls) = accumulator.Snapshot();
        return total == 0 ? null : (decimal)nulls / total;
    }

    /// <summary>Column names registered for null tracking, whether or not any row was seen.</summary>
    public IReadOnlyCollection<string> NullTrackedColumns => _nullCounts.Keys.ToList();

    /// <summary>
    /// True when the named column was written by more than one sink statement in this run. v1
    /// resolves <c>NULL_PERCENT(col)</c> across the run's sink writes; an ambiguous name is a clean
    /// error rather than a silently-wrong metric (qualified <c>NULL_PERCENT(target.col)</c> is a
    /// noted future extension).
    /// </summary>
    public bool IsNullTrackedColumnAmbiguous(string columnName) =>
        _nullCounts.TryGetValue(columnName, out var accumulator) && accumulator.SinkCount > 1;

    /// <summary>Records that a distinct sink statement contributed values for a tracked column.</summary>
    public void RecordNullTrackedSink(string columnName)
    {
        if (_nullCounts.TryGetValue(columnName, out var accumulator)) accumulator.AddSink();
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
        _nullCounts.Clear();
        _tracksNullCounts = false;
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

    private sealed class ColumnNullAccumulator
    {
        private long _total;
        private long _nulls;
        private int _sinkCount;

        public int SinkCount => Volatile.Read(ref _sinkCount);

        public void Add(bool isNull)
        {
            Interlocked.Increment(ref _total);
            if (isNull) Interlocked.Increment(ref _nulls);
        }

        public void AddSink() => Interlocked.Increment(ref _sinkCount);

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
}

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
