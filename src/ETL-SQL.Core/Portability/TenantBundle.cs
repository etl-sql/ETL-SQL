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

    /// <summary>The canonical manifest file name at the bundle root.</summary>
    public const string ManifestFileName = "manifest.json";
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
public sealed record TenantBundleComponent(
    string LogicalId,
    string ResourceClass,
    string ContentType,
    long ByteLength,
    string Sha256,
    string Path,
    IReadOnlyList<string> DependsOn);

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
    TenantBundleCounts Counts);
