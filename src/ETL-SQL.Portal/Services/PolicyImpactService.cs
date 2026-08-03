using System.Text.Json;
using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Answers the question asked immediately before pressing activate: what happens when I do?
///
/// Policy Authority could already validate, publish, activate, canary and roll back — every verb,
/// and no consequence. The three that matter are all knowable in advance: which machines actually
/// receive this version (as opposed to which are merely registered), whether anyone other than the
/// author approved it, and whether the policy's audit requirement is about to start refusing
/// mutations against a collector that is not currently healthy.
/// </summary>
public sealed class PolicyImpactService(
    PortalDbContext db,
    AuditCollectorHealthService collectorHealth,
    TimeProvider clock)
{
    /// <summary>A machine not seen for this long is treated as not currently reachable by a rollout.</summary>
    public const int StaleAfterHours = 24;

    public async Task<PolicyImpactDto?> BuildAsync(
        string tenant, string environment, string? policyVersion, CancellationToken ct = default)
    {
        var versions = await db.Set<PolicyVersionEntity>()
            .AsNoTracking()
            .Where(entity => entity.Tenant == tenant && entity.Environment == environment)
            .ToListAsync(ct);

        var target = policyVersion is null
            ? versions.FirstOrDefault(entity => entity.RolloutState == "Active")
            : versions.FirstOrDefault(entity =>
                string.Equals(entity.PolicyVersion, policyVersion, StringComparison.OrdinalIgnoreCase));
        if (target is null) return null;

        var active = versions.FirstOrDefault(entity => entity.RolloutState == "Active");
        var canary = versions.FirstOrDefault(entity => entity.RolloutState == "Canary");

        var machines = await db.Set<PolicyMachineEntity>()
            .AsNoTracking()
            .Where(entity => entity.Tenant == tenant && entity.Environment == environment)
            .OrderBy(entity => entity.MachineId)
            .ToListAsync(ct);

        var now = clock.GetUtcNow();
        var machineImpact = machines
            .Select(machine =>
            {
                var hoursSinceSeen = machine.LastSeenAtUtc is DateTimeOffset seen
                    ? (int)Math.Max(0, (now - seen).TotalHours)
                    : (int?)null;
                var inCanaryGroup = canary?.CanaryGroup is { Length: > 0 } group
                    && string.Equals(machine.CanaryGroup, group, StringComparison.OrdinalIgnoreCase);

                return new PolicyMachineImpactDto(
                    machine.MachineId,
                    machine.CanaryGroup,
                    machine.Revoked,
                    machine.LastSeenAtUtc,
                    hoursSinceSeen,
                    Stale: hoursSinceSeen is null || hoursSinceSeen > StaleAfterHours,
                    EffectiveVersion: machine.Revoked
                        ? "none (revoked)"
                        : inCanaryGroup
                            ? canary!.PolicyVersion
                            : active?.PolicyVersion ?? "none");
            })
            .ToList();

        var live = machineImpact.Where(machine => !machine.Revoked).ToList();
        var targeted = live.Count(machine =>
            string.Equals(machine.EffectiveVersion, target.PolicyVersion, StringComparison.OrdinalIgnoreCase));

        var fleetFindings = new List<string>();
        if (machines.Count == 0)
            fleetFindings.Add("No machines are registered for this tenant and environment.");
        var stale = live.Count(machine => machine.Stale);
        if (stale > 0)
        {
            // The count that misleads: registered is not the same as reachable.
            fleetFindings.Add(
                $"{stale} of {live.Count} live machine(s) have not been seen in over {StaleAfterHours}h and "
                + "will not pick this up until they check in.");
        }
        if (target.RolloutState == "Canary" && string.IsNullOrWhiteSpace(target.CanaryGroup))
            fleetFindings.Add("This canary version targets no machine group, so it reaches nothing.");

        return new PolicyImpactDto(
            BuildVersion(target, now),
            BuildApproval(target),
            new PolicyFleetImpactDto(
                machines.Count,
                targeted,
                CanaryTargeted: live.Count(machine =>
                    canary is not null
                    && string.Equals(machine.EffectiveVersion, canary.PolicyVersion, StringComparison.OrdinalIgnoreCase)),
                Revoked: machineImpact.Count(machine => machine.Revoked),
                stale,
                NeverSeen: live.Count(machine => machine.LastSeenAtUtc is null),
                StaleAfterHours,
                fleetFindings),
            await BuildCollectorAsync(target, ct),
            machineImpact);
    }

    private static PolicyImpactVersionDto BuildVersion(PolicyVersionEntity target, DateTimeOffset now)
    {
        var daysUntilExpiry = (int)Math.Floor((target.ExpiresAtUtc - now).TotalDays);
        return new PolicyImpactVersionDto(
            target.Tenant,
            target.Environment,
            target.PolicyVersion,
            target.PolicyHash,
            target.RolloutState,
            target.CanaryGroup,
            target.SupersededVersion,
            target.IssuedAtUtc,
            target.ExpiresAtUtc,
            Expired: target.ExpiresAtUtc <= now,
            daysUntilExpiry);
    }

    private static PolicyApprovalStateDto BuildApproval(PolicyVersionEntity target)
    {
        var reviewed = !string.IsNullOrWhiteSpace(target.Reviewer);
        var separated = reviewed
            && !string.Equals(target.Reviewer, target.Author, StringComparison.OrdinalIgnoreCase);

        return new PolicyApprovalStateDto(
            target.Author,
            target.Reviewer,
            reviewed,
            separated,
            !reviewed
                ? "No reviewer is recorded: this version was published on one person's authority."
                : separated
                    ? $"Reviewed by {target.Reviewer}, who is not the author."
                    // A filled-in reviewer field is not the same as a second pair of eyes.
                    : "The recorded reviewer is the author, so no second pair of eyes approved this.");
    }

    /// <summary>
    /// The consequence most likely to surprise: a policy requiring remote audit delivery turns an
    /// unhealthy collector into refused mutations. Both halves are already known — the policy says
    /// what it requires, and the collector says whether it is deliverable — so the answer should not
    /// have to be discovered by activating.
    /// </summary>
    private async Task<PolicyCollectorConsequenceDto> BuildCollectorAsync(
        PolicyVersionEntity target, CancellationToken ct)
    {
        bool? requiresRemote = null;
        try
        {
            var envelope = JsonSerializer.Deserialize<SignedOrganizationPolicyEnvelope>(
                target.SignedEnvelopeJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (envelope is not null)
            {
                // The payload is the base64 policy document the signature covers; reading it here is
                // a read of the same bytes the runtime verifies, not a second source of truth.
                var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(envelope.PolicyPayload));
                requiresRemote = OrganizationPolicySchema.ParseJson(json)
                    .MutationGuardrails.RequireRemoteAuditForMutations;
            }
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        {
            // An unreadable envelope reports unknown rather than guessing at a security requirement.
        }

        var health = await collectorHealth.BuildAsync(ct);
        var deliverable = !health.FailClosed.Tripped;
        var wouldBlock = requiresRemote == true && (!health.CollectorConfigured || !deliverable);

        return new PolicyCollectorConsequenceDto(
            requiresRemote,
            health.CollectorConfigured,
            deliverable,
            wouldBlock,
            requiresRemote != true
                ? "This policy does not require remote audit delivery, so activating it cannot block mutations."
                : wouldBlock
                    ? "This policy requires remote audit delivery and the collector is not currently "
                      + "healthy. Activating it will refuse security-sensitive mutations with HTTP 503 "
                      + "until delivery recovers."
                    : "This policy requires remote audit delivery and the collector is currently healthy.");
    }
}
