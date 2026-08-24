using System.Security.Cryptography;
using System.Text.Json;

namespace ETL_SQL.Core.Portability;

public sealed record TenantBundleFinding(string Code, string Severity, string Resource, string Message);

public sealed record TenantBundleValidationResult(
    TenantBundleManifest? Manifest,
    IReadOnlyList<TenantBundleFinding> Findings)
{
    public bool IsValid => Findings.All(f => f.Severity != "Error");
}

/// <summary>
/// The reference bundle reader and validator required by
/// <c>docs/architecture/TenantPortability.md</c> §16: a customer must be able to verify a bundle
/// without contacting the source SaaS operator, including after their access is gone. It therefore
/// depends on nothing but the bundle on disk.
/// </summary>
/// <remarks>
/// Validation is deliberately hostile to its input. A bundle arrives from outside the trust boundary
/// — it may be truncated, tampered with, replayed, or hand-edited — so every path is re-resolved
/// against the root and every payload is re-hashed rather than trusted from the manifest.
/// </remarks>
public static class TenantBundleValidator
{
    /// <summary>
    /// Substrings that must never appear in a manifest. §7 excludes resolved secret material from the
    /// bundle entirely, so their presence means the exporter leaked rather than that the bundle is
    /// merely unusual.
    /// </summary>
    private static readonly string[] ForbiddenManifestMarkers =
    [
        "-----BEGIN", "PRIVATE KEY", "password=", "pwd=", "access_token", "refresh_token"
    ];

    /// <param name="OperatorPublicKeyFile">Published operator key used to verify the signature.</param>
    /// <param name="RequireSignature">
    /// When true, a bundle with no valid operator signature fails. Callers importing from an
    /// untrusted source must set this: a stripped signature is indistinguishable from an unsigned
    /// export unless the caller states that it expected one.
    /// </param>
    public sealed record Options(
        string? OperatorPublicKeyFile = null,
        bool RequireSignature = false,
        string? RecipientPrivateKeyFile = null,
        string? RecipientPassphrase = null,
        long MaxManifestBytes = 16 * 1024 * 1024,
        int MaxComponents = 100_000,
        long MaxPayloadBytes = 64L * 1024 * 1024 * 1024,
        long MaxComponentBytes = 1024L * 1024 * 1024);

    public static Task<TenantBundleValidationResult> ValidateAsync(
        string bundleRoot, CancellationToken ct = default) =>
        ValidateAsync(bundleRoot, new Options(), ct);

    public static async Task<TenantBundleValidationResult> ValidateAsync(
        string bundleRoot, Options options, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleRoot);
        ArgumentNullException.ThrowIfNull(options);
        var findings = new List<TenantBundleFinding>();
        var root = Path.GetFullPath(bundleRoot);
        var manifestPath = Path.Combine(root, TenantBundle.ManifestFileName);

        if (!File.Exists(manifestPath))
        {
            return new TenantBundleValidationResult(null, [new TenantBundleFinding(
                "bundle.manifest.missing", "Error", TenantBundle.ManifestFileName,
                $"No {TenantBundle.ManifestFileName} at the bundle root. Without it nothing in the " +
                "directory can be attributed to a tenant, a source version, or a consistency point.")]);
        }

        var manifestLength = new FileInfo(manifestPath).Length;
        if (manifestLength > options.MaxManifestBytes)
        {
            return new TenantBundleValidationResult(null, [new TenantBundleFinding(
                "bundle.manifest.oversized", "Error", TenantBundle.ManifestFileName,
                $"The manifest is {manifestLength} bytes; the configured limit is {options.MaxManifestBytes}.")]);
        }

        // §13 requires signature verification to precede any trust in payload metadata, so this runs
        // before the manifest is parsed rather than after.
        var signaturePath = Path.Combine(root, TenantBundle.SignatureFileName);
        var signaturePresent = File.Exists(signaturePath);
        if (!string.IsNullOrWhiteSpace(options.OperatorPublicKeyFile))
        {
            var verified = await TenantBundleCrypto
                .VerifyDetachedAsync(manifestPath, signaturePath, options.OperatorPublicKeyFile!, ct)
                .ConfigureAwait(false);
            if (!verified)
            {
                findings.Add(new TenantBundleFinding(
                    "bundle.signature.invalid", "Error", TenantBundle.SignatureFileName,
                    signaturePresent
                        ? "The operator signature does not verify against the supplied key. The " +
                          "manifest was altered after signing, or it was signed by someone else."
                        : "No operator signature is present, but a verification key was supplied."));
                return new TenantBundleValidationResult(null, findings);
            }
        }
        else if (options.RequireSignature)
        {
            findings.Add(new TenantBundleFinding(
                "bundle.signature.unverified", "Error", TenantBundle.SignatureFileName,
                "A signature was required but no operator public key was supplied to verify it " +
                "against. The presence of a signature file proves nothing on its own."));
            return new TenantBundleValidationResult(null, findings);
        }

        var manifestJson = await File.ReadAllTextAsync(manifestPath, ct).ConfigureAwait(false);
        foreach (var marker in ForbiddenManifestMarkers)
        {
            if (manifestJson.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new TenantBundleFinding(
                    "bundle.manifest.secret-material", "Error", TenantBundle.ManifestFileName,
                    $"The manifest contains '{marker}', which looks like resolved secret material. " +
                    "A portability bundle carries secret references, never secret values."));
            }
        }

        TenantBundleManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<TenantBundleManifest>(
                manifestJson, TenantBundleWriter.JsonOptions);
        }
        catch (JsonException ex)
        {
            findings.Add(new TenantBundleFinding(
                "bundle.manifest.unreadable", "Error", TenantBundle.ManifestFileName,
                $"The manifest is not valid bundle JSON: {ex.Message}"));
            return new TenantBundleValidationResult(null, findings);
        }

        if (manifest is null)
        {
            findings.Add(new TenantBundleFinding(
                "bundle.manifest.unreadable", "Error", TenantBundle.ManifestFileName,
                "The manifest deserialized to null."));
            return new TenantBundleValidationResult(null, findings);
        }

        if (!string.Equals(manifest.SchemaVersion, TenantBundle.SchemaVersion, StringComparison.Ordinal)
            && !string.Equals(manifest.SchemaVersion, TenantBundle.Phase2SchemaVersion, StringComparison.Ordinal))
        {
            findings.Add(new TenantBundleFinding(
                "bundle.schema.unsupported", "Error", manifest.SchemaVersion ?? "(none)",
                $"Bundle schema '{manifest.SchemaVersion}' is not a supported v1/v2 tenant bundle. " +
                "Refusing to interpret an unknown schema as if it were this one."));
            return new TenantBundleValidationResult(manifest, findings);
        }


        if (manifest.Components.Count > options.MaxComponents
            || manifest.Components.Any(c => c.ByteLength < 0 || c.ByteLength > options.MaxComponentBytes)
            || manifest.Components.Sum(c => c.ByteLength) > options.MaxPayloadBytes)
        {
            findings.Add(new TenantBundleFinding(
                "bundle.limits.exceeded", "Error", TenantBundle.ManifestFileName,
                "The declared component count or payload bytes exceed the configured validation limits."));
            return new TenantBundleValidationResult(manifest, findings);
        }

        foreach (var duplicate in manifest.Components.GroupBy(c => c.LogicalId, StringComparer.Ordinal)
                     .Where(g => g.Count() > 1))
            findings.Add(new TenantBundleFinding("bundle.logical-id.duplicate", "Error", duplicate.Key,
                $"Logical id '{duplicate.Key}' appears {duplicate.Count()} times."));
        foreach (var duplicate in manifest.Components.GroupBy(c => c.Path, StringComparer.Ordinal)
                     .Where(g => g.Count() > 1))
            findings.Add(new TenantBundleFinding("bundle.path.duplicate", "Error", duplicate.Key,
                $"Payload path '{duplicate.Key}' is claimed by {duplicate.Count()} components."));

        if (manifest.ExportMode != TenantBundleExportMode.ConfigurationAndArtifacts
            && !string.Equals(manifest.SchemaVersion, TenantBundle.Phase2SchemaVersion, StringComparison.Ordinal))
        {
            findings.Add(new TenantBundleFinding(
                "bundle.mode.unsupported", "Error", manifest.ExportMode.ToString(),
                $"Export mode '{manifest.ExportMode}' is declared but not implemented in this " +
                "release. The bundle may be incomplete relative to what its mode promises."));
        }

        if (string.Equals(manifest.SchemaVersion, TenantBundle.Phase2SchemaVersion, StringComparison.Ordinal))
        {
            if (manifest.DeclaredConsistencyPoint is null
                || !TenantExportConsistencyCoordinator.Verify(manifest.DeclaredConsistencyPoint)
                || !string.Equals(manifest.DeclaredConsistencyPoint.TenantId,
                    manifest.TenantExportIdentity, StringComparison.Ordinal)
                || !string.Equals(manifest.ConsistencyPoint,
                    manifest.DeclaredConsistencyPoint.Digest, StringComparison.OrdinalIgnoreCase))
                findings.Add(new TenantBundleFinding("bundle.consistency.invalid", "Error",
                    "declaredConsistencyPoint", "The cross-system consistency point is absent, altered, or tenant-mismatched."));
            var inventory = TenantPortabilityInventory.Reconcile(manifest.TenantExportIdentity, manifest.Inventory ?? []);
            foreach (var error in inventory.Errors)
                findings.Add(new TenantBundleFinding("bundle.inventory.invalid", "Error", "inventory", error));
            var inventoryById = (manifest.Inventory ?? []).GroupBy(x => x.StableId, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
            var chunkedById = (manifest.ChunkedContent ?? []).GroupBy(x => x.StableId, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
            foreach (var component in manifest.Components)
            {
                if (!inventoryById.TryGetValue(component.LogicalId, out var item)
                    || item.Disposition != TenantInventoryDisposition.Included
                    || item.ByteLength != (component.PlaintextSha256 is null ? component.ByteLength : item.ByteLength)
                    || !string.Equals(item.Sha256, component.PlaintextSha256 ?? component.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                    findings.Add(new TenantBundleFinding("bundle.inventory.component-mismatch", "Error",
                        component.LogicalId, "The component is absent from inventory or its disposition/hash is different."));
            }
            foreach (var item in inventoryById.Values.Where(x => x.Disposition == TenantInventoryDisposition.Included))
                if (!manifest.Components.Any(x => x.LogicalId == item.StableId)
                    && (!chunkedById.TryGetValue(item.StableId, out var content)
                        || content.TotalLength != item.ByteLength
                        || !string.Equals(content.ContentSha256, item.Sha256, StringComparison.OrdinalIgnoreCase))
                    && (item.ContainerLogicalId is null
                        || !manifest.Components.Any(x => x.LogicalId == item.ContainerLogicalId)))
                    findings.Add(new TenantBundleFinding("bundle.inventory.content-missing", "Error", item.StableId,
                        "An included inventory row has no matching component or chunk index."));
            if (manifest.ExportMode == TenantBundleExportMode.FinalCutoverDelta
                && manifest.DeclaredConsistencyPoint?.MutationsFenced != true)
                findings.Add(new TenantBundleFinding("bundle.cutover.unfenced", "Error", "exportMode",
                    "A final cutover delta does not carry a durable mutation/scheduler fence."));
            if (manifest.ExportMode is TenantBundleExportMode.IncrementalDelta or TenantBundleExportMode.FinalCutoverDelta
                && string.IsNullOrWhiteSpace(manifest.BaseConsistencyPointDigest))
                findings.Add(new TenantBundleFinding("bundle.delta.base-missing", "Error", "baseConsistencyPointDigest",
                    "An incremental delta does not identify the certified base export."));
        }

        if (string.Equals(manifest.SourceProfile, TenantBundle.EncryptionRequiredSourceProfile,
                StringComparison.OrdinalIgnoreCase) && manifest.Encryption?.Encrypted != true)
        {
            findings.Add(new TenantBundleFinding(
                "bundle.encryption.required", "Error", manifest.SourceProfile,
                "This bundle declares a SaaS source but its payloads are not encrypted to a tenant " +
                "recipient key (§13.1). Tenant data must not travel in the clear out of SaaS."));
        }

        if (manifest.SignatureFile is not null && !signaturePresent)
        {
            findings.Add(new TenantBundleFinding(
                "bundle.signature.missing", "Error", manifest.SignatureFile,
                "The manifest says it was signed, but the signature file is absent. Someone removed " +
                "it after export."));
        }

        foreach (var component in manifest.Components)
        {
            string resolved;
            try
            {
                resolved = TenantBundleWriter.ResolveInside(root, component.Path, component.LogicalId);
            }
            catch (ArgumentException ex)
            {
                findings.Add(new TenantBundleFinding(
                    "bundle.path.escape", "Error", component.LogicalId, ex.Message));
                continue;
            }

            if (!File.Exists(resolved))
            {
                findings.Add(new TenantBundleFinding(
                    "bundle.payload.missing", "Error", component.LogicalId,
                    $"The manifest lists '{component.Path}' but the file is absent. A truncated " +
                    "bundle must fail here rather than import as a smaller tenant."));
                continue;
            }

            var actualLength = new FileInfo(resolved).Length;
            if (actualLength != component.ByteLength)
            {
                findings.Add(new TenantBundleFinding(
                    "bundle.payload.length", "Error", component.LogicalId,
                    $"'{component.Path}' is {actualLength} bytes; the manifest declares " +
                    $"{component.ByteLength}."));
            }

            await using var content = new FileStream(resolved, FileMode.Open, FileAccess.Read,
                FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(content, ct).ConfigureAwait(false))
                .ToLowerInvariant();
            if (!string.Equals(actual, component.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new TenantBundleFinding(
                    "bundle.payload.hash", "Error", component.LogicalId,
                    $"'{component.Path}' hashes to {actual}; the manifest declares {component.Sha256}. " +
                    "The payload was altered after export."));
            }


            if (manifest.Encryption?.Encrypted == true
                && !string.IsNullOrWhiteSpace(options.RecipientPrivateKeyFile)
                && component.PlaintextSha256 is { } expectedPlaintext)
            {
                try
                {
                    await using var encrypted = new FileStream(resolved, FileMode.Open, FileAccess.Read,
                        FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await using var plaintext = new HashingSinkStream();
                    await TenantBundleCrypto.DecryptAsync(encrypted, plaintext,
                        options.RecipientPrivateKeyFile!, options.RecipientPassphrase, ct).ConfigureAwait(false);
                    var plaintextHash = plaintext.GetHash();
                    if (!string.Equals(plaintextHash, expectedPlaintext, StringComparison.OrdinalIgnoreCase))
                        findings.Add(new TenantBundleFinding("bundle.payload.plaintext-hash", "Error",
                            component.LogicalId, "The decrypted payload does not match its export-time plaintext hash."));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    findings.Add(new TenantBundleFinding("bundle.payload.decrypt", "Error",
                        component.LogicalId, $"The customer-held recipient key could not authenticate this payload: {ex.Message}"));
                }
            }
        }

        var declared = manifest.Components.Select(c => c.LogicalId).ToHashSet(StringComparer.Ordinal);
        foreach (var component in manifest.Components)
        {
            foreach (var dependency in component.DependsOn.Where(d => !declared.Contains(d)))
            {
                findings.Add(new TenantBundleFinding(
                    "bundle.dependency.missing", "Error", component.LogicalId,
                    $"Depends on '{dependency}', which the bundle does not contain. Preflight cannot " +
                    "order activation against a dependency that will never arrive."));
            }
        }

        var totalIncluded = manifest.Counts.Included.Values.Sum();
        if (totalIncluded != manifest.Components.Count)
        {
            findings.Add(new TenantBundleFinding(
                "bundle.counts.mismatch", "Error", "counts.included",
                $"Included counts total {totalIncluded} but the manifest lists " +
                $"{manifest.Components.Count} components. The reconciliation numbers a customer " +
                "checks must describe the bundle they actually received."));
        }

        return new TenantBundleValidationResult(manifest, findings);
    }

    private sealed class HashingSinkStream : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        public string GetHash() => Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();
        public override void Write(byte[] buffer, int offset, int count) => _hash.AppendData(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => _hash.AppendData(buffer);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); _hash.AppendData(buffer.Span); return ValueTask.CompletedTask; }
        protected override void Dispose(bool disposing) { if (disposing) _hash.Dispose(); base.Dispose(disposing); }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
