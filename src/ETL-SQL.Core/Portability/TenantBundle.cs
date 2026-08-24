namespace ETL_SQL.Core.Portability;

/// <summary>
/// The one unified tenant portability bundle defined by
/// <c>docs/architecture/TenantPortability.md</c> §5. It supersedes nothing: it composes the existing
/// Portal configuration export, Orchestrator promotion package, and portable source artifacts into a
/// single documented, versioned, hash-verified package rather than adding a fourth export format.
/// </summary>
/// <remarks>
/// Only the minimum configuration/artifact mode ships first, per §5.3. Large content, evidence, and
/// incremental deltas are deliberately absent rather than stubbed — a mode that exists but does not
/// work is worse than one a customer can see is unavailable.
/// </remarks>
public static class TenantBundle
{
    public const string SchemaVersion = "etl-sql.tenant-bundle/v1";
    public const string Phase2SchemaVersion = "etl-sql.tenant-bundle/v2";

    /// <summary>The canonical manifest file name at the bundle root.</summary>
    public const string ManifestFileName = "manifest.json";

    /// <summary>Detached operator signature over <see cref="ManifestFileName"/> (§13.1).</summary>
    public const string SignatureFileName = "signatures/manifest.asc";

    /// <summary>The only payload protection this release implements.</summary>
    public const string EncryptionAlgorithm = "openpgp";

    /// <summary>
    /// Source profile that makes payload encryption mandatory. §13.1 requires it for SaaS-sourced
    /// exports and leaves it optional-but-recorded for an operator moving their own tenant.
    /// </summary>
    public const string EncryptionRequiredSourceProfile = "SaaS";
}

/// <summary>
/// Export modes from <c>TenantPortability.md</c> §5.3. Only <see cref="ConfigurationAndArtifacts"/>
/// is supported today; the others are named so a manifest that declares one can be rejected with an
/// accurate reason instead of a schema error.
/// </summary>
public enum TenantBundleExportMode
{
    ConfigurationAndArtifacts,
    ConfigurationWithSelectedContent,
    FullEligibleTenantExport,
    IncrementalDelta,
    FinalCutoverDelta
}

/// <summary>
/// One included object. <paramref name="LogicalId"/> is the stable identity that survives migration;
/// a display name is not an identity and may collide at the target (§5.2).
/// </summary>
/// <param name="Sha256">
/// Hash of the bytes <em>as stored</em>. When the payload is encrypted this is the ciphertext hash,
/// so a customer can verify the bundle is intact without holding the recipient key.
/// </param>
/// <param name="PlaintextSha256">
/// Hash of the payload before encryption, null when the bundle is unencrypted. Verified after
/// decryption. OpenPGP encryption is non-deterministic — a fresh session key per run — so the
/// ciphertext hash says nothing about whether two exports carry the same tenant state; this does.
/// </param>
public sealed record TenantBundleComponent(
    string LogicalId,
    string ResourceClass,
    string ContentType,
    long ByteLength,
    string Sha256,
    string Path,
    IReadOnlyList<string> DependsOn,
    string? PlaintextSha256 = null);

/// <summary>
/// How the payloads are protected (§13.1). Required when the source profile is SaaS; recorded either
/// way so "unencrypted" is a stated fact rather than an absence a reader has to interpret.
/// </summary>
public sealed record TenantBundleEncryption(
    bool Encrypted,
    string? Algorithm,
    string? RecipientKeyDigest);

/// <summary>
/// Something eligible-looking that did not travel. Every non-portable item carries a reason and, when
/// the target must supply something, the remediation — §5.1 requires both, because a silent omission
/// is indistinguishable from a successful export until the customer needs the missing thing.
/// </summary>
public sealed record TenantBundleExclusion(
    string LogicalId,
    string ResourceClass,
    string Reason,
    string? Remediation);

/// <summary>
/// A binding the target environment must supply before activation: identity, connection, secret
/// reference, path, key, policy, or Gateway resource. The bundle carries the requirement, never the
/// resolved value (§7).
/// </summary>
public sealed record TenantBundleRequiredBinding(
    string LogicalId,
    string BindingClass,
    string Description);

/// <summary>Per-resource-class disposition counts (§5.1).</summary>
public sealed record TenantBundleCounts(
    IReadOnlyDictionary<string, int> Included,
    IReadOnlyDictionary<string, int> Excluded);

/// <summary>
/// The canonical manifest. Deterministic for the same consistency point and export options, except
/// for <see cref="CreatedUtc"/> and <see cref="BundleId"/>, which are documented generation metadata
/// (§5.1) — <see cref="TenantBundleWriter.ComputeDeterministicDigest"/> excludes them so two exports
/// of unchanged state can be compared.
/// </summary>
public sealed record TenantBundleManifest(
    string SchemaVersion,
    string BundleId,
    DateTimeOffset CreatedUtc,
    string SourceProductVersion,
    string SourceProfile,
    string TenantExportIdentity,
    TenantBundleExportMode ExportMode,
    string ConsistencyPoint,
    IReadOnlyList<TenantBundleComponent> Components,
    IReadOnlyList<TenantBundleRequiredBinding> RequiredBindings,
    IReadOnlyList<TenantBundleExclusion> Exclusions,
    TenantBundleCounts Counts,
    TenantBundleEncryption? Encryption = null,
    string? SignatureFile = null,
    TenantExportConsistencyPoint? DeclaredConsistencyPoint = null,
    IReadOnlyList<TenantInventoryItem>? Inventory = null,
    IReadOnlyList<TenantChunkedContent>? ChunkedContent = null,
    string? BaseConsistencyPointDigest = null);
