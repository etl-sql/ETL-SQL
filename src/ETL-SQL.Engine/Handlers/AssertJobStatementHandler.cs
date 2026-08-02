using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Quality;
using ETL_SQL.Data;
using ETL_SQL.Engine.Services;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Engine.Handlers;

/// <summary>
/// Handles <c>ASSERT JOB</c>: evaluates run-level metric predicates against the values the
/// in-stream collector gathered during this run (never a post-run re-scan), routes failures through
/// an optional catalog notification, and throws when <c>ON CRITICAL_FAILURE THROW</c> is declared.
/// </summary>
public class AssertJobStatementHandler(
    ILogger logger,
    IConfiguration? config = null,
    IJobCatalogStore? catalog = null) : IStatementHandler
{
    private const int DefaultHistoryRuns = 5;
    private const int DefaultMinHistoryRuns = 3;
    private const int DefaultMinSigmaHistoryRuns = 10;

    /// <summary>
    /// Per-sigma relative tolerance used when the historical series has zero standard deviation.
    /// 1% per sigma, so <c>WITHIN 3 SIGMA</c> tolerates a 3% move off a perfectly flat baseline.
    /// </summary>
    private const decimal ZeroSigmaFallbackTolerance = 0.01m;
    private const int DefaultAlertRealertHours = 24;

    public Type SupportedStatementType => typeof(AssertJobStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (AssertJobStatement)statement;
        var report = context.DataQuality;

        int historyRuns = ReadOption("Engine:DataQuality:HistoryRuns", DefaultHistoryRuns);
        int minHistoryRuns = ReadOption("Engine:DataQuality:MinHistoryRuns", DefaultMinHistoryRuns);
        int minSigmaHistoryRuns = ReadOption("Engine:DataQuality:MinSigmaHistoryRuns", DefaultMinSigmaHistoryRuns);
        int alertRealertHours = ReadOption("Engine:DataQuality:AlertRealertHours", DefaultAlertRealertHours);

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

        var failForWarnRows = stmt.FailOnWarn && report.RowsWarned > 0;
        if (failForWarnRows)
            failures.Add($"FAIL_ON_WARN = TRUE ({report.RowsWarned:N0} warned row(s))");

        if (failures.Count == 0)
        {
            if (stmt.FailureNotification != null)
                await HandleAlertTransitionAsync(context, stmt, failed: false, failures, alertRealertHours);

            logger.Debug("ASSERT JOB {JobName}: all {Count} predicate(s) passed.", stmt.JobName, stmt.Predicates.Count);
            return;
        }

        var summary = $"ASSERT JOB {stmt.JobName} failed {failures.Count} of {stmt.Predicates.Count} predicate(s): "
            + string.Join("; ", failures);
        logger.Warning("{AssertJobFailure}", summary);

        if (stmt.FailureNotification != null)
            await HandleAlertTransitionAsync(context, stmt, failed: true, failures, alertRealertHours, summary);

        if (stmt.ThrowOnCritical || failForWarnRows)
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
                // A perfectly stable history collapses the band to equality, which would fail the
                // run on a single extra row — the alert-storm behavior sigma exists to avoid. Fall
                // back to a relative tolerance around the mean instead, scaled by the requested
                // sigma count so a wider band stays wider.
                if (baseline == 0m)
                {
                    Warn(context, $"ASSERT JOB {stmt.JobName}: skipping {predicate.Describe()} — " +
                        "the historical baseline and sigma are both zero, so no band is defined.");
                    return;
                }

                var fallbackTolerance = predicate.Tolerance!.Value * ZeroSigmaFallbackTolerance;
                var fallbackDrift = Math.Abs(current - baseline) / Math.Abs(baseline);
                Warn(context, $"ASSERT JOB {stmt.JobName}: {predicate.Describe()} has zero historical sigma; " +
                    $"falling back to a relative tolerance of {Format(fallbackTolerance)} around the mean.");
                if (fallbackDrift > fallbackTolerance)
                {
                    failures.Add($"{predicate.Describe()} (actual {Format(current)}, baseline {Format(baseline)}, " +
                        $"sigma 0, drift {Format(fallbackDrift)} > {Format(fallbackTolerance)})");
                }
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
    /// Posts a failure summary through the named catalog notification. Metric values and counts
    /// only — never sample data, so a PII column's values cannot reach an alerting channel.
    /// Delivery failure has its own policy (log and continue), independent of ON CRITICAL_FAILURE:
    /// a broken alerting channel must not decide whether the run fails.
    /// </summary>
    private async Task HandleAlertTransitionAsync(
        IExecutionContext context,
        AssertJobStatement stmt,
        bool failed,
        List<string> failures,
        int alertRealertHours,
        string? failureSummary = null)
    {
        var provider = context.JobMetrics;
        if (provider is null)
        {
            if (failed)
                await SendAlertAsync(context, stmt, "FAILURE", failures, failureSummary!);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var key = BuildAssertionKey(stmt);
        var prior = await provider.GetAssertJobAlertStateAsync(stmt.JobName, key, context.CancellationToken);

        bool deliveredFailure = false;
        if (failed)
        {
            var shouldAlert = prior is null
                || !prior.LastFailed
                || prior.LastFailureAlertedAtUtc is null
                || now - prior.LastFailureAlertedAtUtc.Value >= TimeSpan.FromHours(alertRealertHours);

            if (shouldAlert)
            {
                deliveredFailure = await SendAlertAsync(context, stmt, "FAILURE", failures, failureSummary!);
            }
            else if (prior?.LastFailureAlertedAtUtc is DateTimeOffset lastAlerted)
            {
                var message = $"ASSERT JOB {stmt.JobName}: suppressing repeated alert for {stmt.Predicates.Count} predicate(s); " +
                    $"last failure alert was {lastAlerted:O}.";
                logger.Info("{AssertJobAlertSuppressed}", message);
                context.Log(message, ConsoleColor.Yellow);
            }

            await provider.SaveAssertJobAlertStateAsync(
                stmt.JobName,
                key,
                new AssertJobAlertState(
                    LastFailed: true,
                    LastFailureAlertedAtUtc: deliveredFailure ? now : prior?.LastFailureAlertedAtUtc,
                    UpdatedAtUtc: now),
                context.CancellationToken);
            return;
        }

        if (prior?.LastFailed == true)
        {
            var summary = $"ASSERT JOB {stmt.JobName} recovered: all {stmt.Predicates.Count} predicate(s) passed.";
            await SendAlertAsync(context, stmt, "RECOVERY", [], summary);
        }

        await provider.SaveAssertJobAlertStateAsync(
            stmt.JobName,
            key,
            new AssertJobAlertState(
                LastFailed: false,
                LastFailureAlertedAtUtc: prior?.LastFailureAlertedAtUtc,
                UpdatedAtUtc: now),
            context.CancellationToken);
    }

    private async Task<bool> SendAlertAsync(
        IExecutionContext context,
        AssertJobStatement stmt,
        string alertKind,
        List<string> failures,
        string summary)
    {
        var notification = await ResolveFailureNotificationAsync(stmt);
        if (notification is null) return false;

        try
        {
            if (!context.Connections.TryGetValue(notification.ConnectionName, out var sink))
            {
                logger.Warning(
                    "ASSERT JOB {JobName}: notification '{Notification}' references connection '{Connection}', which is not defined; skipping the notification.",
                    stmt.JobName, stmt.FailureNotification, notification.ConnectionName);
                return false;
            }

            var report = context.DataQuality;
            var table = new DataTable();
            table.SetColumns(["Title", "Text", "JobName", "AlertKind", "FailedPredicates",
                "RowsValidated", "RowsQuarantined", "RowsWarned", "Owners", "FailingColumns", "Recipient"]);
            var row = table.NewRow();
            row["Title"] = alertKind.Equals("RECOVERY", StringComparison.OrdinalIgnoreCase)
                ? $"Data quality recovery: {stmt.JobName}"
                : $"Data quality alert: {stmt.JobName}";
            row["Text"] = summary;
            row["JobName"] = stmt.JobName;
            row["AlertKind"] = alertKind;
            row["FailedPredicates"] = string.Join("; ", failures);
            row["RowsValidated"] = (decimal)report.RowsValidated;
            row["RowsQuarantined"] = (decimal)report.RowsQuarantined;
            row["RowsWarned"] = (decimal)report.RowsWarned;

            // Name the columns that actually failed and who owns them, so the alert reaches
            // someone who can act rather than only stating that a threshold moved. Column names
            // and stewards only — never sample values, which may be PII.
            var failingColumns = report.Failures
                .Select(f => f.Column)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var owners = report.Failures
                .Where(f => !string.IsNullOrWhiteSpace(f.Owner))
                .Select(f => f.Owner!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(o => o, StringComparer.OrdinalIgnoreCase)
                .ToList();

            row["FailingColumns"] = string.Join(", ", failingColumns);
            row["Owners"] = string.Join(", ", owners);
            row["Recipient"] = notification.Recipient ?? "";
            if (owners.Count > 0)
                row["Text"] = $"{summary} Owner(s): {string.Join(", ", owners)}.";

            await table.AddRowAsync(row);

            await sink.WriteBatches(SingleBatch(table), append: true, context.CancellationToken);
            logger.Info(
                "ASSERT JOB {JobName}: notification '{Notification}' delivered through connection '{Connection}'.",
                stmt.JobName, stmt.FailureNotification, notification.ConnectionName);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Warning("ASSERT JOB {JobName}: notification delivery through '{Notification}' failed: {Message}",
                stmt.JobName, stmt.FailureNotification, ETL_SQL.Core.Common.SecretRedactor.Redact(ex.Message));
            return false;
        }
    }

    private async Task<NotificationDefinition?> ResolveFailureNotificationAsync(AssertJobStatement stmt)
    {
        if (catalog is null)
            throw new ExecutionException(
                $"ASSERT JOB {stmt.JobName}: ON FAILURE NOTIFY requires an Orchestrator notification catalog. " +
                "Run the script in an orchestrator context or remove the NOTIFY clause.",
                null, stmt.Line, stmt.Column);

        var notification = await catalog.GetNotificationAsync(stmt.FailureNotification!);
        if (notification is null)
        {
            logger.Warning(
                "ASSERT JOB {JobName}: notification '{Notification}' does not exist; skipping the notification.",
                stmt.JobName, stmt.FailureNotification);
            return null;
        }

        if (!notification.IsEnabled)
        {
            logger.Info(
                "ASSERT JOB {JobName}: notification '{Notification}' is disabled; skipping the notification.",
                stmt.JobName, stmt.FailureNotification);
            return null;
        }

        return notification;
    }

    private static string BuildAssertionKey(AssertJobStatement stmt)
    {
        var payload = string.Join("|", stmt.Predicates.Select(p => p.Describe()));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
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
