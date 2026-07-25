using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Quality;
using ETL_SQL.Data;
using ETL_SQL.Engine.Services;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Engine.Handlers;

/// <summary>
/// Handles <c>ASSERT JOB</c>: evaluates run-level metric predicates against the values the
/// in-stream collector gathered during this run (never a post-run re-scan), routes failures to an
/// optional webhook alert, and throws when <c>ON CRITICAL_FAILURE THROW</c> is declared.
/// </summary>
public class AssertJobStatementHandler(ILogger logger, IConfiguration? config = null) : IStatementHandler
{
    private const int DefaultHistoryRuns = 5;
    private const int DefaultMinHistoryRuns = 3;

    public Type SupportedStatementType => typeof(AssertJobStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (AssertJobStatement)statement;
        var report = context.DataQuality;

        int historyRuns = ReadOption("Engine:DataQuality:HistoryRuns", DefaultHistoryRuns);
        int minHistoryRuns = ReadOption("Engine:DataQuality:MinHistoryRuns", DefaultMinHistoryRuns);

        IReadOnlyList<Core.Data.JobRunMetrics>? history = null;
        bool historyLoaded = false;

        var failures = new List<string>();
        foreach (var predicate in stmt.Predicates)
        {
            var current = ResolveCurrentValue(predicate, report, stmt.JobName);
            if (current is null)
            {
                // The metric could not be observed this run (e.g. no rows carried the column).
                // Skip rather than assert on a value we do not have.
                Warn(context, $"ASSERT JOB {stmt.JobName}: skipping {predicate.Describe()} — " +
                    "the metric was not observed during this run.");
                continue;
            }

            if (!predicate.IsHistorical)
            {
                if (!Compare(current.Value, predicate.Op!.Value, predicate.Bound!.Value))
                    failures.Add($"{predicate.Describe()} (actual {Format(current.Value)})");
                continue;
            }

            if (!historyLoaded)
            {
                history = await LoadHistoryAsync(context, stmt.JobName, historyRuns);
                historyLoaded = true;
            }

            if (history is null)
            {
                throw new ExecutionException(
                    $"ASSERT JOB {stmt.JobName}: '{predicate.Describe()}' requires orchestrator run history, " +
                    "which is not available in this execution context. Run the script as a scheduled job, " +
                    "or use a direct comparison instead of OF HISTORICAL.",
                    null, stmt.Line, stmt.Column);
            }

            // Cold start is defined, not accidental: a job's first deployments must not alert-storm.
            if (history.Count < minHistoryRuns)
            {
                Warn(context, $"ASSERT JOB {stmt.JobName}: skipping {predicate.Describe()} — " +
                    $"insufficient history: {history.Count} of {minHistoryRuns} runs.");
                continue;
            }

            var baseline = Baseline(predicate, history);
            if (baseline is null || baseline.Value == 0m)
            {
                Warn(context, $"ASSERT JOB {stmt.JobName}: skipping {predicate.Describe()} — " +
                    "the historical baseline is zero or unavailable, so a relative tolerance is undefined.");
                continue;
            }

            var drift = Math.Abs(current.Value - baseline.Value) / Math.Abs(baseline.Value);
            if (drift > predicate.Tolerance!.Value)
            {
                failures.Add($"{predicate.Describe()} (actual {Format(current.Value)}, " +
                    $"baseline {Format(baseline.Value)}, drift {Format(drift)})");
            }
        }

        if (failures.Count == 0)
        {
            logger.Debug("ASSERT JOB {JobName}: all {Count} predicate(s) passed.", stmt.JobName, stmt.Predicates.Count);
            return;
        }

        var summary = $"ASSERT JOB {stmt.JobName} failed {failures.Count} of {stmt.Predicates.Count} predicate(s): "
            + string.Join("; ", failures);
        logger.Warning("{AssertJobFailure}", summary);

        if (stmt.AlertConnection != null)
            await SendAlertAsync(context, stmt, failures, summary);

        if (stmt.ThrowOnCritical)
            throw new ExecutionException(summary, null, stmt.Line, stmt.Column);
    }

    /// <summary>
    /// Resolves a predicate's current-run value from the in-stream collector. Returns null when the
    /// metric was never observed. Percentages are fractions of validated rows.
    /// </summary>
    private static decimal? ResolveCurrentValue(JobMetricPredicate predicate, DataQualityReport report, string jobName)
    {
        switch (predicate.Metric)
        {
            case JobMetricKind.RowCount:
                return report.RowsValidated;

            case JobMetricKind.QuarantinePercent:
                return report.RowsValidated == 0 ? null : (decimal)report.RowsQuarantined / report.RowsValidated;

            case JobMetricKind.WarnPercent:
                return report.RowsValidated == 0 ? null : (decimal)report.RowsWarned / report.RowsValidated;

            case JobMetricKind.NullPercent:
                // v1 resolves the column across the run's sink writes; an ambiguous name is a clean
                // error rather than a silently-wrong metric.
                if (report.IsNullTrackedColumnAmbiguous(predicate.ColumnName!))
                {
                    throw new ExecutionException(
                        $"ASSERT JOB {jobName}: NULL_PERCENT({predicate.ColumnName}) is ambiguous — more than one " +
                        "sink statement in this run writes a column with that name. Qualified NULL_PERCENT is not " +
                        "supported yet; rename one of the columns or split the assertion into separate jobs.");
                }
                return report.GetNullPercent(predicate.ColumnName!);

            default:
                return null;
        }
    }

    /// <summary>
    /// The historical baseline: the mean of the metric across the last N completed runs.
    /// </summary>
    private static decimal? Baseline(JobMetricPredicate predicate, IReadOnlyList<Core.Data.JobRunMetrics> history)
    {
        if (history.Count == 0) return null;
        switch (predicate.Metric)
        {
            case JobMetricKind.RowCount:
                return history.Average(h => (decimal)h.RowsProcessed);

            case JobMetricKind.QuarantinePercent:
                return AverageRatio(history, h => h.RowsQuarantined);

            case JobMetricKind.WarnPercent:
                return AverageRatio(history, h => h.RowsWarned);

            default:
                // Per-column null fractions are not persisted per run, so NULL_PERCENT has no
                // historical baseline in v1 — the caller reports this as a skip.
                return null;
        }
    }

    private static decimal? AverageRatio(
        IReadOnlyList<Core.Data.JobRunMetrics> history, Func<Core.Data.JobRunMetrics, long> numerator)
    {
        var usable = history.Where(h => h.RowsProcessed > 0).ToList();
        if (usable.Count == 0) return null;
        return usable.Average(h => (decimal)numerator(h) / h.RowsProcessed);
    }

    private static async Task<IReadOnlyList<Core.Data.JobRunMetrics>?> LoadHistoryAsync(
        IExecutionContext context, string jobName, int limit)
    {
        var provider = context.JobMetrics;
        if (provider is null) return null;
        return await provider.GetRecentRunMetricsAsync(jobName, limit, context.CancellationToken);
    }

    /// <summary>
    /// Posts a failure summary through the named webhook connection. Metric values and counts only
    /// — never sample data, so a PII column's values cannot reach an alerting channel. Delivery
    /// failure has its own policy (log and continue), independent of ON CRITICAL_FAILURE: a broken
    /// alerting channel must not decide whether the run fails.
    /// </summary>
    private async Task SendAlertAsync(
        IExecutionContext context, AssertJobStatement stmt, List<string> failures, string summary)
    {
        try
        {
            if (!context.Connections.TryGetValue(stmt.AlertConnection!, out var sink))
            {
                logger.Warning("ASSERT JOB {JobName}: alert connection '{Connection}' is not defined; skipping the alert.",
                    stmt.JobName, stmt.AlertConnection);
                return;
            }

            var report = context.DataQuality;
            var table = new DataTable();
            table.SetColumns(["Title", "Text", "JobName", "FailedPredicates", "RowsValidated", "RowsQuarantined", "RowsWarned"]);
            var row = table.NewRow();
            row["Title"] = $"Data quality alert: {stmt.JobName}";
            row["Text"] = summary;
            row["JobName"] = stmt.JobName;
            row["FailedPredicates"] = string.Join("; ", failures);
            row["RowsValidated"] = (decimal)report.RowsValidated;
            row["RowsQuarantined"] = (decimal)report.RowsQuarantined;
            row["RowsWarned"] = (decimal)report.RowsWarned;
            await table.AddRowAsync(row);

            await sink.WriteBatches(SingleBatch(table), append: true, context.CancellationToken);
            logger.Info("ASSERT JOB {JobName}: alert delivered through '{Connection}'.", stmt.JobName, stmt.AlertConnection);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Warning("ASSERT JOB {JobName}: alert delivery through '{Connection}' failed: {Message}",
                stmt.JobName, stmt.AlertConnection, ETL_SQL.Core.Common.SecretRedactor.Redact(ex.Message));
        }
    }

    private static async IAsyncEnumerable<DataTable> SingleBatch(DataTable table)
    {
        yield return table;
        await Task.CompletedTask;
    }

    private static bool Compare(decimal actual, CompareOp op, decimal bound) => op switch
    {
        CompareOp.GreaterOrEqual => actual >= bound,
        CompareOp.LessOrEqual => actual <= bound,
        CompareOp.Greater => actual > bound,
        CompareOp.Less => actual < bound,
        _ => actual == bound
    };

    private void Warn(IExecutionContext context, string message)
    {
        logger.Warning("{AssertJobSkip}", message);
        context.Log(message, ConsoleColor.Yellow);
    }

    private int ReadOption(string key, int fallback)
    {
        var configured = config?.GetValue<int?>(key);
        return configured is > 0 ? configured.Value : fallback;
    }

    private static string Format(decimal value) =>
        value == decimal.Truncate(value)
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.####", CultureInfo.InvariantCulture);
}
