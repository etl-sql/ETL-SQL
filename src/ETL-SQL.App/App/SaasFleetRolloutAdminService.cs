using System.Text.Json;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Storage;

namespace ETL_SQL.App;

/// <summary>
/// Plans a release rollout across the Dedicated fleet: which deployments are eligible, in what waves,
/// and which are blocked and why. It plans only — every cutover still runs through
/// <see cref="SaasTenantUpgradeService"/> under its own signed, tenant-scoped authorization, so this
/// command cannot upgrade anything and does not ask for the authority to.
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
            return 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException
                                   or InvalidOperationException or IOException or JsonException)
        {
            logger.WriteLine($"Fleet rollout planning failed: {ex.Message}", ConsoleColor.Red);
            return 1;
        }
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
