namespace ETL_SQL.Portal.Models;

/// <summary>
/// Secrets and shared connections seen together, because that is how they break: a connection whose
/// secret was renamed, disabled, or never created looks healthy on the connections page and healthy
/// on the secrets page, and fails the first time something runs.
///
/// No secret value appears in any of these types — only names, dates, and whether a reference
/// resolves.
/// </summary>
public sealed record CredentialPostureDto(
    IReadOnlyList<SecretPostureDto> Secrets,
    IReadOnlyList<ConnectionPostureDto> Connections,
    int RotationWarningDays,
    IReadOnlyList<string> Findings);

/// <param name="LastRotatedUtc">Last time the value was written — the only rotation date there is.</param>
/// <param name="ReferencedBy">Shared-connection aliases referencing this secret; the blast radius of disabling it.</param>
/// <param name="RequiredForPromotion">
/// Whether a configuration export emits this secret as a placeholder the target must supply.
/// </param>
public sealed record SecretPostureDto(
    string Name,
    bool Disabled,
    DateTime CreatedUtc,
    DateTime LastRotatedUtc,
    int AgeDays,
    bool RotationOverdue,
    IReadOnlyList<string> ReferencedBy,
    bool RequiredForPromotion,
    bool Orphaned);

/// <param name="UnresolvedSecrets">
/// Referenced secrets that are missing or disabled. This connection cannot authenticate, and neither
/// page shows it on its own.
/// </param>
/// <param name="UsableWithoutGrant">
/// True when the connection has no ACL rows, in which case every authenticated caller may use it.
/// Not a defect — it is the documented default — but it belongs next to the credential it guards.
/// </param>
public sealed record ConnectionPostureDto(
    string Alias,
    string ConnectorType,
    bool Disabled,
    IReadOnlyList<string> SecretReferences,
    IReadOnlyList<string> UnresolvedSecrets,
    bool UsableWithoutGrant,
    int GrantedGroups,
    DateTime? LastVerifiedUtc,
    DateTime? LastUsedUtc,
    bool Healthy);
