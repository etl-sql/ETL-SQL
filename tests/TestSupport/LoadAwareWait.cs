#nullable enable

using System.Diagnostics;
using System.Text.Json;

namespace ETL_SQL.TestSupport;

/// <summary>
/// Observable-condition wait for integration tests. The baseline budget is scaled once per test
/// process from measured ThreadPool dispatch latency, so saturated CI and an idle workstation do
/// not pretend to have the same scheduler capacity.
/// </summary>
public static class LoadAwareWait
{
    public const string EvidencePathEnvironmentVariable = "ETLSQL_WAIT_TIMING_EVIDENCE";

    private static readonly object EvidenceLock = new();
    private static readonly Lazy<Calibration> ProcessCalibration = new(MeasureLoad);

    public static double BudgetScale => ProcessCalibration.Value.Scale;

    public static Task<T> UntilAsync<T>(
        string description,
        Func<CancellationToken, Task<T>> observe,
        Func<T, bool> satisfied,
        TimeSpan baselineBudget,
        TimeSpan? pollInterval = null,
        Func<T, string>? describe = null,
        CancellationToken cancellationToken = default) =>
        UntilCoreAsync(
            description, observe, satisfied, baselineBudget,
            pollInterval ?? TimeSpan.FromMilliseconds(100), describe, cancellationToken);

    private static async Task<T> UntilCoreAsync<T>(
        string description,
        Func<CancellationToken, Task<T>> observe,
        Func<T, bool> satisfied,
        TimeSpan baselineBudget,
        TimeSpan pollInterval,
        Func<T, string>? describe,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(observe);
        ArgumentNullException.ThrowIfNull(satisfied);
        if (baselineBudget <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(baselineBudget));
        if (pollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollInterval));

        var calibration = ProcessCalibration.Value;
        var scaledBudget = TimeSpan.FromMilliseconds(baselineBudget.TotalMilliseconds * calibration.Scale);
        var stopwatch = Stopwatch.StartNew();
        var attempts = 0;
        T? last = default;
        string lastState = "<not observed>";

        while (stopwatch.Elapsed < scaledBudget)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempts++;
            var observationBudget = scaledBudget - stopwatch.Elapsed;
            if (observationBudget <= TimeSpan.Zero) break;
            using var observationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            observationCts.CancelAfter(observationBudget);
            try
            {
                last = await observe(observationCts.Token);
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested && observationCts.IsCancellationRequested)
            {
                lastState = "observation did not return before the scaled budget expired";
                break;
            }
            lastState = Describe(last, describe);
            if (satisfied(last))
            {
                Record(description, "Satisfied", stopwatch.Elapsed, baselineBudget, scaledBudget,
                    calibration, attempts, lastState);
                return last;
            }

            var remaining = scaledBudget - stopwatch.Elapsed;
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining < pollInterval ? remaining : pollInterval, cancellationToken);
        }

        Record(description, "TimedOut", stopwatch.Elapsed, baselineBudget, scaledBudget,
            calibration, attempts, lastState);
        throw new TimeoutException(
            $"Timed out after {stopwatch.Elapsed.TotalSeconds:F2}s waiting for {description}. " +
            $"Baseline budget={baselineBudget.TotalSeconds:F2}s; scaled budget={scaledBudget.TotalSeconds:F2}s; " +
            $"load scale={calibration.Scale:F2} (ThreadPool probe={calibration.ProbeMilliseconds:F1}ms); " +
            $"attempts={attempts}; last observed state: {lastState}.");
    }

    private static string Describe<T>(T? value, Func<T, string>? describe)
    {
        if (value is null) return "<null>";
        try { return describe is null ? value.ToString() ?? "<null>" : describe(value); }
        catch (Exception ex) { return $"<state formatter failed: {ex.GetType().Name}>"; }
    }

    private static Calibration MeasureLoad()
    {
        const int probes = 8;
        using var completed = new CountdownEvent(probes);
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < probes; i++)
            ThreadPool.QueueUserWorkItem(_ => completed.Signal());
        var allDispatched = completed.Wait(TimeSpan.FromSeconds(5));
        stopwatch.Stop();

        var probeMs = allDispatched ? stopwatch.Elapsed.TotalMilliseconds : 5000;
        var scale = Math.Clamp(probeMs / 125d, 1d, 4d);
        return new Calibration(scale, probeMs);
    }

    private static void Record(
        string description,
        string outcome,
        TimeSpan elapsed,
        TimeSpan baseline,
        TimeSpan scaled,
        Calibration calibration,
        int attempts,
        string lastState)
    {
        var path = Environment.GetEnvironmentVariable(EvidencePathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(path)) return;

        var record = JsonSerializer.Serialize(new
        {
            schemaVersion = "etl-sql.test-wait-timing/v1",
            timestampUtc = DateTimeOffset.UtcNow,
            processId = Environment.ProcessId,
            description,
            outcome,
            elapsedMilliseconds = Math.Round(elapsed.TotalMilliseconds, 3),
            baselineBudgetMilliseconds = baseline.TotalMilliseconds,
            scaledBudgetMilliseconds = Math.Round(scaled.TotalMilliseconds, 3),
            loadScale = Math.Round(calibration.Scale, 3),
            threadPoolProbeMilliseconds = Math.Round(calibration.ProbeMilliseconds, 3),
            attempts,
            lastState
        });

        lock (EvidenceLock)
        {
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.AppendAllText(fullPath, record + Environment.NewLine);
        }
    }

    private sealed record Calibration(double Scale, double ProbeMilliseconds);
}
