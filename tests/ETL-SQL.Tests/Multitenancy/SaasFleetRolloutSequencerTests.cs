using ETL_SQL.App;
using ETL_SQL.Orchestrator.Storage;
using Xunit;

namespace ETL_SQL.Tests.Multitenancy;

/// <summary>
/// Walking a fleet rollout wave by wave. The sequencer holds no authority: it drives the caller's
/// executor, which owns every authorization decision.
/// </summary>
public sealed class SaasFleetRolloutSequencerTests
{
    [Fact]
    public async Task WavesRunInOrderAndOnlyAfterTheEarlierOneHasFinishedDraining()
    {
        var plan = Plan(waveSize: 1, "tenant-a", "tenant-b", "tenant-c");
        var attempted = new List<string>();

        // The first deployment fences and drains; its work has not finished.
        var run = await SaasFleetRolloutSequencer.RunAsync(
            plan,
            (tenant, _) =>
            {
                attempted.Add(tenant.TenantId);
                return Task.FromResult(new FleetCutoverResult(
                    tenant.TenantId,
                    tenant.TenantId == "tenant-a"
                        ? FleetRolloutOutcome.Draining
                        : FleetRolloutOutcome.Completed));
            },
            maxFailures: 0);

        // Overlapping a draining wave with the next one is how a rollout takes down more of the fleet
        // than it has repaired, so the later waves are not opened at all.
        Assert.Equal(["tenant-a"], attempted);
        Assert.Equal(1, run.WavesStarted);
        Assert.Equal(0, run.WavesCompleted);
        Assert.Equal(1, run.Status.Draining);
        Assert.Equal(2, run.Status.Pending);
        Assert.All(
            run.Results.Where(result => result.TenantId != "tenant-a"),
            result => Assert.Equal(FleetCutoverRefusal.EarlierWaveDraining, result.Refusal));
    }

    [Fact]
    public async Task RolloutStopsOpeningWavesOnceFailuresExceedTolerance()
    {
        var plan = Plan(waveSize: 2, "tenant-a", "tenant-b", "tenant-c", "tenant-d");
        var attempted = new List<string>();

        var run = await SaasFleetRolloutSequencer.RunAsync(
            plan,
            (tenant, _) =>
            {
                attempted.Add(tenant.TenantId);
                return Task.FromResult(new FleetCutoverResult(
                    tenant.TenantId, FleetRolloutOutcome.Failed, Detail: "cutover rejected"));
            },
            maxFailures: 1);

        // The first wave is attempted in full, then the halt stops the release going further.
        Assert.Equal(["tenant-a", "tenant-b"], attempted);
        Assert.True(run.Status.Halted);
        Assert.Equal(2, run.Status.Failed);
        Assert.All(
            run.Results.Where(result => result.TenantId is "tenant-c" or "tenant-d"),
            result => Assert.Equal(FleetCutoverRefusal.RolloutHalted, result.Refusal));
    }

    [Fact]
    public async Task UnauthorizedDeploymentsStayPendingAndNeverHaltTheRollout()
    {
        var plan = Plan(waveSize: 4, "tenant-a", "tenant-b", "tenant-c");

        var run = await SaasFleetRolloutSequencer.RunAsync(
            plan,
            (tenant, _) => Task.FromResult(tenant.TenantId == "tenant-b"
                ? new FleetCutoverResult(
                    tenant.TenantId, FleetRolloutOutcome.Pending, FleetCutoverRefusal.NotAuthorized,
                    "no signed authorization names this tenant")
                : new FleetCutoverResult(tenant.TenantId, FleetRolloutOutcome.Completed)),
            maxFailures: 0);

        // A rollout walks as far as its per-tenant authorizations reach. Work still owed is not work
        // that failed, and must not halt deployments that are authorized.
        Assert.False(run.Status.Halted);
        Assert.Equal(2, run.Status.Completed);
        Assert.Equal(1, run.Status.Pending);
        Assert.Equal(
            FleetCutoverRefusal.NotAuthorized,
            run.Results.Single(result => result.TenantId == "tenant-b").Refusal);
    }

    [Fact]
    public async Task ACleanRolloutCompletesEveryWave()
    {
        var plan = Plan(waveSize: 2, "tenant-a", "tenant-b", "tenant-c");

        var run = await SaasFleetRolloutSequencer.RunAsync(
            plan,
            (tenant, _) => Task.FromResult(
                new FleetCutoverResult(tenant.TenantId, FleetRolloutOutcome.Completed)),
            maxFailures: 0);

        Assert.Equal(2, run.WavesStarted);
        Assert.Equal(2, run.WavesCompleted);
        Assert.Equal(3, run.Status.Completed);
        Assert.False(run.Status.Halted);
    }

    [Fact]
    public async Task CancellationStopsBetweenDeploymentsRatherThanMidCutover()
    {
        var plan = Plan(waveSize: 5, "tenant-a", "tenant-b", "tenant-c");
        using var cancellation = new CancellationTokenSource();
        var attempted = new List<string>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SaasFleetRolloutSequencer.RunAsync(
                plan,
                (tenant, _) =>
                {
                    attempted.Add(tenant.TenantId);
                    cancellation.Cancel();
                    return Task.FromResult(
                        new FleetCutoverResult(tenant.TenantId, FleetRolloutOutcome.Completed));
                },
                maxFailures: 0,
                cancellation.Token));

        // The cutover that was already running is allowed to return; the next one never starts.
        Assert.Equal(["tenant-a"], attempted);
    }

    private static FleetRolloutPlan Plan(int waveSize, params string[] tenantIds) =>
        SaasFleetRolloutPlanner.Plan(
            tenantIds.Select(tenantId => new SharedTenantControlPlaneState(
                tenantId, "Active", "0.17.0", 4, 2048, 2, 1, DateTimeOffset.UtcNow, null, 1)).ToArray(),
            "0.18.0",
            waveSize);
}
