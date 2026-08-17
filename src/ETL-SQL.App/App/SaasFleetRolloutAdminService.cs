using System.Text.Json;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Storage;

namespace ETL_SQL.App;

/// <summary>
/// Plans a release rollout across the Dedicated fleet — which deployments are eligible, in what
/// waves, and which are blocked and why — and with <c>--execute</c> walks those waves.
///
/// <para>It holds no authority of its own. Every cutover runs through
/// <see cref="SaasTenantUpgradeService"/> under its own signed, tenant-scoped authorization, and a
/// deployment the loaded authorization does not name is reported as still owed rather than upgraded.
/// A rollout therefore advances exactly as far as the grants an operator has already obtained, and
/// never further.</para>
/// </summary>
internal static class SaasFleetRolloutAdminService
{
    internal static async Task<int> RunAsync(
        CliContext context,
        ILogger logger,
        IOrchestratorStoreFactory storeFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var authorization = FleetInventoryAuthorization.Issue(
                context.FleetOperator ?? string.Empty,
                context.FleetAuthorizationReference ?? string.Empty,
                context.FleetReason ?? string.Empty,
                now.AddMinutes(15),
                now);

            if (storeFactory.Create() is not ISharedTenantLifecycleStore store)
            {
                logger.WriteLine(
                    "The configured Orchestrator store cannot report tenant lifecycle state, so the " +
                    "fleet population is unknown.", ConsoleColor.Red);
                return 1;
            }

            var population = await store.ListSharedTenantStatesAsync(authorization, now, cancellationToken);
            if (population.Count == 0)
            {
                logger.WriteLine("No tenant deployments are recorded in the control plane.", ConsoleColor.Yellow);
                return 0;
            }

            var plan = SaasFleetRolloutPlanner.Plan(
                population, context.FleetTargetRelease!, context.FleetWaveSize);
            Report(plan, logger);
            if (!context.FleetExecute)
            {
                logger.WriteLine(
                    "Add --execute to walk these waves. Each cutover still runs under its own signed " +
                    "authorization, so the rollout advances only as far as those reach.",
                    ConsoleColor.Yellow);
                return 0;
            }

            if (string.IsNullOrWhiteSpace(context.FleetRoot))
            {
                logger.WriteLine(
                    "--fleet-root is required with --execute: it is the root the deployments were " +
                    "onboarded under, where each tenant occupies its own directory.", ConsoleColor.Red);
                return 1;
            }

            var run = await SaasFleetRolloutSequencer.RunAsync(
                plan,
                (tenant, ct) => CutoverAsync(context, tenant, logger, ct),
                context.FleetMaxFailures,
                cancellationToken);
            ReportRun(run, logger);
            return run.Status.Halted ? 1 : run.Status.Pending > 0 || run.Status.Draining > 0 ? 2 : 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException
                                   or InvalidOperationException or IOException or JsonException)
        {
            logger.WriteLine($"Fleet rollout planning failed: {ex.Message}", ConsoleColor.Red);
            return 1;
        }
    }

    /// <summary>
    /// Cuts one deployment over through the ordinary single-tenant path. It refuses any tenant the
    /// loaded signed authorization does not name — the rollout does not widen authority, it walks as
    /// far as the authorizations already granted reach.
    /// </summary>
    private static async Task<FleetCutoverResult> CutoverAsync(
        CliContext context,
        FleetTenantPlan tenant,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var authorized = EnterprisePolicyRuntime.Current.Document?.SaasUpgrade;
        if (authorized?.Enabled != true ||
            !string.Equals(authorized.TenantId, tenant.TenantId, StringComparison.Ordinal))
        {
            logger.WriteLine(
                $"  {tenant.TenantId}: awaiting a signed authorization naming this tenant.",
                ConsoleColor.Yellow);
            return new FleetCutoverResult(
                tenant.TenantId, FleetRolloutOutcome.Pending, FleetCutoverRefusal.NotAuthorized,
                "No loaded signed authorization names this tenant.");
        }

        // Deployments live one directory per tenant under the root they were onboarded to.
        var tenantContext = context.Clone();
        tenantContext.SaasTenantId = tenant.TenantId;
        tenantContext.SaasUpgradeTenantRoot = Path.Combine(
            Path.GetFullPath(context.FleetRoot!), tenant.TenantId);
        tenantContext.SaasUpgradeTargetRelease = context.FleetTargetRelease;
        tenantContext.SaasUpgradeMaxConcurrentJobs = authorized.MaxConcurrentJobs;
        tenantContext.SaasUpgradeMaxStorageMb = authorized.MaxStorageMb;
        tenantContext.SaasUpgradeMaxReportSessions = authorized.MaxReportSessions;
        tenantContext.SaasUpgradeExecute = true;

        try
        {
            var now = DateTimeOffset.UtcNow;
            var authority = SaasTenantUpgradeService.ResolveAuthorizedContext(
                tenantContext, EnterprisePolicyRuntime.Current, now);
            var receipt = await SaasTenantUpgradeService.UpgradeAsync(
                tenantContext, authority, now, execute: true, cancellationToken);
            return receipt.Status switch
            {
                "Draining" => new FleetCutoverResult(
                    tenant.TenantId, FleetRolloutOutcome.Draining, Detail:
                    $"{receipt.BlockingAdmissions.Count} admission(s) still to finish or reconcile."),
                "Completed" => new FleetCutoverResult(tenant.TenantId, FleetRolloutOutcome.Completed),
                _ => new FleetCutoverResult(
                    tenant.TenantId, FleetRolloutOutcome.Failed, Detail: receipt.Failure ?? receipt.Status)
            };
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException
                                   or ArgumentException or IOException or JsonException)
        {
            return new FleetCutoverResult(
                tenant.TenantId, FleetRolloutOutcome.Failed, Detail: ex.Message);
        }
    }

    private static void ReportRun(FleetRolloutRun run, ILogger logger)
    {
        logger.WriteLine(
            $"Rollout walked {run.WavesStarted} wave(s), {run.WavesCompleted} completed: " +
            $"{run.Status.Completed} upgraded, {run.Status.Draining} draining, " +
            $"{run.Status.Failed} failed, {run.Status.Pending} still owed.",
            run.Status.Halted ? ConsoleColor.Red : ConsoleColor.Cyan);
        if (run.Status.HaltReason is not null)
            logger.WriteLine($"  {run.Status.HaltReason}", ConsoleColor.Red);

        foreach (var result in run.Results.Where(value => value.Refusal != FleetCutoverRefusal.None))
            logger.WriteLine($"  {result.TenantId}: not attempted — {result.Refusal}.", ConsoleColor.Yellow);
        foreach (var result in run.Results.Where(value => value.Outcome == FleetRolloutOutcome.Failed))
            logger.WriteLine($"  {result.TenantId}: failed — {result.Detail}", ConsoleColor.Red);
    }

    private static void Report(FleetRolloutPlan plan, ILogger logger)
    {
        var upgradeable = plan.Upgradeable.ToArray();
        logger.WriteLine(
            $"Fleet rollout to {plan.TargetRelease}: {upgradeable.Length} of {plan.Tenants.Count} " +
            $"deployment(s) eligible, in {plan.WaveCount} wave(s) of up to {plan.WaveSize}.",
            ConsoleColor.Cyan);

        for (var wave = 0; wave < plan.WaveCount; wave++)
        {
            var members = upgradeable.Where(tenant => tenant.Wave == wave).ToArray();
            logger.WriteLine(
                $"  Wave {wave + 1}: {string.Join(", ", members.Select(tenant => tenant.TenantId))}");
        }

        // Everything not being rolled is named with its reason. A deployment silently missing from a
        // fleet rollout is how one stack stays a release behind for a year.
        foreach (var skipped in plan.Tenants.Where(tenant => tenant.Decision != FleetRolloutDecision.Upgrade))
        {
            logger.WriteLine(
                $"  {skipped.TenantId}: {skipped.Decision} ({skipped.CurrentRelease}) — {skipped.Reason}",
                skipped.Decision == FleetRolloutDecision.AlreadyCurrent
                    ? ConsoleColor.DarkGray
                    : ConsoleColor.Yellow);
        }

        logger.WriteLine(
            "Each deployment is cut over separately with 'admin promotion saas-upgrade' under its own " +
            "signed authorization; this plan confers no authority.",
            ConsoleColor.DarkGray);
    }
}
