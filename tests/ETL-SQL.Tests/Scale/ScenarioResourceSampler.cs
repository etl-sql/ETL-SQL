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
    double CpuUtilizationPercent);

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
            var now = CaptureBaseline();
            var elapsed = Math.Max(0.001, (now.Timestamp - _baseline.Timestamp).TotalSeconds);
            var cpu = now.CpuTime - _baseline.CpuTime;
            var result = new ScenarioResourceMetrics(
                _baseline.WorkingSetBytes,
                _peakWorkingSet,
                _peakPrivateBytes,
                _peakManagedHeap,
                Math.Max(0, now.AllocatedBytes - _baseline.AllocatedBytes),
                Math.Max(0, now.Gen0Collections - _baseline.Gen0Collections),
                Math.Max(0, now.Gen1Collections - _baseline.Gen1Collections),
                Math.Max(0, now.Gen2Collections - _baseline.Gen2Collections),
                now.GcPauseTime - _baseline.GcPauseTime,
                cpu,
                Math.Round(Math.Max(0, cpu.TotalSeconds / elapsed / Environment.ProcessorCount * 100), 1));

            _baseline = now;
            _peakWorkingSet = 0;
            _peakPrivateBytes = 0;
            _peakManagedHeap = 0;
            return result;
        }
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
        _process.Refresh();
        var managedHeap = GC.GetGCMemoryInfo().HeapSizeBytes;
        lock (_gate)
        {
            _peakWorkingSet = Math.Max(_peakWorkingSet, _process.WorkingSet64);
            _peakPrivateBytes = Math.Max(_peakPrivateBytes, _process.PrivateMemorySize64);
            _peakManagedHeap = Math.Max(_peakManagedHeap, managedHeap);
        }
    }

    private Baseline CaptureBaseline()
    {
        _process.Refresh();
        return new Baseline(
            DateTimeOffset.UtcNow,
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
        DateTimeOffset Timestamp,
        TimeSpan CpuTime,
        long AllocatedBytes,
        int Gen0Collections,
        int Gen1Collections,
        int Gen2Collections,
        TimeSpan GcPauseTime,
        long WorkingSetBytes);
}
