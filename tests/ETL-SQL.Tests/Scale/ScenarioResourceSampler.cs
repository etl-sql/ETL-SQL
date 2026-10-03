using System.Diagnostics;

namespace ETL_SQL.Tests.Scale;

internal sealed record ScenarioResourceMetrics(
    long StartWorkingSetBytes,
    long PeakWorkingSetBytes,
    long PeakPrivateBytes,
    long PeakManagedHeapBytes,
    long AllocatedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    TimeSpan GcPauseTime,
    TimeSpan CpuTime,
    double CpuUtilizationPercent,
    TimeSpan ObservedDuration)
{
    public ScenarioResourceMetrics AddOperation(ScenarioResourceMetrics other)
    {
        var duration = ObservedDuration + other.ObservedDuration;
        var cpu = CpuTime + other.CpuTime;
        return this with
        {
            PeakWorkingSetBytes = Math.Max(PeakWorkingSetBytes, other.PeakWorkingSetBytes),
            PeakPrivateBytes = Math.Max(PeakPrivateBytes, other.PeakPrivateBytes),
            PeakManagedHeapBytes = Math.Max(PeakManagedHeapBytes, other.PeakManagedHeapBytes),
            AllocatedBytes = AllocatedBytes + other.AllocatedBytes,
            Gen0Collections = Gen0Collections + other.Gen0Collections,
            Gen1Collections = Gen1Collections + other.Gen1Collections,
            Gen2Collections = Gen2Collections + other.Gen2Collections,
            GcPauseTime = GcPauseTime + other.GcPauseTime,
            CpuTime = cpu,
            CpuUtilizationPercent = Utilization(cpu, duration),
            ObservedDuration = duration
        };
    }

    internal static double Utilization(TimeSpan cpu, TimeSpan duration)
        => Math.Round(Math.Max(0, cpu.TotalSeconds / Math.Max(0.001, duration.TotalSeconds)
            / Environment.ProcessorCount * 100), 1);
}

/// <summary>Continuously samples process and GC resources, then atomically snapshots and resets.</summary>
internal sealed class ScenarioResourceSampler : IDisposable
{
    private readonly object _gate = new();
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _samplingTask;
    private long _peakWorkingSet;
    private long _peakPrivateBytes;
    private long _peakManagedHeap;
    private Baseline _baseline;
    private Baseline? _operationBaseline;

    public ScenarioResourceSampler()
    {
        Sample();
        _baseline = CaptureBaseline();
        _samplingTask = SampleContinuouslyAsync(_stop.Token);
    }

    public ScenarioResourceMetrics SnapshotAndReset()
    {
        Sample();
        lock (_gate)
        {
            if (_operationBaseline != null)
                throw new InvalidOperationException("Finish the timed operation before resetting lifecycle resources.");
            var now = CaptureBaseline();
            var result = Measure(_baseline, now);

            _baseline = now;
            _peakWorkingSet = 0;
            _peakPrivateBytes = 0;
            _peakManagedHeap = 0;
            return result;
        }
    }

    // Operation deltas exclude fixture setup and verification. Lifecycle peaks continue sampling
    // through both, so the memory containment gate still covers the complete scenario.
    public void StartOperation()
    {
        lock (_gate)
        {
            if (_operationBaseline != null)
                throw new InvalidOperationException("A timed operation is already running.");
            _operationBaseline = CaptureBaseline();
        }
    }

    public ScenarioResourceMetrics FinishOperation()
    {
        Sample();
        lock (_gate)
        {
            if (_operationBaseline == null)
                throw new InvalidOperationException("No timed operation is running.");
            var result = Measure(_operationBaseline, CaptureBaseline());
            _operationBaseline = null;
            return result;
        }
    }

    private ScenarioResourceMetrics Measure(Baseline start, Baseline end)
    {
        var duration = Stopwatch.GetElapsedTime(start.Timestamp, end.Timestamp);
        var cpu = end.CpuTime - start.CpuTime;
        return new ScenarioResourceMetrics(
            start.WorkingSetBytes, _peakWorkingSet, _peakPrivateBytes, _peakManagedHeap,
            Math.Max(0, end.AllocatedBytes - start.AllocatedBytes),
            Math.Max(0, end.Gen0Collections - start.Gen0Collections),
            Math.Max(0, end.Gen1Collections - start.Gen1Collections),
            Math.Max(0, end.Gen2Collections - start.Gen2Collections),
            end.GcPauseTime - start.GcPauseTime, cpu,
            ScenarioResourceMetrics.Utilization(cpu, duration), duration);
    }

    private async Task SampleContinuouslyAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) Sample();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void Sample()
    {
        lock (_gate)
        {
            _process.Refresh();
            var managedHeap = GC.GetGCMemoryInfo().HeapSizeBytes;
            _peakWorkingSet = Math.Max(_peakWorkingSet, _process.WorkingSet64);
            _peakPrivateBytes = Math.Max(_peakPrivateBytes, _process.PrivateMemorySize64);
            _peakManagedHeap = Math.Max(_peakManagedHeap, managedHeap);
        }
    }

    private Baseline CaptureBaseline()
    {
        _process.Refresh();
        return new Baseline(
            Stopwatch.GetTimestamp(),
            _process.TotalProcessorTime,
            GC.GetTotalAllocatedBytes(precise: false),
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            GC.GetTotalPauseDuration(),
            _process.WorkingSet64);
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _samplingTask.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        _stop.Dispose();
        _process.Dispose();
    }

    private sealed record Baseline(
        long Timestamp,
        TimeSpan CpuTime,
        long AllocatedBytes,
        int Gen0Collections,
        int Gen1Collections,
        int Gen2Collections,
        TimeSpan GcPauseTime,
        long WorkingSetBytes);
}
