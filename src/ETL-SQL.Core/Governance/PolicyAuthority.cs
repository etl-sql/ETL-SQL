using System.Buffers.Binary;
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
    RolledBack,

    /// <summary>A canary version: served only to machines in its <see cref="CanaryCohort"/> while the
    /// rest of the tenant/environment keeps running the <see cref="Active"/> version. Promotion turns
    /// it into the fleet-wide active version; halting rolls it back and reverts its machines.</summary>
    Canary
}

/// <summary>
/// Targets a canary policy version at a subset of a tenant/environment's enrolled machines: either a
/// named machine <see cref="Group"/> or a <see cref="Percentage"/> of the fleet chosen by a stable,
/// deterministic hash of the machine identity. Exactly one selector is set. Percentage membership is
/// stable across polls and monotonic as the percentage ramps up — a machine that is in at N% stays in
/// at any M% ≥ N — so a canary machine never flaps back to the fleet-wide version on its own.
/// </summary>
public sealed record CanaryCohort
{
    public string? Group { get; init; }
    public int? Percentage { get; init; }

    public static CanaryCohort ForGroup(string group)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        return new CanaryCohort { Group = group.Trim() };
    }

    public static CanaryCohort ForPercentage(int percentage)
    {
        if (percentage is < 1 or > 100)
            throw new ArgumentOutOfRangeException(
                nameof(percentage), percentage, "Canary percentage must be between 1 and 100.");
        return new CanaryCohort { Percentage = percentage };
    }

    /// <summary>Rejects a cohort that does not name exactly one selector, or an out-of-range percentage.</summary>
    public void Validate()
    {
        var hasGroup = !string.IsNullOrWhiteSpace(Group);
        var hasPercentage = Percentage is not null;
        if (hasGroup == hasPercentage)
            throw new PolicyAuthorityException(
                "A canary cohort must set exactly one of group or percentage.");
        if (hasPercentage && Percentage is < 1 or > 100)
            throw new PolicyAuthorityException("Canary percentage must be between 1 and 100.");
    }

    /// <summary>True when the given machine belongs to this cohort. Group cohorts match the machine's
    /// assigned group label (case-insensitive); percentage cohorts match when the machine's stable
    /// bucket (0–99) is below the target percentage.</summary>
    public bool Includes(string machineId, string? machineGroup)
    {
        if (!string.IsNullOrWhiteSpace(Group))
            return !string.IsNullOrWhiteSpace(machineGroup)
                && string.Equals(machineGroup, Group, StringComparison.OrdinalIgnoreCase);
        if (Percentage is not null)
            return Bucket(machineId) < Percentage.Value;
        return false;
    }

    /// <summary>Deterministic 0–99 bucket from the machine identity. Uses the leading bytes of a
    /// SHA-256 digest so the assignment is stable, uniform, and independent of enrollment order.</summary>
    internal static int Bucket(string machineId)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(machineId ?? ""), hash);
        return (int)(BinaryPrimitives.ReadUInt64BigEndian(hash) % 100);
    }
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
    DateTimeOffset PublishedAtUtc)
{
    /// <summary>Set only when <see cref="RolloutState"/> is <see cref="PolicyRolloutState.Canary"/>:
    /// the subset of machines this version targets. Null for fleet-wide versions.</summary>
    public CanaryCohort? Canary { get; init; }
}

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
            try
            {
                store.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly);
            }
            catch (Exception ex) when (IsUnsupportedStore(location, ex))
            {
                continue;
            }
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

    private static bool IsUnsupportedStore(
        System.Security.Cryptography.X509Certificates.StoreLocation location,
        Exception ex) =>
        location == System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine
        && (ex is PlatformNotSupportedException || ex.InnerException is PlatformNotSupportedException);

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
    /// <summary>The in-progress canary version for a tenant/environment, or null when none is running.</summary>
    Task<PublishedPolicyVersion?> GetCanaryAsync(string tenant, string environment, CancellationToken ct = default);
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
    Func<DateTimeOffset>? clock = null,
    ETL_SQL.Core.Multitenancy.TenantContext? authorityTenant = null)
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
        tenant = ScopeTenant(tenant);
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

        var envelope = BuildSignedEnvelope(document, tenant, policyVersion, issuedAt, expiresAtUtc);

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
        tenant = ScopeTenant(tenant);
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
        store.ListAsync(ScopeTenant(tenant), environment, ct);

    public Task<PublishedPolicyVersion?> GetActiveVersionAsync(
        string tenant, string environment, CancellationToken ct = default) =>
        store.GetActiveAsync(ScopeTenant(tenant), environment, ct);

    public Task<PublishedPolicyVersion?> GetCanaryVersionAsync(
        string tenant, string environment, CancellationToken ct = default) =>
        store.GetCanaryAsync(ScopeTenant(tenant), environment, ct);

    // ── Canary rollout ────────────────────────────────────────────────────────
    // A canary version is published alongside — not over — the active version and served only to its
    // cohort. The active version is never superseded by a canary, so the fleet is unaffected until the
    // canary is promoted. Halt reverts the cohort by re-issuing the active document (see HaltCanaryAsync).

    /// <summary>
    /// Publishes a new version targeted at a canary <paramref name="cohort"/> while the fleet stays on
    /// the current active version. Requires an existing active baseline to revert to and refuses to
    /// start a second canary while one is already in progress. Issues strictly later than the active
    /// version so cohort machines accept it and a later promote keeps issuance moving forward.
    /// </summary>
    public async Task<PublishedPolicyVersion> PublishCanaryAsync(
        OrganizationPolicyDocument document,
        string tenant,
        string environment,
        string policyVersion,
        string author,
        string? reviewer,
        DateTimeOffset expiresAtUtc,
        CanaryCohort cohort,
        CancellationToken ct = default)
    {
        tenant = ScopeTenant(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(environment);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(author);
        ArgumentNullException.ThrowIfNull(cohort);
        cohort.Validate();

        var validation = OrganizationPolicySchema.Validate(document);
        if (!validation.IsValid)
            throw new PolicyAuthorityException("Invalid policy document: " + string.Join("; ", validation.Errors));

        var now = _clock();
        if (expiresAtUtc <= now)
            throw new PolicyAuthorityException("Policy expiry must be in the future.");

        var existing = await store.ListAsync(tenant, environment, ct).ConfigureAwait(false);
        if (existing.Any(v => string.Equals(v.PolicyVersion, policyVersion, StringComparison.Ordinal)))
            throw new PolicyAuthorityException($"Policy version '{policyVersion}' already exists for {tenant}/{environment}.");
        if (existing.Any(v => v.RolloutState == PolicyRolloutState.Canary))
            throw new PolicyAuthorityException(
                $"A canary is already in progress for {tenant}/{environment}; promote or halt it before starting another.");

        var active = existing.FirstOrDefault(v => v.RolloutState == PolicyRolloutState.Active)
            ?? throw new PolicyAuthorityException(
                $"A canary needs an active fleet-wide policy to fall back to; publish an active version for {tenant}/{environment} first.");

        var issuedAt = Later(now, active.IssuedAtUtc.AddMilliseconds(1));
        var envelope = BuildSignedEnvelope(document, tenant, policyVersion, issuedAt, expiresAtUtc);
        var version = new PublishedPolicyVersion(
            tenant, environment, policyVersion, ComputeHash(document), issuedAt, expiresAtUtc,
            author, reviewer, active.PolicyVersion, PolicyRolloutState.Canary,
            JsonSerializer.Serialize(envelope, JsonOptions), now)
        {
            Canary = cohort
        };

        // Deliberately do NOT supersede the active version: the fleet keeps running it.
        await store.AppendAsync(version, ct).ConfigureAwait(false);
        return version;
    }

    /// <summary>
    /// Promotes a canary to the fleet-wide active version, superseding the previous active. The canary
    /// already issued later than that active version at publish time, so clients accept it fleet-wide.
    /// </summary>
    public async Task<PublishedPolicyVersion> PromoteCanaryAsync(
        string tenant, string environment, string policyVersion, CancellationToken ct = default)
    {
        tenant = ScopeTenant(tenant);
        var versions = await store.ListAsync(tenant, environment, ct).ConfigureAwait(false);
        var canary = versions.FirstOrDefault(v =>
            string.Equals(v.PolicyVersion, policyVersion, StringComparison.Ordinal))
            ?? throw new PolicyAuthorityException(
                $"Policy version '{policyVersion}' was not found for {tenant}/{environment}.");
        if (canary.RolloutState != PolicyRolloutState.Canary)
            throw new PolicyAuthorityException(
                $"Policy version '{policyVersion}' is {canary.RolloutState}; only a canary version can be promoted.");

        var active = versions.FirstOrDefault(v => v.RolloutState == PolicyRolloutState.Active);
        if (active is not null && canary.IssuedAtUtc <= active.IssuedAtUtc)
            throw new PolicyAuthorityException(
                $"Canary '{policyVersion}' was issued before the current active version '{active.PolicyVersion}' " +
                "and would be rejected by clients; halt it and start a fresh canary.");

        // Activate first so retrieval never sees a gap with no active policy.
        await store.SetRolloutStateAsync(tenant, environment, policyVersion,
            PolicyRolloutState.Active, ct).ConfigureAwait(false);
        if (active is not null)
            await store.SetRolloutStateAsync(tenant, environment, active.PolicyVersion,
                PolicyRolloutState.Superseded, ct).ConfigureAwait(false);
        return canary with { RolloutState = PolicyRolloutState.Active };
    }

    /// <summary>
    /// Halts a canary and reverts its cohort. Cohort machines hold an envelope issued later than the
    /// fleet active and would reject an older issuance (client rollback protection), so this re-issues
    /// the active document as a fresh active version issued later than the canary — halted machines
    /// then revert on their next poll. The canary is recorded as <see cref="PolicyRolloutState.RolledBack"/>.
    /// </summary>
    public async Task<PublishedPolicyVersion> HaltCanaryAsync(
        string tenant, string environment, string policyVersion,
        string author, string? reviewer, CancellationToken ct = default)
    {
        tenant = ScopeTenant(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(author);
        var versions = await store.ListAsync(tenant, environment, ct).ConfigureAwait(false);
        var canary = versions.FirstOrDefault(v =>
            string.Equals(v.PolicyVersion, policyVersion, StringComparison.Ordinal))
            ?? throw new PolicyAuthorityException(
                $"Policy version '{policyVersion}' was not found for {tenant}/{environment}.");
        if (canary.RolloutState != PolicyRolloutState.Canary)
            throw new PolicyAuthorityException(
                $"Policy version '{policyVersion}' is {canary.RolloutState}; only a canary version can be halted.");

        var active = versions.FirstOrDefault(v => v.RolloutState == PolicyRolloutState.Active)
            ?? throw new PolicyAuthorityException(
                $"No active fleet-wide policy exists for {tenant}/{environment} to revert canary machines to.");

        var now = _clock();
        if (active.ExpiresAtUtc <= now)
            throw new PolicyAuthorityException(
                $"The active policy for {tenant}/{environment} has expired; publish a fresh active version before halting the canary.");

        var issuedAt = Later(now, canary.IssuedAtUtc.AddMilliseconds(1));
        var reissuedVersion = $"{active.PolicyVersion}+halt.{now.UtcDateTime:yyyyMMddHHmmssfff}";
        var document = ReadDocument(active);
        var envelope = BuildSignedEnvelope(document, tenant, reissuedVersion, issuedAt, active.ExpiresAtUtc);
        var republished = new PublishedPolicyVersion(
            tenant, environment, reissuedVersion, ComputeHash(document), issuedAt, active.ExpiresAtUtc,
            author, reviewer, active.PolicyVersion, PolicyRolloutState.Active,
            JsonSerializer.Serialize(envelope, JsonOptions), now);

        await store.AppendAsync(republished, ct).ConfigureAwait(false);
        await store.SetRolloutStateAsync(tenant, environment, active.PolicyVersion,
            PolicyRolloutState.Superseded, ct).ConfigureAwait(false);
        await store.SetRolloutStateAsync(tenant, environment, policyVersion,
            PolicyRolloutState.RolledBack, ct).ConfigureAwait(false);
        return republished;
    }

    /// <summary>Returns the active signed envelope for a tenant/environment, for a machine to retrieve.</summary>
    public async Task<SignedOrganizationPolicyEnvelope?> RetrieveActiveEnvelopeAsync(
        string tenant, string environment, CancellationToken ct = default)
    {
        tenant = ScopeTenant(tenant);
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
        tenant = ScopeTenant(tenant);
        var versions = await store.ListAsync(tenant, environment, ct).ConfigureAwait(false);
        var target = versions.FirstOrDefault(v =>
            string.Equals(v.PolicyVersion, targetPolicyVersion, StringComparison.Ordinal))
            ?? throw new PolicyAuthorityException($"Rollback target '{targetPolicyVersion}' not found.");

        var document = ReadDocument(target);

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

    private SignedOrganizationPolicyEnvelope BuildSignedEnvelope(
        OrganizationPolicyDocument document, string tenant, string policyVersion,
        DateTimeOffset issuedAt, DateTimeOffset expiresAtUtc)
    {
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
        return envelope with { Signature = signer.Sign(envelope) };
    }

    private string ScopeTenant(string tenant) => authorityTenant is null
        ? ETL_SQL.Core.Multitenancy.TenantId.FromTrustedSource(tenant).Value
        : authorityTenant.RequireTenant(tenant).Value;

    private static OrganizationPolicyDocument ReadDocument(PublishedPolicyVersion version)
    {
        var envelope = JsonSerializer.Deserialize<SignedOrganizationPolicyEnvelope>(version.SignedEnvelopeJson, JsonOptions)
            ?? throw new PolicyAuthorityException("Stored policy envelope could not be read.");
        return OrganizationPolicySchema.ParseJson(
            Encoding.UTF8.GetString(Convert.FromBase64String(envelope.PolicyPayload)));
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

    public Task<PublishedPolicyVersion?> GetCanaryAsync(string tenant, string environment, CancellationToken ct = default)
    {
        var list = _versions.GetValueOrDefault(Key(tenant, environment));
        lock (Gate(tenant, environment))
            return Task.FromResult(list?.LastOrDefault(v => v.RolloutState == PolicyRolloutState.Canary));
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
