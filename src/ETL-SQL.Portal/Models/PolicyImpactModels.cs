namespace ETL_SQL.Portal.Models;

/// <summary>
/// What activating a policy version would actually do: who receives it, whether anyone other than
/// its author approved it, and what it does to audit delivery.
///
/// Policy Authority could already validate, publish, activate, canary and roll back. What it could
/// not answer is the question asked immediately before pressing activate — <em>what happens when I
/// do?</em>
/// </summary>
public sealed record PolicyImpactDto(
    PolicyImpactVersionDto Version,
    PolicyApprovalStateDto Approval,
    PolicyFleetImpactDto Fleet,
    PolicyCollectorConsequenceDto Collector,
    IReadOnlyList<PolicyMachineImpactDto> Machines);

public sealed record PolicyImpactVersionDto(
    string Tenant,
    string Environment,
    string PolicyVersion,
    string PolicyHash,
    string RolloutState,
    string? CanaryGroup,
    string? SupersededVersion,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    bool Expired,
    int? DaysUntilExpiry);

/// <param name="SeparationOfDuties">
/// Whether the reviewer is someone other than the author. A version reviewed by its own author has a
/// reviewer field filled in and no second pair of eyes behind it.
/// </param>
public sealed record PolicyApprovalStateDto(
    string Author,
    string? Reviewer,
    bool Reviewed,
    bool SeparationOfDuties,
    string Explanation);

/// <param name="Targeted">Machines that would receive this version once it is active.</param>
/// <param name="Stale">
/// Registered machines not seen recently. A policy cannot reach a machine that is not collecting it,
/// so a large stale count means the rollout is narrower than the fleet count suggests.
/// </param>
public sealed record PolicyFleetImpactDto(
    int RegisteredMachines,
    int Targeted,
    int CanaryTargeted,
    int Revoked,
    int Stale,
    int NeverSeen,
    int StaleAfterHours,
    IReadOnlyList<string> Findings);

/// <param name="WouldBlockMutations">
/// True when this policy requires remote audit delivery and delivery is not currently healthy —
/// activating it starts refusing security-sensitive mutations with HTTP 503.
/// </param>
public sealed record PolicyCollectorConsequenceDto(
    bool? PolicyRequiresRemoteDelivery,
    bool CollectorConfigured,
    bool CurrentlyDeliverable,
    bool WouldBlockMutations,
    string Explanation);

/// <param name="EffectiveVersion">
/// The version this machine receives: the canary version when it is in the targeted group, otherwise
/// the active one. The link between a machine and the policy history that governs it.
/// </param>
public sealed record PolicyMachineImpactDto(
    string MachineId,
    string? CanaryGroup,
    bool Revoked,
    DateTimeOffset? LastSeenAtUtc,
    int? HoursSinceSeen,
    bool Stale,
    string EffectiveVersion);
