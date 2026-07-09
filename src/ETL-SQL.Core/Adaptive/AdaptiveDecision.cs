namespace ETL_SQL.Core.Adaptive;

public enum AdaptiveDecisionKind
{
    None,
    ScaleDown,
    ScaleUp
}

/// <summary>A controller decision computed from one observed signal sample.</summary>
public sealed record AdaptiveDecision(
    AdaptiveDecisionKind Kind,
    string Reason,
    ResourceSignals Signals,
    DateTimeOffset Timestamp);
