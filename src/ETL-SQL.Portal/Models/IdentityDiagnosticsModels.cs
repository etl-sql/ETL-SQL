namespace ETL_SQL.Portal.Models;

/// <summary>
/// Identity-provider health for an operator, with no secret in it anywhere: a configured secret is
/// reported as a presence flag, never a value.
/// </summary>
public sealed record IdentityDiagnosticsDto(
    string ConfiguredProvider,
    IdentityOidcDiagnosticsDto Oidc,
    IdentityLdapDiagnosticsDto Ldap,
    IReadOnlyList<IdentityGroupMappingDto> GroupMappings,
    IdentitySyncHealthDto SyncHealth,
    IdentityBreakGlassDto BreakGlass);

/// <param name="ConfigErrors">Startup validation findings — the usual reason federated login fails.</param>
/// <param name="DiscoveryReachable">Null when OIDC is disabled and no probe was attempted.</param>
public sealed record IdentityOidcDiagnosticsDto(
    bool Enabled,
    string? Authority,
    string? ClientId,
    bool ClientSecretConfigured,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<string> GroupClaimTypes,
    IReadOnlyList<string> ConfigErrors,
    bool? DiscoveryReachable,
    string? DiscoveryIssuer,
    int? SigningKeyCount,
    string? DiscoveryError);

public sealed record IdentityLdapDiagnosticsDto(
    bool Enabled,
    string? Server,
    int Port,
    bool UseSsl,
    bool AllowSelfSignedCertificates,
    string? Domain,
    string? BaseDn,
    bool ServiceUserConfigured,
    bool ServicePasswordConfigured,
    int RoleMappingCount);

/// <param name="ClaimValue">
/// The value a provider must send for this group to be matched — the group's AD name when it has
/// one, otherwise its portal name. Naming it removes the guesswork from "why did nobody land in
/// this group?".
/// </param>
public sealed record IdentityGroupMappingDto(
    int GroupId,
    string GroupName,
    string ClaimValue,
    int MemberCount);

/// <param name="FederatedUsersWithNoMappedGroup">
/// Federated accounts in no provider-managed group. A non-zero count usually means the claim values
/// do not match the configured groups, which looks like working sign-in and broken authorization.
/// </param>
public sealed record IdentitySyncHealthDto(
    int FederatedUsers,
    int ActiveFederatedUsers,
    int FederatedUsersWithNoMappedGroup,
    int ProviderManagedGroups);

/// <param name="Ready">
/// Whether at least one active administrator could sign in with the identity provider unreachable.
/// </param>
/// <param name="LocalAdministrators">
/// Active administrators whose accounts are not provider-managed. Listed by name because the
/// remedy — go and check that account still works — needs to know which one.
/// </param>
public sealed record IdentityBreakGlassDto(
    bool Ready,
    IReadOnlyList<string> LocalAdministrators,
    string Explanation);

public sealed record GroupMappingTestRequest(IReadOnlyList<string>? ClaimValues);

/// <param name="Unmatched">Claim values that map to no group — the usual cause of a silent authorization gap.</param>
public sealed record GroupMappingTestResultDto(
    IReadOnlyList<IdentityGroupMappingDto> Matched,
    IReadOnlyList<string> Unmatched);
