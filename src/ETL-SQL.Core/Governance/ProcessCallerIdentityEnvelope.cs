using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Multitenancy;

namespace ETL_SQL.Core.Governance;

/// <summary>
/// Signed, single-use caller identity envelope written by <c>ProcessJobExecutor</c> to a private
/// temp file and verified/deleted before script execution by the spawned child process.
/// Uses a dedicated issuer and audience so HTTP <see cref="OrchestratorIdentityAssertion"/> tokens
/// cannot be replayed across the child-process boundary.
/// </summary>
public static class ProcessCallerIdentityEnvelope
{
    public const int CurrentVersion = 1;
    public const int MaximumBytes = 65536;
    public const string Issuer = "etl-sql-orchestrator";
    public const string Audience = "etl-sql-orchestrator-child-process";
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan MaxAllowedLifetime = TimeSpan.FromMinutes(10);

    public const string EnvRequired = "ETLSQL_CALLER_IDENTITY_REQUIRED";
    public const string EnvFile = "ETLSQL_CALLER_IDENTITY_FILE";
    public const string EnvJobId = "ETLSQL_CALLER_JOB_ID";

    /// <summary>
    /// Carries the per-run envelope key to a one-shot child. A warm runner gets it in its stdin
    /// request instead, so it never sits in a long-lived process environment.
    /// </summary>
    public const string EnvKey = "ETLSQL_CALLER_IDENTITY_KEY";

    /// <summary>
    /// Environment names of the Orchestrator/Portal identity-signing secrets. A spawned child runs
    /// user script text and must never hold them: anyone who reads one can forge an Orchestrator
    /// assertion for any user and tenant. Children inherit the parent environment, so spawners
    /// remove these explicitly.
    /// </summary>
    public static readonly IReadOnlyList<string> ParentSigningSecretEnvironmentNames =
    [
        "Orchestrator__IdentitySigningSecret",
        "Portal__Orchestrator__IdentitySigningSecret",
    ];

    /// <summary>
    /// Creates a single-run HMAC key (256 random bits, base64url). The parent signs one envelope
    /// with it and hands it to that child only, so the key forges nothing outside that run and no
    /// long-lived secret ever crosses the process boundary.
    /// </summary>
    public static string CreateRunKey() =>
        OrchestratorIdentityAssertion.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string ComputeScriptHash(byte[] scriptBytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(scriptBytes)).ToLowerInvariant();

    public static async Task<string> ComputeScriptHashAsync(string scriptPath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(scriptPath);
        return "sha256:" + Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    public static string Create(
        ExecutionIdentity identity,
        string? jobId,
        string scriptHash,
        string signingSecret,
        TimeSpan? lifetime = null,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        OrchestratorIdentityAssertion.ValidateSecret(signingSecret);
        if (string.IsNullOrWhiteSpace(identity.EffectiveUser) || string.IsNullOrWhiteSpace(identity.RealUser))
            throw new ArgumentException("Execution identity must specify EffectiveUser and RealUser.", nameof(identity));
        if (string.IsNullOrWhiteSpace(scriptHash))
            throw new ArgumentException("Script hash is required.", nameof(scriptHash));

        var effectiveLifetime = lifetime ?? DefaultLifetime;
        if (effectiveLifetime <= TimeSpan.Zero || effectiveLifetime > MaxAllowedLifetime)
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Envelope lifetime must be positive and within allowed bounds.");

        var normalizedTenant = string.IsNullOrWhiteSpace(identity.TenantId)
            ? null
            : TenantId.FromTrustedSource(identity.TenantId).Value;
        var issuedAt = now ?? DateTimeOffset.UtcNow;
        var payload = new EnvelopePayload(
            CurrentVersion,
            Issuer,
            Audience,
            Guid.NewGuid().ToString("N"),
            issuedAt.ToUnixTimeSeconds(),
            issuedAt.Add(effectiveLifetime).ToUnixTimeSeconds(),
            string.IsNullOrWhiteSpace(jobId) ? null : jobId.Trim(),
            scriptHash,
            normalizedTenant is null ? identity with { TenantId = null } : identity with { TenantId = normalizedTenant });

        var encodedPayload = OrchestratorIdentityAssertion.Base64UrlEncode(
            JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
        var signature = OrchestratorIdentityAssertion.Sign(encodedPayload, signingSecret);
        return encodedPayload + "." + OrchestratorIdentityAssertion.Base64UrlEncode(signature);
    }

    public static ExecutionIdentity ValidateAndConsume(
        string? signedEnvelope,
        string signingSecret,
        string scriptHash,
        string? expectedJobId = null,
        DateTimeOffset? now = null,
        ProcessCallerNonceReplayGuard? replayGuard = null)
    {
        if (string.IsNullOrWhiteSpace(signedEnvelope))
            throw new UnauthorizedAccessException("Required caller identity envelope is missing or empty.");
        if (string.IsNullOrWhiteSpace(signingSecret))
            throw new UnauthorizedAccessException("The caller identity envelope key was not supplied to the child process.");

        try
        {
            OrchestratorIdentityAssertion.ValidateSecret(signingSecret);
        }
        catch (ArgumentException ex)
        {
            throw new UnauthorizedAccessException(ex.Message, ex);
        }

        var separator = signedEnvelope.IndexOf('.');
        if (separator <= 0 || separator != signedEnvelope.LastIndexOf('.'))
            throw new UnauthorizedAccessException("Caller identity envelope is malformed.");

        var encodedPayload = signedEnvelope[..separator];
        byte[] suppliedSignature;
        byte[] payloadBytes;
        try
        {
            suppliedSignature = OrchestratorIdentityAssertion.Base64UrlDecode(signedEnvelope[(separator + 1)..]);
            payloadBytes = OrchestratorIdentityAssertion.Base64UrlDecode(encodedPayload);
        }
        catch (FormatException ex)
        {
            throw new UnauthorizedAccessException("Caller identity envelope encoding is invalid.", ex);
        }

        var expectedSignature = OrchestratorIdentityAssertion.Sign(encodedPayload, signingSecret);
        if (suppliedSignature.Length != expectedSignature.Length
            || !CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature))
            throw new UnauthorizedAccessException("Caller identity envelope signature is invalid.");

        EnvelopePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<EnvelopePayload>(payloadBytes, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new UnauthorizedAccessException("Caller identity envelope payload is invalid.", ex);
        }

        if (payload is null
            || payload.Version != CurrentVersion
            || !string.Equals(payload.Issuer, Issuer, StringComparison.Ordinal)
            || !string.Equals(payload.Audience, Audience, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(payload.Nonce)
            || payload.Identity is null
            || string.IsNullOrWhiteSpace(payload.Identity.EffectiveUser)
            || string.IsNullOrWhiteSpace(payload.Identity.RealUser))
        {
            throw new UnauthorizedAccessException("Caller identity envelope claims are invalid.");
        }

        var observedAt = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        if (payload.IssuedAt > observedAt + 30
            || payload.ExpiresAt < observedAt
            || payload.ExpiresAt - payload.IssuedAt <= 0
            || payload.ExpiresAt - payload.IssuedAt > (long)MaxAllowedLifetime.TotalSeconds)
        {
            throw new UnauthorizedAccessException("Caller identity envelope is expired or outside its allowed lifetime.");
        }

        if (string.IsNullOrWhiteSpace(scriptHash)
            || !string.Equals(payload.ScriptHash, scriptHash, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Caller identity envelope does not match the target script.");
        }

        if (!string.IsNullOrWhiteSpace(expectedJobId)
            && !string.Equals(payload.JobId, expectedJobId.Trim(), StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Caller identity envelope does not match the scheduled job.");
        }

        var identity = payload.Identity;
        if (!string.IsNullOrWhiteSpace(identity.TenantId))
            identity = identity with { TenantId = TenantId.FromTrustedSource(identity.TenantId).Value };

        var guard = replayGuard ?? ProcessCallerNonceReplayGuard.Shared;
        if (!guard.TryConsume(payload.Nonce, payload.ExpiresAt, observedAt))
            throw new UnauthorizedAccessException("Caller identity envelope has already been consumed.");

        return identity;
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record EnvelopePayload(
        int Version,
        string Issuer,
        string Audience,
        string Nonce,
        long IssuedAt,
        long ExpiresAt,
        string? JobId,
        string ScriptHash,
        ExecutionIdentity Identity);
}

public sealed class ProcessCallerNonceReplayGuard
{
    public static ProcessCallerNonceReplayGuard Shared { get; } = new();

    private readonly ConcurrentDictionary<string, long> _seen = new(StringComparer.Ordinal);

    public bool TryConsume(string nonce, long expiresAtUnixSeconds, long nowUnixSeconds)
    {
        if (string.IsNullOrWhiteSpace(nonce))
            return false;

        foreach (var entry in _seen)
        {
            if (entry.Value < nowUnixSeconds)
                _seen.TryRemove(entry.Key, out _);
        }

        return _seen.TryAdd(nonce, expiresAtUnixSeconds);
    }
}
