using System.Security.Cryptography;
using System.Text;

namespace ETL_SQL.Core.Governance;

/// <summary>Lifecycle state of a Gateway enrollment.</summary>
public enum GatewayEnrollmentState
{
    /// <summary>Issued by a tenant administrator and not yet consumed.</summary>
    Pending,

    /// <summary>Consumed exactly once by an on-premises administrator; a workload identity now exists.</summary>
    Consumed,

    /// <summary>Withdrawn before consumption, or the Gateway was later revoked.</summary>
    Revoked
}

/// <summary>
/// A one-time Gateway enrollment (SaaS isolation architecture §11.3 steps 1–2). A tenant
/// administrator issues it in the Portal; an on-premises administrator consumes it exactly once
/// while installing the Gateway, presenting a public key that becomes the Gateway's workload
/// identity.
///
/// <para>The secret half of the enrollment never lives here. Only <see cref="TokenHash"/> — a
/// SHA-256 of the one-time token — is stored, so a reader of the enrollment store cannot enrol a
/// Gateway. The same reasoning as the binding model: possession of the record must not confer the
/// authority it describes.</para>
/// </summary>
public sealed record GatewayEnrollment(
    string EnrollmentId,
    string TenantId,
    string GatewayId,
    string TokenHash,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ExpiresUtc,
    GatewayEnrollmentState State = GatewayEnrollmentState.Pending,
    DateTimeOffset? ConsumedUtc = null,
    string? WorkloadPublicKeyThumbprint = null);

/// <summary>Thrown when an enrollment cannot be consumed. The message never repeats the token.</summary>
public sealed class GatewayEnrollmentException(string message) : Exception(message);

/// <summary>
/// Issues and consumes Gateway enrollments. Implementations must make consumption atomic: two
/// concurrent installers presenting the same token must not both end up with a workload identity.
/// </summary>
public interface IGatewayEnrollmentStore
{
    Task<GatewayEnrollment> IssueAsync(
        string tenantId, string gatewayId, string oneTimeToken, DateTimeOffset expiresUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes an enrollment, binding the presented workload public key to the Gateway. Throws
    /// <see cref="GatewayEnrollmentException"/> when the token is unknown, already consumed,
    /// revoked, expired, or presented for a different tenant.
    /// </summary>
    Task<GatewayEnrollment> ConsumeAsync(
        string tenantId, string oneTimeToken, string workloadPublicKeyThumbprint,
        CancellationToken cancellationToken = default);

    Task RevokeAsync(string tenantId, string gatewayId, CancellationToken cancellationToken = default);

    Task<GatewayEnrollment?> FindByGatewayAsync(
        string tenantId, string gatewayId, CancellationToken cancellationToken = default);
}

/// <summary>Hashing for one-time enrollment tokens. Separate so every store agrees on the form.</summary>
public static class GatewayEnrollmentToken
{
    /// <summary>Minimum entropy for a one-time token, in characters of a base64url-ish alphabet.</summary>
    public const int MinimumTokenLength = 32;

    public static string Hash(string oneTimeToken)
    {
        if (string.IsNullOrWhiteSpace(oneTimeToken))
            throw new ArgumentException("A one-time enrollment token is required.", nameof(oneTimeToken));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(oneTimeToken)));
    }

    /// <summary>
    /// Constant-time comparison so a caller cannot narrow a token by timing repeated attempts.
    /// </summary>
    public static bool Matches(string storedHash, string presentedToken) =>
        CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(storedHash),
            Convert.FromHexString(Hash(presentedToken)));

    public static void ValidateStrength(string oneTimeToken)
    {
        if (string.IsNullOrWhiteSpace(oneTimeToken) || oneTimeToken.Trim().Length < MinimumTokenLength)
            throw new ArgumentException(
                $"A Gateway enrollment token must be at least {MinimumTokenLength} characters.",
                nameof(oneTimeToken));
    }
}

/// <summary>
/// In-memory enrollment store. This is the reference implementation the contract tests run against;
/// a durable Portal-backed store implements the same interface and must satisfy the same tests.
/// </summary>
public sealed class InMemoryGatewayEnrollmentStore(TimeProvider? timeProvider = null) : IGatewayEnrollmentStore
{
    private readonly Dictionary<string, GatewayEnrollment> _enrollments = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public Task<GatewayEnrollment> IssueAsync(
        string tenantId, string gatewayId, string oneTimeToken, DateTimeOffset expiresUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayId);
        GatewayEnrollmentToken.ValidateStrength(oneTimeToken);

        var enrollment = new GatewayEnrollment(
            EnrollmentId: Guid.NewGuid().ToString("N"),
            TenantId: tenantId,
            GatewayId: gatewayId,
            TokenHash: GatewayEnrollmentToken.Hash(oneTimeToken),
            CreatedUtc: _time.GetUtcNow(),
            ExpiresUtc: expiresUtc);

        lock (_gate)
        {
            _enrollments[enrollment.EnrollmentId] = enrollment;
        }

        return Task.FromResult(enrollment);
    }

    public Task<GatewayEnrollment> ConsumeAsync(
        string tenantId, string oneTimeToken, string workloadPublicKeyThumbprint,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workloadPublicKeyThumbprint);
        if (string.IsNullOrWhiteSpace(oneTimeToken))
            throw new GatewayEnrollmentException("The enrollment token is not valid.");

        lock (_gate)
        {
            // Tenant is part of the lookup, not a check after it: a token presented under the wrong
            // tenant must be indistinguishable from an unknown token.
            var match = _enrollments.Values.FirstOrDefault(candidate =>
                string.Equals(candidate.TenantId, tenantId, StringComparison.Ordinal)
                && GatewayEnrollmentToken.Matches(candidate.TokenHash, oneTimeToken));

            if (match is null)
                throw new GatewayEnrollmentException("The enrollment token is not valid.");

            // Every refusal below says the same thing on purpose. Distinguishing "already consumed"
            // from "expired" from "revoked" would tell a caller holding a stolen token which
            // enrollments are worth attacking.
            if (match.State != GatewayEnrollmentState.Pending)
                throw new GatewayEnrollmentException("The enrollment token is not valid.");
            if (_time.GetUtcNow() >= match.ExpiresUtc)
                throw new GatewayEnrollmentException("The enrollment token is not valid.");

            var consumed = match with
            {
                State = GatewayEnrollmentState.Consumed,
                ConsumedUtc = _time.GetUtcNow(),
                WorkloadPublicKeyThumbprint = workloadPublicKeyThumbprint
            };
            _enrollments[consumed.EnrollmentId] = consumed;
            return Task.FromResult(consumed);
        }
    }

    public Task RevokeAsync(string tenantId, string gatewayId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            foreach (var (id, enrollment) in _enrollments.ToArray())
            {
                if (string.Equals(enrollment.TenantId, tenantId, StringComparison.Ordinal)
                    && string.Equals(enrollment.GatewayId, gatewayId, StringComparison.Ordinal))
                {
                    _enrollments[id] = enrollment with { State = GatewayEnrollmentState.Revoked };
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task<GatewayEnrollment?> FindByGatewayAsync(
        string tenantId, string gatewayId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_enrollments.Values.FirstOrDefault(enrollment =>
                string.Equals(enrollment.TenantId, tenantId, StringComparison.Ordinal)
                && string.Equals(enrollment.GatewayId, gatewayId, StringComparison.Ordinal)));
        }
    }
}
