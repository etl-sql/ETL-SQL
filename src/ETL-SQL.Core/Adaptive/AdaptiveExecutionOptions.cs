namespace ETL_SQL.Core.Adaptive;

/// <summary>Configuration for the adaptive execution controller.</summary>
public sealed record AdaptiveExecutionOptions
{
    public bool Enabled { get; init; }
    public int SampleMs { get; init; } = 250;
    public double CpuHigh { get; init; } = 0.90;
    public double CpuLow { get; init; } = 0.55;
    public double MemoryHigh { get; init; } = 0.80;
    public double MemoryLow { get; init; } = 0.55;
    public double GrantHigh { get; init; } = 0.85;
    public double GrantLow { get; init; } = 0.50;
    public double SpillWriteLatencyHighMsPerMB { get; init; } = 250;
    public double SpillWriteLatencyLowMsPerMB { get; init; } = 75;
    public int MinBatchRows { get; init; } = 1000;
    public int MaxPipelineDepth { get; init; } = 2;
    public int MinOperatorGrantRequestMB { get; init; } = 64;
    public int ConsecutiveHighSamples { get; init; } = 2;
    public int ConsecutiveLowSamples { get; init; } = 8;
    public int CooldownSamples { get; init; } = 4;
    public int LegacyBatchRows { get; init; } = 10000;
    public int LegacyWorkerDegree { get; init; } = 1;
    public int LegacyPipelineDepth { get; init; } = 1;
    public int LegacySpillWriteConcurrency { get; init; } = 1;
    public int LegacyOperatorGrantRequestMB { get; init; } = 256;
}
