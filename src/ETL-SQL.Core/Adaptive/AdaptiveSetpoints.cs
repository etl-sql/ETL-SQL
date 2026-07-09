namespace ETL_SQL.Core.Adaptive;

/// <summary>Effective execution setpoints currently advised for a job.</summary>
public sealed record AdaptiveSetpoints(
    int BatchRows,
    int WorkerDegree,
    int PipelineDepth,
    int SpillWriteConcurrency,
    int OperatorGrantRequestMB);
