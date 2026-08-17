namespace ETL_SQL.App;

/// <summary>Why a deployment's cutover was not attempted on this pass.</summary>
public enum FleetCutoverRefusal
{
    /// <summary>It was attempted.</summary>
    None,

    /// <summary>
    /// No signed authorization naming this tenant is loaded. Each cutover keeps its own tenant-scoped
    /// grant, so a rollout walks as far as its authorizations reach and no further.
    /// </summary>
    NotAuthorized,

    /// <summary>The rollout had already halted when this deployment came up.</summary>
    RolloutHalted,

    /// <summary>An earlier wave is still draining; starting this one would overlap them.</summary>
    EarlierWaveDraining
}

public sealed record FleetCutoverResult(
    string TenantId,
    FleetRolloutOutcome Outcome,
    FleetCutoverRefusal Refusal = FleetCutoverRefusal.None,
    string? Detail = null);

public sealed record FleetRolloutRun(
    FleetRolloutStatus Status,
    IReadOnlyList<FleetCutoverResult> Results,
    int WavesStarted,
    int WavesCompleted);

/// <summary>
/// Walks a <see cref="FleetRolloutPlan"/> wave by wave and applies each deployment's cutover through
/// the caller's executor.
///
/// <para>It holds no authority of its own. The executor is expected to refuse any tenant it has no
/// signed, tenant-scoped authorization for, and the sequencer records that refusal and keeps going
/// rather than treating it as a failure — a rollout legitimately walks as far as its authorizations
/// reach and stops there.</para>
/// </summary>
public static class SaasFleetRolloutSequencer
{
    /// <summary>Applies a cutover to one deployment. Implementations own all authorization.</summary>
    public delegate Task<FleetCutoverResult> CutoverAsync(
        FleetTenantPlan tenant,
        CancellationToken cancellationToken);

    public static async Task<FleetRolloutRun> RunAsync(
        FleetRolloutPlan plan,
        CutoverAsync cutover,
        int maxFailures,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(cutover);
        if (maxFailures < 0) throw new ArgumentOutOfRangeException(nameof(maxFailures));

        var results = new List<FleetCutoverResult>();
        var progress = new Dictionary<string, FleetTenantProgress>(StringComparer.Ordinal);
        var wavesStarted = 0;
        var wavesCompleted = 0;
        var halted = false;
        var draining = false;

        for (var wave = 0; wave < plan.WaveCount; wave++)
        {
            var members = plan.Upgradeable.Where(tenant => tenant.Wave == wave).ToArray();
            if (members.Length == 0) continue;

            // Two reasons to stop opening waves. A halt means the release has already broken
            // deployments; a wave still draining means its work has not finished, and overlapping it
            // with the next wave is how a rollout takes down more of the fleet than it has repaired.
            var blocked = halted
                ? FleetCutoverRefusal.RolloutHalted
                : draining
                    ? FleetCutoverRefusal.EarlierWaveDraining
                    : FleetCutoverRefusal.None;
            if (blocked != FleetCutoverRefusal.None)
            {
                foreach (var tenant in members)
                    Record(new FleetCutoverResult(tenant.TenantId, FleetRolloutOutcome.Pending, blocked));
                continue;
            }

            wavesStarted++;
            foreach (var tenant in members)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Record(await cutover(tenant, cancellationToken).ConfigureAwait(false));
            }

            var afterWave = SaasFleetRolloutPlanner.Track(plan, progress, maxFailures);
            halted = afterWave.Halted;
            draining = members.Any(tenant =>
                progress.TryGetValue(tenant.TenantId, out var value)
                && value.Outcome == FleetRolloutOutcome.Draining);
            if (!halted && !draining) wavesCompleted++;
        }

        return new FleetRolloutRun(
            SaasFleetRolloutPlanner.Track(plan, progress, maxFailures),
            results,
            wavesStarted,
            wavesCompleted);

        void Record(FleetCutoverResult result)
        {
            results.Add(result);
            // An unauthorized deployment stays Pending: it is work still owed, not work that failed,
            // and counting it as a failure would halt a rollout that is merely waiting for approval.
            if (result.Refusal == FleetCutoverRefusal.None || result.Outcome != FleetRolloutOutcome.Pending)
                progress[result.TenantId] = new FleetTenantProgress(
                    result.TenantId, result.Outcome, result.Detail);
        }
    }
}
