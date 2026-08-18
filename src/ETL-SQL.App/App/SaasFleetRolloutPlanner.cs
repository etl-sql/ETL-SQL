using ETL_SQL.Orchestrator.Storage;

namespace ETL_SQL.App;

/// <summary>What the fleet intends to do with one tenant deployment in this rollout.</summary>
public enum FleetRolloutDecision
{
    /// <summary>Eligible: the tenant is active and its release is behind the target.</summary>
    Upgrade,

    /// <summary>Already on the target release. Not work, and not a failure.</summary>
    AlreadyCurrent,

    /// <summary>Deleted, or mid-lifecycle. Rolling it now would race the operation in flight.</summary>
    BlockedByState,

    /// <summary>The target is not a forward move from this tenant's release.</summary>
    IncompatibleRelease
}

/// <summary>How a tenant's cutover actually ended, once its per-tenant upgrade has been attempted.</summary>
public enum FleetRolloutOutcome
{
    /// <summary>Not attempted yet.</summary>
    Pending,

    /// <summary>Fenced, and still waiting on work to finish or be reconciled.</summary>
    Draining,

    Completed,
    Failed
}

public sealed record FleetTenantPlan(
    string TenantId,
    string CurrentRelease,
    FleetRolloutDecision Decision,
    string Reason,
    int Wave);

public sealed record FleetRolloutPlan(
    string TargetRelease,
    int WaveSize,
    IReadOnlyList<FleetTenantPlan> Tenants)
{
    public IEnumerable<FleetTenantPlan> Upgradeable =>
        Tenants.Where(tenant => tenant.Decision == FleetRolloutDecision.Upgrade);

    public int WaveCount => Upgradeable.Any() ? Upgradeable.Max(tenant => tenant.Wave) + 1 : 0;
}

public sealed record FleetTenantProgress(
    string TenantId,
    FleetRolloutOutcome Outcome,
    string? Detail = null);

public sealed record FleetRolloutStatus(
    string TargetRelease,
    int Completed,
    int Draining,
    int Failed,
    int Pending,
    bool Halted,
    string? HaltReason,
    IReadOnlyList<FleetTenantProgress> Tenants);

/// <summary>
/// Plans and tracks a release rollout across a population of per-tenant deployments — the operational
/// problem the Dedicated topology creates, where a release is not one upgrade but a hundred.
///
/// <para>It deliberately does not execute or authorize anything. Each tenant's cutover keeps needing
/// its own signed, tenant-scoped authorization; what was missing was the layer above that: which
/// deployments are eligible, in what order, and what the fleet's state is halfway through.</para>
/// </summary>
public static class SaasFleetRolloutPlanner
{
    /// <summary>Lifecycle states a tenant must be in before its deployment can be rolled.</summary>
    private const string RollableState = "Active";

    public static FleetRolloutPlan Plan(
        IReadOnlyList<SharedTenantControlPlaneState> population,
        string targetRelease,
        int waveSize)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRelease);
        if (waveSize <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(waveSize), "A rollout wave must contain at least one deployment.");

        var plans = new List<FleetTenantPlan>();
        var eligible = 0;
        // Deterministic order, so the same population plans into the same waves on every node and a
        // canary wave means the same tenants every time it is re-planned.
        foreach (var tenant in population.OrderBy(value => value.TenantId, StringComparer.Ordinal))
        {
            var (decision, reason) = Classify(tenant, targetRelease);
            var wave = decision == FleetRolloutDecision.Upgrade ? eligible++ / waveSize : -1;
            plans.Add(new FleetTenantPlan(
                tenant.TenantId, tenant.ActiveRelease, decision, reason, wave));
        }

        return new FleetRolloutPlan(targetRelease, waveSize, plans);
    }

    private static (FleetRolloutDecision Decision, string Reason) Classify(
        SharedTenantControlPlaneState tenant,
        string targetRelease)
    {
        if (!string.Equals(tenant.State, RollableState, StringComparison.Ordinal))
        {
            return (FleetRolloutDecision.BlockedByState,
                $"The deployment is '{tenant.State}', so a rollout would race the lifecycle " +
                "operation already in flight.");
        }

        if (string.Equals(tenant.ActiveRelease, targetRelease, StringComparison.Ordinal))
            return (FleetRolloutDecision.AlreadyCurrent, "Already on the target release.");

        return CompareReleases(tenant.ActiveRelease, targetRelease) switch
        {
            < 0 => (FleetRolloutDecision.Upgrade, "Eligible: active, and behind the target release."),
            _ => (FleetRolloutDecision.IncompatibleRelease,
                $"The target '{targetRelease}' is not a forward move from '{tenant.ActiveRelease}'; " +
                "a fleet rollout never moves a deployment backwards.")
        };
    }

    /// <summary>
    /// Orders two release identities. Dotted numeric components compare numerically so 0.10 follows
    /// 0.9; anything that is not a pure number compares ordinally, and an unparseable identity simply
    /// fails to be a forward move rather than being guessed at.
    /// </summary>
    internal static int CompareReleases(string current, string target)
    {
        var left = current.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var right = target.Split('.', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < Math.Max(left.Length, right.Length); index++)
        {
            var leftPart = index < left.Length ? left[index] : "0";
            var rightPart = index < right.Length ? right[index] : "0";
            if (leftPart == rightPart) continue;

            if (int.TryParse(leftPart, out var leftValue) && int.TryParse(rightPart, out var rightValue))
                return leftValue.CompareTo(rightValue);
            return string.CompareOrdinal(leftPart, rightPart);
        }
        return 0;
    }

    /// <summary>
    /// Rolls the per-tenant upgrade outcomes up into one fleet answer, and decides whether the rollout
    /// must stop. Halting on failures is the point: continuing to push a release that has already
    /// broken deployments is how one bad release becomes a fleet-wide outage. A draining tenant is not
    /// a failure — it is work still finishing — so it never counts toward the halt.
    /// </summary>
    public static FleetRolloutStatus Track(
        FleetRolloutPlan plan,
        IReadOnlyDictionary<string, FleetTenantProgress> progress,
        int maxFailures)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(progress);
        if (maxFailures < 0)
            throw new ArgumentOutOfRangeException(nameof(maxFailures));

        var tenants = new List<FleetTenantProgress>();
        foreach (var tenant in plan.Upgradeable)
        {
            tenants.Add(progress.TryGetValue(tenant.TenantId, out var reported)
                ? reported
                : new FleetTenantProgress(tenant.TenantId, FleetRolloutOutcome.Pending));
        }

        var completed = tenants.Count(value => value.Outcome == FleetRolloutOutcome.Completed);
        var draining = tenants.Count(value => value.Outcome == FleetRolloutOutcome.Draining);
        var failed = tenants.Count(value => value.Outcome == FleetRolloutOutcome.Failed);
        var pending = tenants.Count(value => value.Outcome == FleetRolloutOutcome.Pending);
        var halted = failed > maxFailures;
        return new FleetRolloutStatus(
            plan.TargetRelease, completed, draining, failed, pending, halted,
            halted
                ? $"{failed} deployment(s) failed, above the tolerated {maxFailures}; " +
                  "the rollout stopped before pushing the release further."
                : null,
            tenants);
    }
}
