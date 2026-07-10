using System;
using System.Collections.Generic;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Planning;
using ETL_SQL.Engine.Services;
using Xunit;

namespace ETL_SQL.Tests.Engine;

public class PlanDecisionTelemetryTests
{
    [Fact]
    public void RecordPlanDecision_StoresSanitizedDecision()
    {
        var telemetry = new ExecutionTelemetryManager();

        telemetry.RecordPlanDecision(new PlanDecision(
            QueryId: " q1 ",
            OperatorId: " op1\r\nextra ",
            CandidatePath: "ColumnarAggregate",
            Outcome: PlanDecisionOutcome.Rejected,
            ReasonCode: PlanDecisionReasonCodes.UnsupportedExpression,
            Message: "Unsupported function with PASSWORD='open-sesame' and SECRET:db_password",
            Attributes: new Dictionary<string, string>
            {
                ["function"] = "REGEX_MATCH",
                ["PASSWORD"] = "open-sesame",
                ["connectionString"] = "postgres://user:secret@server/db"
            }));

        var decision = Assert.Single(telemetry.PlanDecisions);
        Assert.Equal("q1", decision.QueryId);
        Assert.Equal("op1  extra", decision.OperatorId);
        Assert.Equal(PlanDecisionOutcome.Rejected, decision.Outcome);
        Assert.Equal(PlanDecisionReasonCodes.UnsupportedExpression, decision.ReasonCode);
        Assert.DoesNotContain("open-sesame", decision.Message);
        Assert.DoesNotContain("SECRET:db_password", decision.Message);
        Assert.Equal(SecretRedactor.Mask, decision.Attributes["PASSWORD"]);
        Assert.DoesNotContain("secret", decision.Attributes["connectionString"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecordPlanDecision_UsesBoundedOldestDropBuffer()
    {
        var telemetry = new ExecutionTelemetryManager { MaxPlanDecisions = 2 };

        telemetry.RecordPlanDecision(NewDecision("q1"));
        telemetry.RecordPlanDecision(NewDecision("q2"));
        telemetry.RecordPlanDecision(NewDecision("q3"));

        Assert.Collection(
            telemetry.PlanDecisions,
            d => Assert.Equal("q2", d.QueryId),
            d => Assert.Equal("q3", d.QueryId));
    }

    [Fact]
    public void Clear_RemovesPlanDecisions()
    {
        var telemetry = new ExecutionTelemetryManager();
        telemetry.RecordPlanDecision(NewDecision("q1"));

        telemetry.Clear();

        Assert.Empty(telemetry.PlanDecisions);
    }

    [Fact]
    public void ReasonCodes_MatchDocumentedTaxonomy()
    {
        var codes = new[]
        {
            PlanDecisionReasonCodes.UnsupportedExpression,
            PlanDecisionReasonCodes.UnsupportedType,
            PlanDecisionReasonCodes.UnsupportedCollation,
            PlanDecisionReasonCodes.SemanticGuard,
            PlanDecisionReasonCodes.MemoryAdmissionRejected,
            PlanDecisionReasonCodes.MissingStatistics,
            PlanDecisionReasonCodes.NonReplayableSource,
            PlanDecisionReasonCodes.ConnectorCapabilityMissing,
            PlanDecisionReasonCodes.GovernanceCeiling,
            PlanDecisionReasonCodes.PlannerException
        };

        Assert.Equal(10, new HashSet<string>(codes, StringComparer.Ordinal).Count);
        Assert.All(codes, code => Assert.Matches("^[A-Z][A-Za-z]+$", code));
    }

    [Fact]
    public void RecordPlanDecision_RespectsTelemetryDisabledAndZeroCap()
    {
        var disabled = new ExecutionTelemetryManager { TelemetryEnabled = false };
        disabled.RecordPlanDecision(NewDecision("disabled"));
        Assert.Empty(disabled.PlanDecisions);

        var capped = new ExecutionTelemetryManager { MaxPlanDecisions = 0 };
        capped.RecordPlanDecision(NewDecision("capped"));
        Assert.Empty(capped.PlanDecisions);
    }

    private static PlanDecision NewDecision(string queryId) =>
        new(
            QueryId: queryId,
            OperatorId: "op",
            CandidatePath: "ColumnarAggregate",
            Outcome: PlanDecisionOutcome.Accepted,
            ReasonCode: PlanDecisionReasonCodes.SemanticGuard,
            Message: "ok",
            Attributes: new Dictionary<string, string>());
}
