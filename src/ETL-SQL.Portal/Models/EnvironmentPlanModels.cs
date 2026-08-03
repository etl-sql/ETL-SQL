namespace ETL_SQL.Portal.Models;

/// <summary>
/// A deployment plan for one departmental environment: every isolated resource derived from the
/// environment id, as data a separately authorized deployment plane can act on.
///
/// <b>The Portal generates it and never applies it.</b> Provisioning an environment means creating
/// databases, OS accounts, key rings, and network endpoints — authority this process deliberately
/// does not hold, and must not, because the whole point of departmental isolation is that no
/// environment can reach into another. A plan is a description; acting on it is someone else's job.
///
/// It is also <b>secret-free</b>. Keys are described as requirements at named configuration keys,
/// never generated and never valued: a plan that carried key material would be a plan you could not
/// safely email, review, or store.
/// </summary>
public sealed record EnvironmentPlanDto(
    string EnvironmentId,
    int PortBase,
    IReadOnlyList<EnvironmentResourceDto> Resources,
    IReadOnlyList<EnvironmentPortDto> Ports,
    IReadOnlyList<EnvironmentSecretRequirementDto> SecretRequirements,
    IReadOnlyList<string> Notes);

/// <param name="IsolationRequirement">Why this resource must not be shared with another environment.</param>
public sealed record EnvironmentResourceDto(
    string Kind,
    string Name,
    string SingleNodeValue,
    string HighAvailabilityValue,
    string IsolationRequirement);

public sealed record EnvironmentPortDto(string Endpoint, int Port, int OffsetFromBase);

/// <param name="ConfigurationKey">Where the value belongs. The value itself is never generated here.</param>
public sealed record EnvironmentSecretRequirementDto(
    string ConfigurationKey,
    string Requirement,
    string SharedAcross);

/// <summary>
/// Whether a proposed plan can be deployed alongside what this Portal already knows about.
/// </summary>
/// <param name="Collisions">
/// Each is a resource the proposed environment would share with an existing one. Sharing any single
/// resource breaks isolation, so any collision fails validation outright rather than warning.
/// </param>
public sealed record EnvironmentPlanValidationDto(
    bool IsValid,
    string EnvironmentId,
    IReadOnlyList<EnvironmentCollisionDto> Collisions,
    IReadOnlyList<string> Warnings,
    string ProvisioningNote);

public sealed record EnvironmentCollisionDto(
    string Kind,
    string Detail,
    string ConflictsWith);

/// <summary>
/// This Portal's own environment measured against the isolation contract — the evidence half of the
/// workflow, distinct from planning a new one.
/// </summary>
public sealed record EnvironmentIsolationEvidenceDto(
    string EnvironmentId,
    IReadOnlyList<EnvironmentEvidenceItemDto> Evidence,
    IReadOnlyList<string> Findings,
    string FleetStatusPath);

/// <param name="Isolated">
/// Null where the Portal cannot tell from inside — a shared database login or a shared OS account is
/// visible to the deployment plane, not to the process running under it. Reported as unknown rather
/// than assumed good.
/// </param>
public sealed record EnvironmentEvidenceItemDto(
    string Resource,
    string Observed,
    bool? Isolated,
    string Note);

/// <param name="PortBase">Omit to skip the port-block check.</param>
public sealed record ValidateEnvironmentPlanRequest(string? EnvironmentId, int? PortBase);
