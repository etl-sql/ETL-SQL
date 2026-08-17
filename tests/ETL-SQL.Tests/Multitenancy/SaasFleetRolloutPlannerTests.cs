using ETL_SQL.App;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Storage;
using Xunit;

namespace ETL_SQL.Tests.Multitenancy;

/// <summary>
/// Fleet rollout across a population of per-tenant deployments. The planner decides what is eligible
/// and in what order; it never authorizes anything, because each cutover keeps its own tenant-scoped
/// signed grant.
/// </summary>
public sealed class SaasFleetRolloutPlannerTests
{
    [Fact]
    public void PlanRollsOnlyActiveDeploymentsThatAreBehindTheTarget()
    {
        var plan = SaasFleetRolloutPlanner.Plan(
        [
            State("tenant-behind", "0.17.0"),
            State("tenant-current", "0.18.0"),
            State("tenant-ahead", "0.19.0"),
            State("tenant-deleting", "0.17.0", state: "Deleting"),
            State("tenant-deleted", "0.17.0", state: "Deleted")
        ], "0.18.0", waveSize: 10);

        Assert.Equal(FleetRolloutDecision.Upgrade, Decision(plan, "tenant-behind"));
        Assert.Equal(FleetRolloutDecision.AlreadyCurrent, Decision(plan, "tenant-current"));
        // A rollout never moves a deployment backwards, and never races a lifecycle operation.
        Assert.Equal(FleetRolloutDecision.IncompatibleRelease, Decision(plan, "tenant-ahead"));
        Assert.Equal(FleetRolloutDecision.BlockedByState, Decision(plan, "tenant-deleting"));
        Assert.Equal(FleetRolloutDecision.BlockedByState, Decision(plan, "tenant-deleted"));
        Assert.Equal("tenant-behind", Assert.Single(plan.Upgradeable).TenantId);
    }

    [Fact]
    public void WavesAreDeterministicSoACanaryMeansTheSameDeploymentsEveryTime()
    {
        var population = new[]
        {
            State("tenant-e", "0.17.0"), State("tenant-a", "0.17.0"), State("tenant-d", "0.17.0"),
            State("tenant-b", "0.17.0"), State("tenant-c", "0.17.0")
        };

        var plan = SaasFleetRolloutPlanner.Plan(population, "0.18.0", waveSize: 2);
        var replanned = SaasFleetRolloutPlanner.Plan(population.Reverse().ToArray(), "0.18.0", waveSize: 2);

        Assert.Equal(3, plan.WaveCount);
        Assert.Equal(["tenant-a", "tenant-b"], Wave(plan, 0));
        Assert.Equal(["tenant-c", "tenant-d"], Wave(plan, 1));
        Assert.Equal(["tenant-e"], Wave(plan, 2));
        // Re-planning the same population, in any order, must produce the same waves.
        Assert.Equal(Wave(plan, 0), Wave(replanned, 0));
    }

    [Fact]
    public void SkippedDeploymentsDoNotConsumeAWaveSlot()
    {
        // A hundred-stack rollout is mostly already-current deployments after the first pass; they
        // must not push the deployments that still need work into later waves.
        var plan = SaasFleetRolloutPlanner.Plan(
        [
            State("tenant-a", "0.18.0"),
            State("tenant-b", "0.17.0"),
            State("tenant-c", "0.18.0"),
            State("tenant-d", "0.17.0")
        ], "0.18.0", waveSize: 2);

        Assert.Equal(1, plan.WaveCount);
        Assert.Equal(["tenant-b", "tenant-d"], Wave(plan, 0));
    }

    [Theory]
    [InlineData("0.9.0", "0.10.0", true)]     // numeric, not lexical: 10 follows 9
    [InlineData("0.18.0", "0.18.1", true)]
    [InlineData("0.18.0", "0.18.0", false)]
    [InlineData("0.18.1", "0.18.0", false)]
    [InlineData("0.18", "0.18.0", false)]     // equal once the missing component is zero
    [InlineData("0.18.0", "0.18", false)]
    public void ReleaseOrderingIsNumericPerComponent(string current, string target, bool isForward)
    {
        var plan = SaasFleetRolloutPlanner.Plan([State("tenant-a", current)], target, waveSize: 1);

        Assert.Equal(isForward, Decision(plan, "tenant-a") == FleetRolloutDecision.Upgrade);
    }

    [Fact]
    public void UnattemptedDeploymentsAreReportedPendingRatherThanAssumedDone()
    {
        var plan = SaasFleetRolloutPlanner.Plan(
            [State("tenant-a", "0.17.0"), State("tenant-b", "0.17.0")], "0.18.0", waveSize: 1);

        var status = SaasFleetRolloutPlanner.Track(
            plan,
            new Dictionary<string, FleetTenantProgress>
            {
                ["tenant-a"] = new("tenant-a", FleetRolloutOutcome.Completed)
            },
            maxFailures: 0);

        Assert.Equal(1, status.Completed);
        Assert.Equal(1, status.Pending);
        Assert.False(status.Halted);
    }

    [Fact]
    public void FailuresBeyondToleranceHaltTheRolloutButDrainingNeverDoes()
    {
        var plan = SaasFleetRolloutPlanner.Plan(
        [
            State("tenant-a", "0.17.0"), State("tenant-b", "0.17.0"), State("tenant-c", "0.17.0")
        ], "0.18.0", waveSize: 3);

        // A fenced tenant still finishing its work is progress, not damage.
        var draining = SaasFleetRolloutPlanner.Track(
            plan,
            Progress(("tenant-a", FleetRolloutOutcome.Draining), ("tenant-b", FleetRolloutOutcome.Draining)),
            maxFailures: 0);
        Assert.Equal(2, draining.Draining);
        Assert.False(draining.Halted);

        var tolerated = SaasFleetRolloutPlanner.Track(
            plan, Progress(("tenant-a", FleetRolloutOutcome.Failed)), maxFailures: 1);
        Assert.False(tolerated.Halted);

        var halted = SaasFleetRolloutPlanner.Track(
            plan,
            Progress(("tenant-a", FleetRolloutOutcome.Failed), ("tenant-b", FleetRolloutOutcome.Failed)),
            maxFailures: 1);
        Assert.True(halted.Halted);
        Assert.Contains("stopped before pushing the release further", halted.HaltReason);
    }

    [Fact]
    public void PlanRejectsANonPositiveWave()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SaasFleetRolloutPlanner.Plan([State("tenant-a", "0.17.0")], "0.18.0", waveSize: 0));
    }

    [Fact]
    public void FleetInventoryAuthorizationIsAttributedTimeLimitedAndTenantless()
    {
        var now = DateTimeOffset.UtcNow;
        var authorization = FleetInventoryAuthorization.Issue(
            "platform-operator", "CHG-1001", "v0.18.0 fleet rollout", now.AddMinutes(30), now);

        Assert.False(authorization.IsExpiredAt(now));
        Assert.True(authorization.IsExpiredAt(now.AddMinutes(31)));
        foreach (var (op, reference, reason) in new[]
                 {
                     ("", "CHG-1001", "why"), ("operator", "", "why"), ("operator", "CHG-1001", "")
                 })
        {
            Assert.Throws<ArgumentException>(() =>
                FleetInventoryAuthorization.Issue(op, reference, reason, now.AddMinutes(5), now));
        }

        // Standing visibility into the customer population is not an authorization.
        Assert.Throws<ArgumentException>(() => FleetInventoryAuthorization.Issue(
            "operator", "CHG-1001", "why", now, now));
    }

    private static FleetRolloutDecision Decision(FleetRolloutPlan plan, string tenantId) =>
        plan.Tenants.Single(tenant => tenant.TenantId == tenantId).Decision;

    private static string[] Wave(FleetRolloutPlan plan, int wave) =>
        plan.Upgradeable.Where(tenant => tenant.Wave == wave).Select(tenant => tenant.TenantId).ToArray();

    private static Dictionary<string, FleetTenantProgress> Progress(
        params (string TenantId, FleetRolloutOutcome Outcome)[] entries) =>
        entries.ToDictionary(
            entry => entry.TenantId,
            entry => new FleetTenantProgress(entry.TenantId, entry.Outcome),
            StringComparer.Ordinal);

    private static SharedTenantControlPlaneState State(
        string tenantId,
        string release,
        string state = "Active") =>
        new(tenantId, state, release, 4, 2048, 2, 1, DateTimeOffset.UtcNow, null, 1);
}
