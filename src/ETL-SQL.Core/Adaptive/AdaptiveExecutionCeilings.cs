namespace ETL_SQL.Core.Adaptive;

/// <summary>Configured and governed maximum setpoints for one execution advisor.</summary>
public sealed record AdaptiveExecutionCeilings(
    int BatchRows,
    int WorkerDegree,
    int PipelineDepth,
    int SpillWriteConcurrency,
    int OperatorGrantRequestMB)
{
    public AdaptiveExecutionCeilings Clamp(AdaptiveExecutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new AdaptiveExecutionCeilings(
            Math.Max(options.MinBatchRows, BatchRows),
            Math.Max(1, WorkerDegree),
            Math.Clamp(PipelineDepth, 0, options.MaxPipelineDepth),
            Math.Max(1, SpillWriteConcurrency),
            Math.Max(options.MinOperatorGrantRequestMB, OperatorGrantRequestMB));
    }
}
