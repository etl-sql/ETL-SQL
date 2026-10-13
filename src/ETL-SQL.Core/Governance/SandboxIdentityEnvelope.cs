using System;
using System.Text.Json.Serialization;

namespace ETL_SQL.Core.Governance;

/// <summary>Assignment-bound identity supplied by the trusted sandbox host, never by script state.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SandboxIdentityEnvelope(
    int Version,
    string TenantId,
    string RunId,
    string AttemptId,
    string ArtifactHash,
    ExecutionIdentity Identity)
{
    public const int CurrentVersion = 1;
    public const int MaximumBytes = 65536;
    public const string WorkerDirectory = "/run/etl-sql/identity";
    public const string WorkerFile = WorkerDirectory + "/request.json";

    public ExecutionIdentity Validate(string tenantId, string runId, string attemptId, string artifactHash)
    {
        if (Version != CurrentVersion || Identity is null
            || !string.Equals(TenantId, tenantId, StringComparison.Ordinal)
            || !string.Equals(Identity.TenantId, tenantId, StringComparison.Ordinal)
            || !string.Equals(RunId, runId, StringComparison.Ordinal)
            || !string.Equals(AttemptId, attemptId, StringComparison.Ordinal)
            || !string.Equals(ArtifactHash, artifactHash, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(runId)
            || string.IsNullOrWhiteSpace(attemptId) || string.IsNullOrWhiteSpace(artifactHash)
            || string.IsNullOrWhiteSpace(Identity.EffectiveUser) || string.IsNullOrWhiteSpace(Identity.RealUser))
            throw new UnauthorizedAccessException("Sandbox execution identity does not match its trusted assignment.");
        return Identity;
    }
}
