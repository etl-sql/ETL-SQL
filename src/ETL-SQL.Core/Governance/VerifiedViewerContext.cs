using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ETL_SQL.Core.Governance;

/// <summary>Resource-local policy for asserted application context.</summary>
public sealed record ViewerContextPolicy(
    IReadOnlyList<string> AllowedClaims,
    int MaximumLifetimeSeconds = 60)
{
    public static readonly IReadOnlySet<string> ReservedClaims = new HashSet<string>(
        ["tenant", "gateway", "resource", "operation", "operation_class", "viewer", "real_viewer",
         "executing_credential", "issued_at", "expires_at", "nonce", "roles", "groups"],
        StringComparer.OrdinalIgnoreCase);

    public void Validate()
    {
        if (MaximumLifetimeSeconds is <= 0 or > 300)
            throw new GatewayProtocolException("Viewer context lifetime must be between 1 and 300 seconds.");
        if (AllowedClaims is null)
            throw new GatewayProtocolException("Viewer context requires an explicit claim allowlist.");
        foreach (var claim in AllowedClaims)
        {
            if (!IsClaimKey(claim) || ReservedClaims.Contains(claim))
                throw new GatewayProtocolException($"Viewer context claim '{claim}' is reserved or malformed.");
        }
    }

    internal static bool IsClaimKey(string key) =>
        !string.IsNullOrWhiteSpace(key)
        && key.Length <= 64
        && key.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
}

/// <summary>
/// Portal assertion carried to a Gateway. It proves what the Portal asserted; it does not prove
/// that PostgreSQL authenticated the viewer.
/// </summary>
public sealed record ViewerContextEnvelope(
    int Version,
    string KeyId,
    string TenantId,
    string GatewayId,
    string GatewayNodeId,
    string ResourceId,
    string OperationId,
    GatewayOperationClass OperationClass,
    string ViewerId,
    string RealViewerId,
    string ExecutingCredentialId,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string Nonce,
    IReadOnlyDictionary<string, string> Claims,
    string Signature)
{
    public const int CurrentVersion = 1;
}

/// <summary>Context accepted by the Gateway after signature, binding, expiry, and replay checks.</summary>
public sealed record VerifiedViewerContext(
    string TenantId,
    string ResourceId,
    string OperationId,
    string ViewerId,
    string RealViewerId,
    string ExecutingCredentialId,
    IReadOnlyDictionary<string, string> Claims,
    DateTimeOffset VerifiedAtUtc);

public interface IViewerContextEnvelopeSigner
{
    ViewerContextEnvelope Sign(
        GatewayOperation operation,
        string viewerId,
        string realViewerId,
        string executingCredentialId,
        IReadOnlyDictionary<string, string> claims,
        ViewerContextPolicy policy);
}

public interface IViewerContextEnvelopeVerifier
{
    VerifiedViewerContext Verify(
        ViewerContextEnvelope envelope,
        GatewayOperation operation,
        GatewayResource resource);
}

/// <summary>Thread-safe replay store. Nonces live until expiry and can persist across restart.</summary>
public sealed class ViewerContextReplayStore
{
    private readonly Dictionary<(string Tenant, string Nonce), DateTimeOffset> _seen = [];
    private readonly Lock _gate = new();
    private readonly string? _persistencePath;

    public ViewerContextReplayStore(string? persistencePath = null)
    {
        if (persistencePath is null) return;
        if (!Path.IsPathFullyQualified(persistencePath))
            throw new ArgumentException("The viewer context replay path must be absolute.", nameof(persistencePath));
        _persistencePath = Path.GetFullPath(persistencePath);
        Directory.CreateDirectory(Path.GetDirectoryName(_persistencePath)!);
        if (!File.Exists(_persistencePath)) return;
        try
        {
            var records = JsonSerializer.Deserialize<List<ReplayRecord>>(File.ReadAllText(_persistencePath)) ?? [];
            foreach (var record in records)
            {
                if (string.IsNullOrWhiteSpace(record.TenantId) || string.IsNullOrWhiteSpace(record.Nonce))
                    throw new JsonException("A replay record is malformed.");
                _seen[(record.TenantId, record.Nonce)] = record.ExpiresAtUtc;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new GatewayProtocolException(
                "The viewer context replay store could not be loaded; context-enabled operations are refused.");
        }
    }

    public bool TryConsume(string tenantId, string nonce, DateTimeOffset expiresAtUtc, DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            foreach (var expired in _seen.Where(item => item.Value <= nowUtc).Select(item => item.Key).ToList())
                _seen.Remove(expired);
            if (!_seen.TryAdd((tenantId, nonce), expiresAtUtc)) return false;
            PersistLocked();
            return true;
        }
    }

    private void PersistLocked()
    {
        if (_persistencePath is null) return;
        var records = _seen.Select(item => new ReplayRecord(item.Key.Tenant, item.Key.Nonce, item.Value));
        var temporary = _persistencePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(records));
            File.Move(temporary, _persistencePath, overwrite: true);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(_persistencePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporary); } catch { }
            throw new GatewayProtocolException(
                "The viewer context replay store could not record the nonce; execution is refused.");
        }
    }

    private sealed record ReplayRecord(string TenantId, string Nonce, DateTimeOffset ExpiresAtUtc);
}

/// <summary>HMAC-SHA-256 implementation used by the Portal and the first Gateway connector.</summary>
public sealed class HmacViewerContextEnvelopeService : IViewerContextEnvelopeSigner, IViewerContextEnvelopeVerifier
{
    private readonly string _keyId;
    private readonly byte[] _key;
    private readonly ViewerContextReplayStore _replay;
    private readonly TimeProvider _timeProvider;

    public HmacViewerContextEnvelopeService(
        string keyId,
        byte[] key,
        ViewerContextReplayStore? replay = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length < 32)
            throw new ArgumentException("Viewer context signing keys must contain at least 256 bits.", nameof(key));
        _keyId = keyId;
        _key = key.ToArray();
        _replay = replay ?? new ViewerContextReplayStore();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ViewerContextEnvelope Sign(
        GatewayOperation operation,
        string viewerId,
        string realViewerId,
        string executingCredentialId,
        IReadOnlyDictionary<string, string> claims,
        ViewerContextPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(policy);
        operation.Validate();
        policy.Validate();
        ValidateIdentity(viewerId, "viewer");
        ValidateIdentity(realViewerId, "real viewer");
        ValidateIdentity(executingCredentialId, "executing credential");
        ValidateIdentity(operation.GatewayNodeId ?? string.Empty, "Gateway node");
        ValidateClaims(claims, policy);

        var issued = _timeProvider.GetUtcNow();
        var unsigned = new ViewerContextEnvelope(
            ViewerContextEnvelope.CurrentVersion, _keyId, operation.TenantId, operation.GatewayId,
            operation.GatewayNodeId!,
            operation.ResourceId, operation.OperationId, operation.Class, viewerId, realViewerId,
            executingCredentialId, issued, issued.AddSeconds(policy.MaximumLifetimeSeconds),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            new Dictionary<string, string>(claims, StringComparer.OrdinalIgnoreCase), string.Empty);
        return unsigned with { Signature = ComputeSignature(unsigned) };
    }

    public VerifiedViewerContext Verify(
        ViewerContextEnvelope envelope,
        GatewayOperation operation,
        GatewayResource resource)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(resource);
        var policy = resource.ViewerContextPolicy
            ?? throw new GatewayProtocolException("The resource is not configured to accept viewer context.");
        policy.Validate();

        if (string.IsNullOrWhiteSpace(envelope.Signature) || envelope.Claims is null)
            throw new GatewayProtocolException("The viewer context signature or claim map is missing.");
        if (envelope.Version != ViewerContextEnvelope.CurrentVersion || envelope.KeyId != _keyId)
            throw new GatewayProtocolException("The viewer context signature version or key is not accepted.");
        if (!FixedEquals(envelope.Signature, ComputeSignature(envelope with { Signature = string.Empty })))
            throw new GatewayProtocolException("The viewer context signature is invalid.");
        if (!string.Equals(envelope.TenantId, operation.TenantId, StringComparison.Ordinal)
            || !string.Equals(envelope.GatewayId, operation.GatewayId, StringComparison.Ordinal)
            || !string.Equals(envelope.GatewayNodeId, operation.GatewayNodeId, StringComparison.Ordinal)
            || !string.Equals(envelope.ResourceId, operation.ResourceId, StringComparison.Ordinal)
            || !string.Equals(envelope.OperationId, operation.OperationId, StringComparison.Ordinal)
            || envelope.OperationClass != operation.Class)
            throw new GatewayProtocolException("The viewer context is bound to a different operation, tenant, Gateway, or resource.");
        if (!string.Equals(envelope.ExecutingCredentialId, resource.ExecutingCredentialId, StringComparison.Ordinal))
            throw new GatewayProtocolException("The viewer context is bound to a different executing credential.");

        var now = _timeProvider.GetUtcNow();
        if (envelope.IssuedAtUtc > now.AddSeconds(5)
            || envelope.ExpiresAtUtc <= now
            || envelope.ExpiresAtUtc - envelope.IssuedAtUtc > TimeSpan.FromSeconds(policy.MaximumLifetimeSeconds))
            throw new GatewayProtocolException("The viewer context is expired or has an invalid lifetime.");
        ValidateIdentity(envelope.ViewerId, "viewer");
        ValidateIdentity(envelope.RealViewerId, "real viewer");
        ValidateClaims(envelope.Claims, policy);
        if (string.IsNullOrWhiteSpace(envelope.Nonce)
            || !_replay.TryConsume(envelope.TenantId, envelope.Nonce, envelope.ExpiresAtUtc, now))
            throw new GatewayProtocolException("The viewer context nonce has already been used.");

        return new VerifiedViewerContext(
            envelope.TenantId, envelope.ResourceId, envelope.OperationId, envelope.ViewerId,
            envelope.RealViewerId, envelope.ExecutingCredentialId,
            new Dictionary<string, string>(envelope.Claims, StringComparer.OrdinalIgnoreCase), now);
    }

    private string ComputeSignature(ViewerContextEnvelope envelope)
    {
        using var hmac = new HMACSHA256(_key);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(Canonicalize(envelope))));
    }

    private static string Canonicalize(ViewerContextEnvelope envelope)
    {
        var claims = envelope.Claims.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new KeyValuePair<string, string>(item.Key, item.Value));
        return JsonSerializer.Serialize(new
        {
            envelope.Version,
            envelope.KeyId,
            envelope.TenantId,
            envelope.GatewayId,
            envelope.GatewayNodeId,
            envelope.ResourceId,
            envelope.OperationId,
            operationClass = (int)envelope.OperationClass,
            envelope.ViewerId,
            envelope.RealViewerId,
            envelope.ExecutingCredentialId,
            issuedAt = envelope.IssuedAtUtc.ToUniversalTime().ToString("O"),
            expiresAt = envelope.ExpiresAtUtc.ToUniversalTime().ToString("O"),
            envelope.Nonce,
            claims
        });
    }

    private static bool FixedEquals(string supplied, string expected)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(supplied), Convert.FromBase64String(expected));
        }
        catch (FormatException) { return false; }
    }

    private static void ValidateIdentity(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl))
            throw new GatewayProtocolException($"The viewer context {name} is missing or malformed.");
    }

    private static void ValidateClaims(IReadOnlyDictionary<string, string> claims, ViewerContextPolicy policy)
    {
        if (claims is null)
            throw new GatewayProtocolException("Viewer context requires an explicit claim map.");
        var allowed = policy.AllowedClaims.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in claims)
        {
            if (!ViewerContextPolicy.IsClaimKey(key) || ViewerContextPolicy.ReservedClaims.Contains(key))
                throw new GatewayProtocolException($"Viewer context claim '{key}' is reserved or malformed.");
            if (!allowed.Contains(key))
                throw new GatewayProtocolException($"Viewer context claim '{key}' is not allowed for this resource.");
            if (value is null || value.Length > 2048 || value.Any(char.IsControl))
                throw new GatewayProtocolException($"Viewer context claim '{key}' has an invalid value.");
        }
    }
}
