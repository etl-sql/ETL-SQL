namespace ETL_SQL.Portal.Models;

/// <summary>
/// Recovery and host-identity posture, read-only. Custody, restore, and enrolment all stay outside
/// the running Portal — what travels here is the <em>evidence</em> that they happened, plus what to
/// do when it is missing.
/// </summary>
public sealed record OperationsPostureDto(
    BackupPostureDto Backup,
    RestoreDrillPostureDto RestoreDrill,
    HostEnrollmentPostureDto HostEnrollment);

/// <param name="Fresh">Whether the last successful backup is within the configured freshness policy.</param>
/// <param name="MaxAgeHours">The freshness policy a reading is judged against.</param>
public sealed record BackupPostureDto(
    bool EverRun,
    string? LastStatus,
    DateTime? LastBackupUtc,
    int? AgeHours,
    string? LastExitCode,
    bool Fresh,
    int MaxAgeHours,
    IReadOnlyList<string> Findings,
    string Remediation);

/// <param name="Mode">Whether the last drill validated an archive or performed a full restore.</param>
/// <param name="EverRun">
/// False means no archive has ever been proven readable. A backup nobody has restored is a hope, not
/// a recovery plan, so this is reported as a finding rather than left blank.
/// </param>
public sealed record RestoreDrillPostureDto(
    bool EverRun,
    string? Mode,
    string? LastStatus,
    DateTime? LastDrillUtc,
    int? AgeDays,
    int? Problems,
    IReadOnlyList<string> Findings,
    string Remediation);

/// <param name="Consistent">
/// Whether the host's own enrolment and the Portal's machine registration agree. They are recorded
/// in different places by different commands, so they can drift — a reassigned or copied identity
/// looks healthy from either side alone.
/// </param>
public sealed record HostEnrollmentPostureDto(
    bool HostEnrolled,
    string? MachineId,
    string? Tenant,
    string? Environment,
    bool RegisteredInPortal,
    bool Revoked,
    DateTimeOffset? RegisteredAtUtc,
    DateTimeOffset? LastSeenAtUtc,
    bool Consistent,
    HostCertificatePostureDto Certificate,
    IReadOnlyList<string> Findings,
    string Remediation);

/// <param name="ThumbprintMatches">
/// Whether the certificate the host holds is the one the Portal expects. A mismatch means the host
/// cannot authenticate even though both sides believe they are configured.
/// </param>
public sealed record HostCertificatePostureDto(
    bool HostHasCertificate,
    bool PortalExpectsCertificate,
    bool? ThumbprintMatches,
    DateTimeOffset? NotAfterUtc,
    int? DaysUntilExpiry,
    bool Expired,
    bool ExpiringSoon);
