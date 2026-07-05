using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace ETL_SQL.Core.Governance;

public enum PolicyRolloutState
{
    Staged,
    Active,
    Superseded,
    RolledBack
}

/// <summary>
/// An immutable published organization-policy version held by the authority. It records the signed
/// envelope plus provenance (author, reviewer, hash, superseded version, rollout state); it never
/// holds signing-key material.
/// </summary>
public sealed record PublishedPolicyVersion(
    string Tenant,
    string Environment,
    string PolicyVersion,
    string PolicyHash,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string Author,
    string? Reviewer,
    string? SupersededVersion,
    PolicyRolloutState RolloutState,
    string SignedEnvelopeJson,
    DateTimeOffset PublishedAtUtc);

/// <summary>
/// Signs policy envelopes using a key held by reference in an external key store (OS store / HSM).
/// The authority never receives, persists, or exports the private key — only this abstraction can
/// produce a signature, and it exposes the public key for machine enrollment distribution.
/// </summary>
public interface IPolicyEnvelopeSigner
{
    string PublicKeyPem { get; }
    string Sign(SignedOrganizationPolicyEnvelope envelope);
}

/// <summary>In-memory RSA signer for tests and single-node authorities. Production wraps a cert
/// referenced by thumbprint from the OS/HSM store rather than an exportable key.</summary>
public sealed class RsaPolicyEnvelopeSigner : IPolicyEnvelopeSigner, IDisposable
{
    private readonly RSA _key;
    public RsaPolicyEnvelopeSigner(RSA key) => _key = key;
    public string PublicKeyPem => _key.ExportSubjectPublicKeyInfoPem();
    public string Sign(SignedOrganizationPolicyEnvelope envelope) =>
        EnterprisePolicySignature.Sign(envelope, _key);
    public void Dispose() => _key.Dispose();
}

/// <summary>
/// Signs with the private key of a certificate referenced by thumbprint in the OS certificate store
/// (LocalMachine then CurrentUser). The private key never leaves the store — the authority holds only
/// this reference, satisfying "external certificate/key-store reference; no exportable private key".
/// </summary>
public sealed class CertificatePolicyEnvelopeSigner : IPolicyEnvelopeSigner, IDisposable
{
    private readonly System.Security.Cryptography.X509Certificates.X509Certificate2 _cert;
    private readonly RSA _privateKey;

    public CertificatePolicyEnvelopeSigner(string thumbprint) : this(FindCertificate(thumbprint)) { }

    internal CertificatePolicyEnvelopeSigner(
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate)
    {
        _cert = certificate;
        _privateKey = _cert.GetRSAPrivateKey()
            ?? throw new PolicyAuthorityException("Policy signing certificate has no accessible RSA private key.");
    }

    public string PublicKeyPem => _privateKey.ExportSubjectPublicKeyInfoPem();
    public string Sign(SignedOrganizationPolicyEnvelope envelope) =>
        EnterprisePolicySignature.Sign(envelope, _privateKey);

    private static System.Security.Cryptography.X509Certificates.X509Certificate2 FindCertificate(string thumbprint)
    {
        var normalized = thumbprint.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        foreach (var location in new[]
        {
            System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine,
            System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser
        })
        {
            using var store = new System.Security.Cryptography.X509Certificates.X509Store(
                System.Security.Cryptography.X509Certificates.StoreName.My, location);
            store.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly);
            foreach (var cert in store.Certificates)
            {
                if (cert.HasPrivateKey && string.Equals(cert.Thumbprint, normalized, StringComparison.OrdinalIgnoreCase))
                    return cert;
                cert.Dispose();
            }
        }
        throw new PolicyAuthorityException(
            $"Policy signing certificate '{normalized}' was not found with an accessible private key.");
    }

    public void Dispose() { _privateKey.Dispose(); _cert.Dispose(); }
}

/// <summary>Placeholder signer used when the authority is not configured — every operation fails with
/// a clear message so the admin API returns a deterministic "not configured" result.</summary>
public sealed class DisabledPolicyEnvelopeSigner : IPolicyEnvelopeSigner
{
    public const string Reason =
        "Policy authority signing is not configured (set Portal:PolicyAuthority:SigningCertThumbprint).";
    public string PublicKeyPem => throw new PolicyAuthorityException(Reason);
    public string Sign(SignedOrganizationPolicyEnvelope envelope) => throw new PolicyAuthorityException(Reason);
}

/// <summary>Persistence for published policy versions. Implementations must preserve every published
/// version immutably and expose the single active version per tenant/environment.</summary>
public interface IPolicyAuthorityStore
{
    Task<PublishedPolicyVersion?> GetActiveAsync(string tenant, string environment, CancellationToken ct = default);
    Task<IReadOnlyList<PublishedPolicyVersion>> ListAsync(string tenant, string environment, CancellationToken ct = default);
    Task AppendAsync(PublishedPolicyVersion version, CancellationToken ct = default);
    Task SetRolloutStateAsync(string tenant, string environment, string policyVersion, PolicyRolloutState state, CancellationToken ct = default);
}

public sealed class PolicyAuthorityException(string message) : Exception(message);

/// <summary>
/// Validates, versions, signs, publishes, supersedes, and retrieves organization policies per
/// tenant/environment. Enforces monotonic issuance so a client that rejects older issuance times
/// always accepts a newer published version — the basis for staged rollout and emergency rollback.
/// </summary>
public sealed class PolicyAuthorityService(
    IPolicyAuthorityStore store,
    IPolicyEnvelopeSigner signer,
    Func<DateTimeOffset>? clock = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public OrganizationPolicyValidationResult Validate(OrganizationPolicyDocument document) =>
        OrganizationPolicySchema.Validate(document);

    public async Task<PublishedPolicyVersion> PublishAsync(
        OrganizationPolicyDocument document,
        string tenant,
        string environment,
        string policyVersion,
        string author,
        string? reviewer,
        DateTimeOffset expiresAtUtc,
        bool staged = false,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(environment);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(author);

        var validation = OrganizationPolicySchema.Validate(document);
        if (!validation.IsValid)
            throw new PolicyAuthorityException("Invalid policy document: " + string.Join("; ", validation.Errors));

        var now = _clock();
        if (expiresAtUtc <= now)
            throw new PolicyAuthorityException("Policy expiry must be in the future.");

        var existing = await store.ListAsync(tenant, environment, ct).ConfigureAwait(false);
        if (existing.Any(v => string.Equals(v.PolicyVersion, policyVersion, StringComparison.Ordinal)))
            throw new PolicyAuthorityException($"Policy version '{policyVersion}' already exists for {tenant}/{environment}.");

        var active = existing.FirstOrDefault(v => v.RolloutState == PolicyRolloutState.Active);
        // Monotonic issuance: a superseding version must issue strictly later than the current active
        // one, so a client that rejects older issuance times always accepts the newer version.
        var issuedAt = active is null ? now : Later(now, active.IssuedAtUtc.AddMilliseconds(1));

        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(OrganizationPolicySchema.Serialize(document)));
        var envelope = new SignedOrganizationPolicyEnvelope
        {
            Tenant = tenant,
            PolicyVersion = policyVersion,
            IssuedAtUtc = issuedAt,
            ExpiresAtUtc = expiresAtUtc,
            PolicyPayload = payload,
            Signature = "" // set below
        };
        envelope = envelope with { Signature = signer.Sign(envelope) };

        var version = new PublishedPolicyVersion(
            tenant, environment, policyVersion,
            ComputeHash(document), issuedAt, expiresAtUtc, author, reviewer,
            active?.PolicyVersion,
            staged ? PolicyRolloutState.Staged : PolicyRolloutState.Active,
            JsonSerializer.Serialize(envelope, JsonOptions), now);

        await store.AppendAsync(version, ct).ConfigureAwait(false);
        if (!staged && active is not null)
            await store.SetRolloutStateAsync(tenant, environment, active.PolicyVersion,
                PolicyRolloutState.Superseded, ct).ConfigureAwait(false);
        return version;
    }

    /// <summary>
    /// Promotes a staged version to active, superseding the current active version. The staged
    /// envelope keeps its original signature and issuance time, so promotion is only legal while the
    /// staged version still issues later than the active one — clients reject older issuance. A
    /// staged version that has been overtaken by a newer publish must be republished instead.
    /// </summary>
    public async Task<PublishedPolicyVersion> ActivateStagedAsync(
        string tenant, string environment, string policyVersion, CancellationToken ct = default)
    {
        var versions = await store.ListAsync(tenant, environment, ct).ConfigureAwait(false);
        var staged = versions.FirstOrDefault(v =>
            string.Equals(v.PolicyVersion, policyVersion, StringComparison.Ordinal))
            ?? throw new PolicyAuthorityException(
                $"Policy version '{policyVersion}' was not found for {tenant}/{environment}.");
        if (staged.RolloutState != PolicyRolloutState.Staged)
            throw new PolicyAuthorityException(
                $"Policy version '{policyVersion}' is {staged.RolloutState}; only a staged version can be activated.");

        var active = versions.FirstOrDefault(v => v.RolloutState == PolicyRolloutState.Active);
        if (active is not null && staged.IssuedAtUtc <= active.IssuedAtUtc)
            throw new PolicyAuthorityException(
                $"Staged version '{policyVersion}' was issued before the current active version " +
                $"'{active.PolicyVersion}' and would be rejected by clients; republish the document as a new version.");

        // Activate first: if interrupted between the two writes, the store briefly holds two active
        // versions and GetActiveAsync deterministically serves the newer one — safer than a window
        // with no active policy at all.
        await store.SetRolloutStateAsync(tenant, environment, policyVersion,
            PolicyRolloutState.Active, ct).ConfigureAwait(false);
        if (active is not null)
            await store.SetRolloutStateAsync(tenant, environment, active.PolicyVersion,
                PolicyRolloutState.Superseded, ct).ConfigureAwait(false);
        return staged with { RolloutState = PolicyRolloutState.Active };
    }

    public Task<IReadOnlyList<PublishedPolicyVersion>> ListVersionsAsync(
        string tenant, string environment, CancellationToken ct = default) =>
        store.ListAsync(tenant, environment, ct);

    public Task<PublishedPolicyVersion?> GetActiveVersionAsync(
        string tenant, string environment, CancellationToken ct = default) =>
        store.GetActiveAsync(tenant, environment, ct);

    /// <summary>Returns the active signed envelope for a tenant/environment, for a machine to retrieve.</summary>
    public async Task<SignedOrganizationPolicyEnvelope?> RetrieveActiveEnvelopeAsync(
        string tenant, string environment, CancellationToken ct = default)
    {
        var active = await store.GetActiveAsync(tenant, environment, ct).ConfigureAwait(false);
        return active is null
            ? null
            : JsonSerializer.Deserialize<SignedOrganizationPolicyEnvelope>(active.SignedEnvelopeJson, JsonOptions);
    }

    /// <summary>
    /// Emergency rollback: republish a prior version's document as a new active version with a fresh
    /// (later) issuance time. Clients reject older issuance, so rollback is a forward publish, never a
    /// silent revert to an older envelope.
    /// </summary>
    public async Task<PublishedPolicyVersion> RollbackToAsync(
        string tenant, string environment, string targetPolicyVersion, string newPolicyVersion,
        string author, string? reviewer, DateTimeOffset expiresAtUtc, CancellationToken ct = default)
    {
        var versions = await store.ListAsync(tenant, environment, ct).ConfigureAwait(false);
        var target = versions.FirstOrDefault(v =>
            string.Equals(v.PolicyVersion, targetPolicyVersion, StringComparison.Ordinal))
            ?? throw new PolicyAuthorityException($"Rollback target '{targetPolicyVersion}' not found.");

        var envelope = JsonSerializer.Deserialize<SignedOrganizationPolicyEnvelope>(target.SignedEnvelopeJson, JsonOptions)
            ?? throw new PolicyAuthorityException("Rollback target envelope could not be read.");
        var document = OrganizationPolicySchema.ParseJson(
            Encoding.UTF8.GetString(Convert.FromBase64String(envelope.PolicyPayload)));

        var abandoned = versions.FirstOrDefault(v => v.RolloutState == PolicyRolloutState.Active);
        var republished = await PublishAsync(document, tenant, environment, newPolicyVersion,
            author, reviewer, expiresAtUtc, staged: false, ct).ConfigureAwait(false);
        // The publish marked the abandoned version Superseded; record it as RolledBack so the
        // durable history distinguishes an emergency rollback from a routine supersession.
        if (abandoned is not null)
            await store.SetRolloutStateAsync(tenant, environment, abandoned.PolicyVersion,
                PolicyRolloutState.RolledBack, ct).ConfigureAwait(false);
        return republished with { RolloutState = PolicyRolloutState.Active };
    }

    private static DateTimeOffset Later(DateTimeOffset a, DateTimeOffset b) => a >= b ? a : b;

    private static string ComputeHash(OrganizationPolicyDocument document) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(document)))).ToLowerInvariant();
}

/// <summary>In-memory authority store for tests and single-node/standalone authorities.</summary>
public sealed class InMemoryPolicyAuthorityStore : IPolicyAuthorityStore
{
    private readonly ConcurrentDictionary<string, List<PublishedPolicyVersion>> _versions = new();

    private static string Key(string tenant, string environment) => $"{tenant} {environment}";

    public Task<PublishedPolicyVersion?> GetActiveAsync(string tenant, string environment, CancellationToken ct = default)
    {
        var list = _versions.GetValueOrDefault(Key(tenant, environment));
        lock (Gate(tenant, environment))
            return Task.FromResult(list?.LastOrDefault(v => v.RolloutState == PolicyRolloutState.Active));
    }

    public Task<IReadOnlyList<PublishedPolicyVersion>> ListAsync(string tenant, string environment, CancellationToken ct = default)
    {
        lock (Gate(tenant, environment))
        {
            var list = _versions.GetValueOrDefault(Key(tenant, environment));
            IReadOnlyList<PublishedPolicyVersion> result = list is null ? [] : list.ToArray();
            return Task.FromResult(result);
        }
    }

    public Task AppendAsync(PublishedPolicyVersion version, CancellationToken ct = default)
    {
        lock (Gate(version.Tenant, version.Environment))
            _versions.GetOrAdd(Key(version.Tenant, version.Environment), _ => new()).Add(version);
        return Task.CompletedTask;
    }

    public Task SetRolloutStateAsync(string tenant, string environment, string policyVersion, PolicyRolloutState state, CancellationToken ct = default)
    {
        lock (Gate(tenant, environment))
        {
            var list = _versions.GetValueOrDefault(Key(tenant, environment));
            if (list is null) return Task.CompletedTask;
            for (var i = 0; i < list.Count; i++)
                if (string.Equals(list[i].PolicyVersion, policyVersion, StringComparison.Ordinal))
                    list[i] = list[i] with { RolloutState = state };
        }
        return Task.CompletedTask;
    }

    private readonly ConcurrentDictionary<string, object> _gates = new();
    private object Gate(string tenant, string environment) => _gates.GetOrAdd(Key(tenant, environment), _ => new object());
}
