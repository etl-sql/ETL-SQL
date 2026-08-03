namespace ETL_SQL.Portal.Models;

/// <summary>
/// Dataset at-rest key posture. Key <em>versions</em> are non-secret identifiers and are named
/// freely; key <b>material</b> is never in any of these types, only whether a key is configured.
/// </summary>
public sealed record DatasetKeyPostureDto(
    string CurrentVersion,
    bool CurrentKeyConfigured,
    IReadOnlyList<DatasetKeyVersionDto> Inventory,
    DatasetKeyRotationPreflightDto Preflight,
    DatasetKeyVerificationDto Verification,
    string RollbackGuidance);

/// <param name="KeyConfigured">
/// Whether a key is still configured for this version. A version with datasets and no key is the
/// state that matters: those datasets cannot be rotated, and cannot be read either.
/// </param>
public sealed record DatasetKeyVersionDto(
    string Version,
    int DatasetCount,
    bool IsCurrent,
    bool KeyConfigured);

/// <param name="Blocked">
/// Datasets that cannot rotate, with the reason. Discovering these mid-rotation means finding out
/// during the operation what could have been known before it.
/// </param>
public sealed record DatasetKeyRotationPreflightDto(
    bool CanProceed,
    int WouldRotate,
    int AlreadyCurrent,
    IReadOnlyList<DatasetKeyBlockerDto> Blocked,
    IReadOnlyList<string> Findings);

public sealed record DatasetKeyBlockerDto(int DatasetId, string Name, string Version, string Reason);

/// <param name="FullyRotated">Every encrypted dataset is stamped with the current version.</param>
/// <param name="RetiredVersionsStillConfigured">
/// Versions no dataset references any more whose keys are still configured. Not an error — backups
/// taken under them still need those keys — but they are the cleanup a rotation is not finished
/// without.
/// </param>
public sealed record DatasetKeyVerificationDto(
    bool FullyRotated,
    int OnCurrentVersion,
    int OnOtherVersions,
    int MissingFiles,
    IReadOnlyList<string> RetiredVersionsStillConfigured);
