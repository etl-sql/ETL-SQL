using System.Diagnostics;

namespace ETL_SQL.Core.Adaptive;

/// <summary>Low-allocation process signal sampler for adaptive observe mode.</summary>
public sealed class ResourceSignalSampler
{
    private readonly IMemoryGrantArbiter _memoryGrantArbiter;
    private readonly AdaptiveRuntimeMetrics _runtimeMetrics;
    private readonly Process _process;
    private TimeSpan _lastCpu;
    private long _lastTimestamp;

    public ResourceSignalSampler(
        IMemoryGrantArbiter? memoryGrantArbiter = null,
        Process? process = null,
        AdaptiveRuntimeMetrics? runtimeMetrics = null)
    {
        _memoryGrantArbiter = memoryGrantArbiter ?? UnlimitedMemoryGrantArbiter.Instance;
        _runtimeMetrics = runtimeMetrics ?? AdaptiveRuntimeMetricsShared.Empty;
        _process = process ?? Process.GetCurrentProcess();
        _lastCpu = _process.TotalProcessorTime;
        _lastTimestamp = Stopwatch.GetTimestamp();
    }

    public ResourceSignals Sample()
    {
        var now = Stopwatch.GetTimestamp();
        var cpu = _process.TotalProcessorTime;
        var elapsedSeconds = (now - _lastTimestamp) / (double)Stopwatch.Frequency;
        var cpuSeconds = (cpu - _lastCpu).TotalSeconds;
        _lastTimestamp = now;
        _lastCpu = cpu;

        var cpuUtilization = elapsedSeconds > 0
            ? cpuSeconds / elapsedSeconds / Math.Max(1, Environment.ProcessorCount)
            : 0;

        var memoryInfo = GC.GetGCMemoryInfo();
        var memoryLoad = memoryInfo.HighMemoryLoadThresholdBytes > 0
            ? memoryInfo.MemoryLoadBytes / (double)memoryInfo.HighMemoryLoadThresholdBytes
            : 0;

        var grantPressure = _memoryGrantArbiter.TotalBudgetBytes > 0
            ? _memoryGrantArbiter.ReservedBytes / (double)_memoryGrantArbiter.TotalBudgetBytes
            : 0;

        return new ResourceSignals(
            cpuUtilization,
            memoryLoad,
            grantPressure,
            SpillWriteLatencyMsPerMB: _runtimeMetrics.SpillWriteLatencyMsPerMB,
            QueueDepth: _runtimeMetrics.QueueDepth).Clamp();
    }
}
