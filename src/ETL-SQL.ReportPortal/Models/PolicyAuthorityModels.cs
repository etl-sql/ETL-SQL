using ETL_SQL.Core.Governance;

namespace ETL_SQL.ReportPortal.Models;

public record PolicyValidateRequest(string PolicyJson);

public record PolicyPublishRequest(
    string Tenant,
    string Environment,
    string PolicyVersion,
    string PolicyJson,
    string? Reviewer,
    DateTimeOffset ExpiresAtUtc,
    bool Staged = false);

public record PolicyActivateRequest(string Tenant, string Environment, string PolicyVersion);

public record PolicyRollbackRequest(
    string Tenant,
    string Environment,
    string TargetPolicyVersion,
    string NewPolicyVersion,
    string? Reviewer,
    DateTimeOffset ExpiresAtUtc);

public record PolicyMachineRegisterRequest(
    string MachineId,
    string EnrollmentId,
    string Tenant,
    string Environment,
    string? ClientCertificateThumbprint,
    string? CanaryGroup = null);

public record PolicyMachineRevokeRequest(string? Reason);

public record PolicyMachineDto(
    string MachineId,
    string EnrollmentId,
    string Tenant,
    string Environment,
    bool RequiresClientCertificate,
    bool Revoked,
    DateTimeOffset? RevokedAtUtc,
    string? RevokedReason,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset? LastSeenAtUtc,
    string? CanaryGroup)
{
    public static PolicyMachineDto From(ETL_SQL.ReportPortal.Data.PolicyMachineEntity m) => new(
        m.MachineId, m.EnrollmentId, m.Tenant, m.Environment,
        !string.IsNullOrWhiteSpace(m.ClientCertificateThumbprint),
        m.Revoked, m.RevokedAtUtc, m.RevokedReason, m.RegisteredAtUtc, m.LastSeenAtUtc, m.CanaryGroup);
}

public record PolicyVersionDto(
    string Tenant,
    string Environment,
    string PolicyVersion,
    string PolicyHash,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string Author,
    string? Reviewer,
    string? SupersededVersion,
    string RolloutState,
    DateTimeOffset PublishedAtUtc,
    string? CanaryGroup,
    int? CanaryPercentage)
{
    public static PolicyVersionDto From(PublishedPolicyVersion v) => new(
        v.Tenant, v.Environment, v.PolicyVersion, v.PolicyHash, v.IssuedAtUtc, v.ExpiresAtUtc,
        v.Author, v.Reviewer, v.SupersededVersion, v.RolloutState.ToString(), v.PublishedAtUtc,
        v.Canary?.Group, v.Canary?.Percentage);
}
