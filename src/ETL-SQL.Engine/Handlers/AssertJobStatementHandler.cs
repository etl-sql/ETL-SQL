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
    private const int DefaultMinSigmaHistoryRuns = 10;

    public Type SupportedStatementType => typeof(AssertJobStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (AssertJobStatement)statement;
        var report = context.DataQuality;

        int historyRuns = ReadOption("Engine:DataQuality:HistoryRuns", DefaultHistoryRuns);
        int minHistoryRuns = ReadOption("Engine:DataQuality:MinHistoryRuns", DefaultMinHistoryRuns);
        int minSigmaHistoryRuns = ReadOption("Engine:DataQuality:MinSigmaHistoryRuns", DefaultMinSigmaHistoryRuns);

        IReadOnlyList<Core.Data.JobRunMetrics>? history = null;
        bool historyLoaded = false;

        var failures = new List<string>();
        foreach (var predicate in stmt.Predicates)
        {
            var current = ResolveCurrentValue(predicate, context, report, stmt.JobName);
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
                var bound = predicate.IntervalBound != null
                    ? (decimal)predicate.IntervalBound.ToTimeSpan().TotalSeconds
                    : predicate.Bound!.Value;
                if (!Compare(current.Value, predicate.Op!.Value, bound))
                    failures.Add($"{predicate.Describe()} (actual {Format(current.Value)})");
                continue;
            }

            if (predicate.Metric == JobMetricKind.NullPercent)
            {
                var columnHistoryLimit = predicate.UsesSigma
                    ? Math.Max(historyRuns, minSigmaHistoryRuns)
                    : historyRuns;
                var columnHistory = await LoadColumnHistoryAsync(context, stmt.JobName, predicate, columnHistoryLimit);
                if (columnHistory is null)
                {
                    throw new ExecutionException(
                        $"ASSERT JOB {stmt.JobName}: '{predicate.Describe()}' requires orchestrator column run history, " +
                        "which is not available in this execution context.",
                        null, stmt.Line, stmt.Column);
                }

                var ratios = columnHistory
                    .Where(h => h.TotalRows > 0)
                    .Select(h => (decimal)h.NullRows / h.TotalRows)
                    .ToList();
                EvaluateHistoricalSeries(stmt, predicate, current.Value, ratios, minHistoryRuns, minSigmaHistoryRuns, failures, context);
                continue;
            }

            if (!historyLoaded)
            {
                var historyLimit = stmt.Predicates.Any(p => p.IsHistorical && p.UsesSigma)
                    ? Math.Max(historyRuns, minSigmaHistoryRuns)
                    : historyRuns;
                history = await LoadHistoryAsync(context, stmt.JobName, historyLimit);
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

            EvaluateHistoricalSeries(
                stmt,
                predicate,
                current.Value,
                HistoricalSeries(predicate, history),
                minHistoryRuns,
                minSigmaHistoryRuns,
                failures,
                context);
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
    /// Resolves a predicate's current-run value. ROW_COUNT matches the engine telemetry persisted
    /// to job history; percentages come from the in-stream data-quality collector.
    /// </summary>
    private static decimal? ResolveCurrentValue(
        JobMetricPredicate predicate, IExecutionContext context, DataQualityReport report, string jobName)
    {
        switch (predicate.Metric)
        {
            case JobMetricKind.RowCount:
                return context.Telemetry.RowsProcessed;

            case JobMetricKind.QuarantinePercent:
                return report.RowsValidated == 0 ? null : (decimal)report.RowsQuarantined / report.RowsValidated;

            case JobMetricKind.WarnPercent:
                return report.RowsValidated == 0 ? null : (decimal)report.RowsWarned / report.RowsValidated;

            case JobMetricKind.NullPercent:
                // Unqualified NULL_PERCENT resolves across this run's sink writes; an ambiguous
                // name is a clean error rather than a silently-wrong metric.
                if (report.IsNullTrackedColumnAmbiguous(predicate.TargetName, predicate.ColumnName!))
                {
                    throw new ExecutionException(
                        $"ASSERT JOB {jobName}: NULL_PERCENT({predicate.ColumnName}) is ambiguous — more than one " +
                        "sink statement in this run writes a column with that name. Use qualified " +
                        "NULL_PERCENT(target.column), rename one of the columns, or split the assertion into separate jobs.");
                }
                return report.GetNullPercent(predicate.TargetName, predicate.ColumnName!);

            case JobMetricKind.Freshness:
                var maxTimestamp = report.GetMaxTimestamp(predicate.TargetName, predicate.ColumnName!);
                return maxTimestamp == null
                    ? null
                    : (decimal)Math.Max(0, (DateTimeOffset.UtcNow - maxTimestamp.Value).TotalSeconds);

            default:
                return null;
        }
    }

    private static List<decimal> HistoricalSeries(JobMetricPredicate predicate, IReadOnlyList<Core.Data.JobRunMetrics> history) =>
        predicate.Metric switch
        {
            JobMetricKind.RowCount => history.Select(h => (decimal)h.RowsProcessed).ToList(),
            JobMetricKind.QuarantinePercent => history.Where(h => h.RowsProcessed > 0)
                .Select(h => (decimal)h.RowsQuarantined / h.RowsProcessed).ToList(),
            JobMetricKind.WarnPercent => history.Where(h => h.RowsProcessed > 0)
                .Select(h => (decimal)h.RowsWarned / h.RowsProcessed).ToList(),
            _ => []
        };

    private void EvaluateHistoricalSeries(
        AssertJobStatement stmt,
        JobMetricPredicate predicate,
        decimal current,
        IReadOnlyList<decimal> series,
        int minHistoryRuns,
        int minSigmaHistoryRuns,
        List<string> failures,
        IExecutionContext context)
    {
        var required = predicate.UsesSigma ? minSigmaHistoryRuns : minHistoryRuns;
        if (series.Count < required)
        {
            Warn(context, $"ASSERT JOB {stmt.JobName}: skipping {predicate.Describe()} — " +
                $"insufficient history: {series.Count} of {required} runs.");
            return;
        }

        var baseline = series.Average();
        if (predicate.UsesSigma)
        {
            var variance = series.Select(v => Math.Pow((double)(v - baseline), 2)).Average();
            var sigma = (decimal)Math.Sqrt(variance);
            if (sigma == 0m)
            {
                Warn(context, $"ASSERT JOB {stmt.JobName}: {predicate.Describe()} has zero historical sigma; " +
                    "using equality against the historical mean.");
                if (current != baseline)
                    failures.Add($"{predicate.Describe()} (actual {Format(current)}, baseline {Format(baseline)}, sigma 0)");
                return;
            }

            var distance = Math.Abs(current - baseline);
            var band = predicate.Tolerance!.Value * sigma;
            if (distance > band)
            {
                failures.Add($"{predicate.Describe()} (actual {Format(current)}, baseline {Format(baseline)}, " +
                    $"sigma {Format(sigma)}, distance {Format(distance)})");
            }
            return;
        }

        if (baseline == 0m)
        {
            Warn(context, $"ASSERT JOB {stmt.JobName}: skipping {predicate.Describe()} — " +
                "the historical baseline is zero or unavailable, so a relative tolerance is undefined.");
            return;
        }

        var drift = Math.Abs(current - baseline) / Math.Abs(baseline);
        if (drift > predicate.Tolerance!.Value)
        {
            failures.Add($"{predicate.Describe()} (actual {Format(current)}, " +
                $"baseline {Format(baseline)}, drift {Format(drift)})");
        }
    }

    private static async Task<IReadOnlyList<Core.Data.JobRunMetrics>?> LoadHistoryAsync(
        IExecutionContext context, string jobName, int limit)
    {
        var provider = context.JobMetrics;
        if (provider is null) return null;
        return await provider.GetRecentRunMetricsAsync(jobName, limit, context.CancellationToken);
    }

    private static async Task<IReadOnlyList<Core.Data.ColumnRunMetrics>?> LoadColumnHistoryAsync(
        IExecutionContext context, string jobName, JobMetricPredicate predicate, int limit)
    {
        var provider = context.JobMetrics;
        if (provider is null) return null;
        return await provider.GetRecentColumnMetricsAsync(
            jobName, predicate.TargetName, predicate.ColumnName!, limit, context.CancellationToken);
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
