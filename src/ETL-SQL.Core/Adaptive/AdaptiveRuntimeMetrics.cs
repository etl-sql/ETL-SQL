namespace ETL_SQL.Core.Adaptive;

/// <summary>Low-cost live execution signals that are not available from process counters.</summary>
public sealed class AdaptiveRuntimeMetrics
{
    private readonly object _gate = new();
    private double _spillWriteLatencyMsPerMB;
    private int _queueDepth;

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
