namespace ETL_SQL.Core.Adaptive;

/// <summary>Process and pipeline pressure signals consumed by the adaptive controller.</summary>
public sealed record ResourceSignals(
    double CpuUtilization,
    double MemoryLoad,
    double GrantPressure,
    double Gen2CollectionsPerSecond = 0,
    double SpillWriteLatencyMsPerMB = 0,
    int QueueDepth = 0)
{
    public static readonly ResourceSignals Idle = new(0, 0, 0);

    public ResourceSignals Clamp() => this with
    {
        CpuUtilization = Math.Clamp(CpuUtilization, 0, 1),
        MemoryLoad = Math.Clamp(MemoryLoad, 0, 1),
        GrantPressure = Math.Clamp(GrantPressure, 0, 1),
        Gen2CollectionsPerSecond = Math.Max(0, Gen2CollectionsPerSecond),
        SpillWriteLatencyMsPerMB = Math.Max(0, SpillWriteLatencyMsPerMB),
        QueueDepth = Math.Max(0, QueueDepth)
    };
}
