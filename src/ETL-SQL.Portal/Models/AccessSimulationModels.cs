namespace ETL_SQL.Portal.Models;

/// <summary>
/// One identity's access, explained. Every section says <em>why</em>, not just what — an answer with
/// no reasoning cannot be acted on, and "why can they still see this?" is the question that gets
/// asked.
/// </summary>
public sealed record AccessSimulationDto(
    AccessSimulationIdentityDto Identity,
    AccessSimulationStudioDto Studio,
    IReadOnlyList<AccessSimulationConnectionDto> Connections,
    AccessSimulationReportDto? Report,
    AccessSimulationDatasetDto? Dataset);

/// <param name="IsActive">
/// A disabled account keeps its grants on paper; delivery and sign-in refuse it. Reporting the
/// grants without the account state would read as access the user does not have.
/// </param>
public sealed record AccessSimulationIdentityDto(
    int UserId,
    string Username,
    bool IsActive,
    bool IsAdmin,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Groups);

/// <param name="FromRoles">Configured role mapping — changed in configuration.</param>
/// <param name="FromGroups">Group grants — changed in the Portal. Split because the remedy differs.</param>
public sealed record AccessSimulationStudioDto(
    string Mode,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> FromRoles,
    IReadOnlyList<string> FromGroups);

public sealed record AccessSimulationConnectionDto(
    string Alias,
    string ConnectorType,
    bool Disabled,
    bool Usable,
    string Reason);

public sealed record AccessSimulationReportDto(
    int ReportId,
    string Name,
    string FolderPath,
    string? Permission,
    IReadOnlyList<string> Sources,
    bool CanView,
    bool CanExecute,
    bool CanManage,
    AccessSimulationRlsDto RowLevelSecurity);

/// <summary>
/// What row-level security would do to this identity — named, never executed.
/// </summary>
/// <param name="IdentitySensitive">Null when the script could not be read and so was not scanned.</param>
/// <param name="IdentityReferences">The identity tokens the script mentions, e.g. <c>HAS_GROUP</c>.</param>
public sealed record AccessSimulationRlsDto(
    bool? IdentitySensitive,
    IReadOnlyList<string> IdentityReferences,
    string? BoundUser,
    IReadOnlyList<string>? BoundGroups,
    string Explanation);

public sealed record AccessSimulationDatasetDto(
    int DatasetId,
    string Name,
    string AccessLevel,
    string? Permission,
    IReadOnlyList<string> Sources);
