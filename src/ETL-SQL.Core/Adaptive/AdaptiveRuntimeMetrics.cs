namespace ETL_SQL.Core.Adaptive;

/// <summary>Low-cost live execution signals that are not available from process counters.</summary>
public sealed class AdaptiveRuntimeMetrics
{
    private readonly object _gate = new();
    private double _spillWriteLatencyMsPerMB;
    private int _queueDepth;
    private long _spillWriteTicks;
    private long _spillWriteBytes;
    private int _storagePressureEvents;

    /// <summary>Wall time spent in spill writes since the session started.</summary>
    public TimeSpan TotalSpillWriteTime
    {
        get
        {
            lock (_gate) return TimeSpan.FromTicks(_spillWriteTicks);
        }
    }

    /// <summary>Bytes written by the timed spill writes in <see cref="TotalSpillWriteTime"/>.</summary>
    public long TotalSpillWriteBytes
    {
        get
        {
            lock (_gate) return _spillWriteBytes;
        }
    }

    /// <summary>Times the adaptive controller scaled down because spill writes were slow.</summary>
    public int StoragePressureEvents
    {
        get
        {
            lock (_gate) return _storagePressureEvents;
        }
    }

    public void ReportStoragePressure()
    {
        lock (_gate) _storagePressureEvents++;
    }

    public int QueueDepth
    {
        get
        {
            lock (_gate) return _queueDepth;
        }
    }

    public double SpillWriteLatencyMsPerMB
    {
        get
        {
            lock (_gate) return _spillWriteLatencyMsPerMB;
        }
    }

    public void ReportQueueDepth(int queueDepth)
    {
        lock (_gate)
            _queueDepth = Math.Max(0, queueDepth);
    }

    public void ReportSpillWrite(long bytesWritten, TimeSpan elapsed)
    {
        if (bytesWritten <= 0 || elapsed <= TimeSpan.Zero)
            return;

        lock (_gate)
        {
            _spillWriteTicks += elapsed.Ticks;
            _spillWriteBytes += bytesWritten;
        }

        var mb = bytesWritten / 1024d / 1024d;
        if (mb <= 0)
            return;

        var sample = elapsed.TotalMilliseconds / mb;
        lock (_gate)
        {
            _spillWriteLatencyMsPerMB = _spillWriteLatencyMsPerMB <= 0
                ? sample
                : (_spillWriteLatencyMsPerMB * 0.8) + (sample * 0.2);
        }
    }
}

internal static class AdaptiveRuntimeMetricsShared
{
    public static AdaptiveRuntimeMetrics Empty { get; } = new();
}
