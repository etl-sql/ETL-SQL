using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.Core.Planning;

public static class PlanDecisionSummary
{
    public static string FormatFallbackSummary(IReadOnlyList<PlanDecision> decisions)
    {
        var summary = decisions
            .Where(d => d.Outcome is PlanDecisionOutcome.Fallback or PlanDecisionOutcome.Rejected or PlanDecisionOutcome.Degraded)
            .GroupBy(d => $"{d.CandidatePath}:{d.ReasonCode}")
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key}={group.Count()}");
        var text = string.Join("; ", summary);
        return string.IsNullOrEmpty(text) ? "--" : text;
    }
}
