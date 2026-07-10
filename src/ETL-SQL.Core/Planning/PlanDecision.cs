using System.Collections.Generic;

namespace ETL_SQL.Core.Planning;

public enum PlanDecisionOutcome
{
    Accepted,
    Rejected,
    Fallback,
    Degraded
}

public static class PlanDecisionReasonCodes
{
    public const string UnsupportedExpression = nameof(UnsupportedExpression);
    public const string UnsupportedType = nameof(UnsupportedType);
    public const string UnsupportedCollation = nameof(UnsupportedCollation);
    public const string SemanticGuard = nameof(SemanticGuard);
    public const string MemoryAdmissionRejected = nameof(MemoryAdmissionRejected);
    public const string MissingStatistics = nameof(MissingStatistics);
    public const string NonReplayableSource = nameof(NonReplayableSource);
    public const string ConnectorCapabilityMissing = nameof(ConnectorCapabilityMissing);
    public const string GovernanceCeiling = nameof(GovernanceCeiling);
    public const string PlannerException = nameof(PlannerException);
}

public sealed record PlanDecision(
    string QueryId,
    string OperatorId,
    string CandidatePath,
    PlanDecisionOutcome Outcome,
    string ReasonCode,
    string Message,
    IReadOnlyDictionary<string, string> Attributes);
