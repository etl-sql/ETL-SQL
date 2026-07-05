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
    DateTimeOffset PublishedAtUtc)
{
    public static PolicyVersionDto From(PublishedPolicyVersion v) => new(
        v.Tenant, v.Environment, v.PolicyVersion, v.PolicyHash, v.IssuedAtUtc, v.ExpiresAtUtc,
        v.Author, v.Reviewer, v.SupersededVersion, v.RolloutState.ToString(), v.PublishedAtUtc);
}
