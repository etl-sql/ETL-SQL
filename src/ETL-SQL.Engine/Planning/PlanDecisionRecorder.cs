using System;
using System.Collections.Generic;
using ETL_SQL.Core;
using ETL_SQL.Core.Planning;

namespace ETL_SQL.Engine.Planning;

internal static class PlanDecisionRecorder
{
    public static void Record(
        IExecutionContext context,
        string operatorId,
        string candidatePath,
        PlanDecisionOutcome outcome,
        string reasonCode,
        string message,
        params (string Key, string? Value)[] attributes)
    {
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in attributes)
        {
            if (!string.IsNullOrWhiteSpace(value))
                facts[key] = value;
        }

        context.Telemetry.RecordPlanDecision(new PlanDecision(
            QueryId: $"execution:{context.SessionId}",
            OperatorId: operatorId,
            CandidatePath: candidatePath,
            Outcome: outcome,
            ReasonCode: reasonCode,
            Message: message,
            Attributes: facts));
    }
}
