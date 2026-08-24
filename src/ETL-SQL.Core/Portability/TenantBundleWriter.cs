using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ETL_SQL.Core.Portability;

/// <summary>One payload offered to the writer before hashing and placement.</summary>
public sealed record TenantBundlePayload(
    string LogicalId,
    string ResourceClass,
    string ContentType,
    string RelativePath,
    byte[] Content,
    IReadOnlyList<string> DependsOn)
{
    public TenantBundlePayload(string logicalId, string resourceClass, string contentType,
        string relativePath, string content)
        : this(logicalId, resourceClass, contentType, relativePath, Encoding.UTF8.GetBytes(content), [])
    {
    }
}

/// <summary>Everything the caller supplies for one export.</summary>
/// <param name="RecipientPublicKeyFile">
/// Tenant-supplied OpenPGP recipient key. Required when <paramref name="SourceProfile"/> is SaaS.
/// </param>
/// <param name="SigningPrivateKeyFile">
/// Operator signing key. When supplied, a detached signature over the canonical manifest is written.
/// </param>
public sealed record TenantBundleRequest(
    string BundleId,
    DateTimeOffset CreatedUtc,
    string SourceProductVersion,
    string SourceProfile,
    string TenantExportIdentity,
    TenantBundleExportMode ExportMode,
    string ConsistencyPoint,
    IReadOnlyList<TenantBundlePayload> Payloads,
    IReadOnlyList<TenantBundleRequiredBinding> RequiredBindings,
    IReadOnlyList<TenantBundleExclusion> Exclusions,
    string? RecipientPublicKeyFile = null,
    string? SigningPrivateKeyFile = null,
    string? SigningPassphrase = null,
    TenantExportConsistencyPoint? DeclaredConsistencyPoint = null,
    IReadOnlyList<TenantInventoryItem>? Inventory = null,
    IReadOnlyList<TenantChunkedContent>? ChunkedContent = null,
    string? BaseConsistencyPointDigest = null);

/// <summary>
/// Writes the unified bundle described in <c>docs/architecture/TenantPortability.md</c> §5.
/// </summary>
public static class TenantBundleWriter
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        // Canonical ordering comes from the writer, not the serializer, so the manifest bytes are
        // reproducible across runtimes.
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Writes the bundle rooted at <paramref name="bundleRoot"/> and returns the manifest as written.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The request declares an unsupported export mode, a duplicate logical id, or a payload path
    /// that escapes the bundle root.
    /// </exception>
    public static async Task<TenantBundleManifest> WriteAsync(
        string bundleRoot, TenantBundleRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleRoot);
        ArgumentNullException.ThrowIfNull(request);

        if (request.ExportMode != TenantBundleExportMode.ConfigurationAndArtifacts
            && request.DeclaredConsistencyPoint is null)
        {
            throw new ArgumentException(
                $"Export mode '{request.ExportMode}' is not implemented without a declared Phase 2 " +
                "consistency point. Refusing to write a bundle whose mode overstates what it contains.",
                nameof(request));
        }

        if (request.DeclaredConsistencyPoint is { } point)
        {
            if (!string.Equals(point.TenantId, request.TenantExportIdentity, StringComparison.Ordinal)
                || !TenantExportConsistencyCoordinator.Verify(point))
                throw new ArgumentException("The declared consistency point is invalid or belongs to another tenant.", nameof(request));
            if (request.ExportMode == TenantBundleExportMode.FinalCutoverDelta && !point.MutationsFenced)
                throw new ArgumentException("A final cutover delta requires a fenced consistency point.", nameof(request));
            if (request.ExportMode is TenantBundleExportMode.IncrementalDelta or TenantBundleExportMode.FinalCutoverDelta
                && string.IsNullOrWhiteSpace(request.BaseConsistencyPointDigest))
                throw new ArgumentException("A delta must name its base consistency-point digest.", nameof(request));
            var reconciliation = TenantPortabilityInventory.Reconcile(request.TenantExportIdentity,
                request.Inventory ?? []);
            if (!reconciliation.IsComplete)
                throw new ArgumentException("The Phase 2 inventory is incomplete: " + string.Join("; ", reconciliation.Errors), nameof(request));
        }

        var duplicate = request.Payloads
            .GroupBy(p => p.LogicalId, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Logical id '{duplicate.Key}' appears {duplicate.Count()} times. Logical ids are the " +
                "identity the target reconciles against, so a duplicate would silently drop one object.",
                nameof(request));
        }

        var encrypting = !string.IsNullOrWhiteSpace(request.RecipientPublicKeyFile);
        if (!encrypting && string.Equals(request.SourceProfile,
                TenantBundle.EncryptionRequiredSourceProfile, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "A SaaS-sourced bundle must be encrypted to a tenant-supplied recipient key " +
                "(TenantPortability.md §13.1). Refusing to write tenant payloads in the clear.",
                nameof(request));
        }

        var root = Path.GetFullPath(bundleRoot);
        Directory.CreateDirectory(root);

        var components = new List<TenantBundleComponent>(request.Payloads.Count);
        foreach (var payload in request.Payloads)
        {
            ct.ThrowIfCancellationRequested();
            var destination = ResolveInside(root, payload.RelativePath, payload.LogicalId);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            var plaintextHash = Convert.ToHexString(SHA256.HashData(payload.Content)).ToLowerInvariant();
            byte[] stored;
            if (encrypting)
            {
                stored = await TenantBundleCrypto
                    .EncryptAsync(payload.Content, request.RecipientPublicKeyFile!, ct)
                    .ConfigureAwait(false);
            }
            else
            {
                stored = payload.Content;
            }

            await File.WriteAllBytesAsync(destination, stored, ct).ConfigureAwait(false);

            components.Add(new TenantBundleComponent(
                payload.LogicalId,
                payload.ResourceClass,
                payload.ContentType,
                stored.LongLength,
                Convert.ToHexString(SHA256.HashData(stored)).ToLowerInvariant(),
                NormalizePath(payload.RelativePath),
                [.. payload.DependsOn.OrderBy(d => d, StringComparer.Ordinal)],
                encrypting ? plaintextHash : null));
        }

        if (request.DeclaredConsistencyPoint is not null)
            ValidatePhase2Inventory(request, components);

        var manifest = new TenantBundleManifest(
            request.DeclaredConsistencyPoint is null ? TenantBundle.SchemaVersion : TenantBundle.Phase2SchemaVersion,
            request.BundleId,
            request.CreatedUtc,
            request.SourceProductVersion,
            request.SourceProfile,
            request.TenantExportIdentity,
            request.ExportMode,
            request.DeclaredConsistencyPoint?.Digest ?? request.ConsistencyPoint,
            [.. components.OrderBy(c => c.LogicalId, StringComparer.Ordinal)],
            [.. request.RequiredBindings.OrderBy(b => b.LogicalId, StringComparer.Ordinal)],
            [.. request.Exclusions.OrderBy(e => e.LogicalId, StringComparer.Ordinal)],
            new TenantBundleCounts(
                CountByClass(request.Payloads.Select(p => p.ResourceClass)),
                CountByClass(request.Exclusions.Select(e => e.ResourceClass))),
            new TenantBundleEncryption(
                encrypting,
                encrypting ? TenantBundle.EncryptionAlgorithm : null,
                encrypting ? TenantBundleCrypto.Fingerprint(request.RecipientPublicKeyFile!) : null),
            string.IsNullOrWhiteSpace(request.SigningPrivateKeyFile)
                ? null
                : TenantBundle.SignatureFileName,
            request.DeclaredConsistencyPoint,
            request.Inventory is null ? null : [.. request.Inventory.OrderBy(x => x.StableId, StringComparer.Ordinal)],
            request.ChunkedContent is null ? null : [.. request.ChunkedContent.OrderBy(x => x.StableId, StringComparer.Ordinal)],
            request.BaseConsistencyPointDigest);

        var manifestPath = Path.Combine(root, TenantBundle.ManifestFileName);
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, JsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(request.SigningPrivateKeyFile))
        {
            // Signed last: the signature covers the manifest exactly as written, and the manifest
            // names every payload by hash, so one operator signature transitively covers the bundle.
            var signaturePath = ResolveInside(root, TenantBundle.SignatureFileName, "signature");
            Directory.CreateDirectory(Path.GetDirectoryName(signaturePath)!);
            await TenantBundleCrypto.SignDetachedAsync(
                manifestPath, signaturePath, request.SigningPrivateKeyFile!,
                request.SigningPassphrase, ct).ConfigureAwait(false);
        }

        return manifest;
    }

    /// <summary>
    /// A digest over everything except documented generation metadata (bundle id and creation time),
    /// so two exports of an unchanged tenant can be shown to be identical. §5.1 requires the manifest
    /// to be deterministic for the same consistency point; this is how that is asserted.
    /// </summary>
    public static string ComputeDeterministicDigest(TenantBundleManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        // Projected rather than taken from the record, because ciphertext is not comparable.
        // OpenPGP uses a fresh session key per run, so an encrypted export's stored hash and byte
        // length differ every time even when the tenant state is identical. The plaintext hash is
        // the only thing that answers "is this the same state?", so the projection prefers it and
        // drops the stored length.
        var comparable = new
        {
            manifest.SchemaVersion,
            manifest.SourceProductVersion,
            manifest.SourceProfile,
            manifest.TenantExportIdentity,
            manifest.ExportMode,
            manifest.ConsistencyPoint,
            Components = manifest.Components.Select(c => new
            {
                c.LogicalId,
                c.ResourceClass,
                c.ContentType,
                c.Path,
                c.DependsOn,
                ContentHash = c.PlaintextSha256 ?? c.Sha256
            }),
            manifest.RequiredBindings,
            manifest.Exclusions,
            manifest.Counts,
            Encrypted = manifest.Encryption?.Encrypted ?? false,
            manifest.DeclaredConsistencyPoint,
            manifest.Inventory,
            manifest.ChunkedContent,
            manifest.BaseConsistencyPointDigest
        };

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(comparable, JsonOptions))))
            .ToLowerInvariant();
    }

    private static IReadOnlyDictionary<string, int> CountByClass(IEnumerable<string> classes) =>
        classes.GroupBy(c => c, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

    internal static string NormalizePath(string relativePath) => relativePath.Replace('\\', '/');

    private static void ValidatePhase2Inventory(
        TenantBundleRequest request, IReadOnlyList<TenantBundleComponent> components)
    {
        var inventory = (request.Inventory ?? []).ToDictionary(x => x.StableId, StringComparer.Ordinal);
        var chunked = (request.ChunkedContent ?? []).ToDictionary(x => x.StableId, StringComparer.Ordinal);
        foreach (var component in components)
        {
            if (!inventory.TryGetValue(component.LogicalId, out var item)
                || item.Disposition != TenantInventoryDisposition.Included)
                throw new ArgumentException($"Component '{component.LogicalId}' has no included inventory row.", nameof(request));
            var contentHash = component.PlaintextSha256 ?? component.Sha256;
            if (item.ByteLength != request.Payloads.Single(x => x.LogicalId == component.LogicalId).Content.LongLength
                || !string.Equals(item.Sha256, contentHash, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Component '{component.LogicalId}' does not match its inventory length/hash.", nameof(request));
        }
        foreach (var item in inventory.Values.Where(x => x.Disposition == TenantInventoryDisposition.Included))
        {
            var represented = components.Any(x => x.LogicalId == item.StableId)
                || chunked.TryGetValue(item.StableId, out var content)
                   && content.TotalLength == item.ByteLength
                   && string.Equals(content.ContentSha256, item.Sha256, StringComparison.OrdinalIgnoreCase)
                || item.ContainerLogicalId is not null
                   && components.Any(x => x.LogicalId == item.ContainerLogicalId);
            if (!represented)
                throw new ArgumentException($"Included inventory item '{item.StableId}' has no payload or chunk index.", nameof(request));
        }
        foreach (var item in inventory.Values.Where(x => x.Disposition != TenantInventoryDisposition.Included))
            if (!request.Exclusions.Any(x => x.LogicalId == item.StableId))
                throw new ArgumentException($"Inventory exclusion '{item.StableId}' is absent from manifest exclusions.", nameof(request));
    }

    /// <summary>
    /// Resolves a payload path inside the bundle root, refusing absolute paths and traversal. A
    /// bundle is written from tenant-supplied logical names, so the path is untrusted input even on
    /// the export side.
    /// </summary>
    internal static string ResolveInside(string root, string relativePath, string logicalId)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException(
                $"Payload '{logicalId}' must declare a relative path inside the bundle; got '{relativePath}'.",
                nameof(relativePath));
        }

        var combined = Path.GetFullPath(Path.Combine(root, relativePath));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Payload '{logicalId}' resolves outside the bundle root: '{relativePath}'.",
                nameof(relativePath));
        }

        return combined;
    }
}
