namespace ETL_SQL.Portal.Models;

/// <param name="LastScan">
/// Null when the estate has never been scanned. The dashboard must render that differently from a
/// completed scan that found nothing: "no findings" and "never looked" are opposite conclusions, and
/// a KPI tile showing zero cannot distinguish them on its own.
/// </param>
public sealed record GovernanceDashboardDto(
    GovernanceSummaryDto Summary,
    IReadOnlyList<GovernanceAssetDto> Assets,
    StewardshipScanDto? LastScan);

public sealed record GovernanceSummaryDto(
    int TotalAssets,
    int GovernedAssets,
    int BelowThreshold,
    int OpenFindings,
    int IgnoredFindings,
    int AcceptedRisks,
    int TargetScore);

/// <param name="AutomaticBadges">
/// Computed from current evidence on every read, never stored — a stored automatic badge would
/// outlive the evidence that justified it.
/// </param>
/// <param name="AssignedBadges">Steward decisions, which are stored precisely because they are decisions.</param>
public sealed record GovernanceAssetDto(
    string AssetKey,
    string AssetVersion,
    string? ScriptPath,
    string? Owner,
    string? Steward,
    string? Domain,
    string? Classification,
    int Score,
    bool Governed,
    IReadOnlyList<GovernanceDeductionDto> Deductions,
    IReadOnlyList<string> AutomaticBadges,
    IReadOnlyList<string> AssignedBadges,
    DateTime? ReviewedAtUtc,
    string? ReviewedVersion,
    IReadOnlyList<StewardshipFindingDto> Findings);

/// <summary>One lost point mapped to the rule that took it. Scores are explainable or they are noise.</summary>
public sealed record GovernanceDeductionDto(string RuleKey, int Points, string Reason);

public sealed record StewardshipFindingDto(
    int Id,
    string AssetKey,
    string RuleKey,
    string? AssetVersion,
    string? Detail,
    string Status,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc,
    DateTime? ResolvedAtUtc,
    DateTime? SuppressedUntilUtc,
    IReadOnlyList<GovernanceDecisionDto> Decisions);

public sealed record GovernanceDecisionDto(
    int Id,
    string Decision,
    string? CategoryValue,
    string Reason,
    string? AssetVersion,
    DateTime DecidedAtUtc,
    string? DecidedBy);

public sealed record StewardshipScanDto(
    int Id,
    string Trigger,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    string Status,
    string? Error,
    int AssetsScanned,
    int FindingsOpened,
    int FindingsResolved,
    int FindingsReopened);

public sealed record StewardshipSettingsDto(
    int TargetScore,
    bool EnableMetadataCheck,
    bool EnableProtectedDataCheck,
    bool EnableGlossaryCheck,
    bool EnableStalenessCheck,
    int DeductMetadata,
    int DeductProtectedData,
    int DeductGlossary,
    int DeductStaleness,
    int StaleAfterDays,
    string PolicyLevel,
    DateTime UpdatedAtUtc,
    long Version);

public sealed record UpdateStewardshipSettingsRequest(
    int TargetScore,
    bool EnableMetadataCheck,
    bool EnableProtectedDataCheck,
    bool EnableGlossaryCheck,
    bool EnableStalenessCheck,
    int DeductMetadata,
    int DeductProtectedData,
    int DeductGlossary,
    int DeductStaleness,
    int StaleAfterDays,
    string? PolicyLevel);

public sealed record GovernanceCategoryDto(
    int Id, string Value, string Label, string Color, int? ExpiryDays, bool Disabled);

public sealed record SaveGovernanceCategoryRequest(
    string Value, string Label, string? Color, int? ExpiryDays, bool Disabled);

public sealed record StewardshipGlossaryTermDto(
    int Id,
    string Term,
    string DataType,
    string Aliases,
    string Description,
    string? Formula,
    string? Steward,
    bool Disabled,
    DateTime UpdatedAtUtc);

public sealed record SaveGlossaryTermRequest(
    string Term,
    string DataType,
    string Aliases,
    string Description,
    string? Formula,
    string? Steward,
    bool Disabled);

/// <param name="AssetVersion">
/// The version the steward is deciding about. Required: a decision with no version cannot be
/// reopened when the asset changes, which is the whole mechanism that keeps suppressions honest.
/// </param>
public sealed record DecideFindingRequest(
    string Decision,
    string? CategoryValue,
    string Reason,
    string AssetVersion);

public sealed record AssignBadgeRequest(
    string AssetKey, string Badge, string? AssetVersion, string? Reason);

public sealed record ReviewAssetRequest(
    string AssetKey, string AssetVersion, string? Note);
