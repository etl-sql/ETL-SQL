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
    IReadOnlyList<TenantBundleExclusion> Exclusions);

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
    public static TenantBundleManifest Write(string bundleRoot, TenantBundleRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleRoot);
        ArgumentNullException.ThrowIfNull(request);

        if (request.ExportMode != TenantBundleExportMode.ConfigurationAndArtifacts)
        {
            throw new ArgumentException(
                $"Export mode '{request.ExportMode}' is not implemented. Only " +
                $"'{nameof(TenantBundleExportMode.ConfigurationAndArtifacts)}' ships today; see " +
                "TenantPortability.md §5.3. Refusing rather than writing a bundle whose mode " +
                "overstates what it contains.",
                nameof(request));
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

        var root = Path.GetFullPath(bundleRoot);
        Directory.CreateDirectory(root);

        var components = new List<TenantBundleComponent>(request.Payloads.Count);
        foreach (var payload in request.Payloads)
        {
            var destination = ResolveInside(root, payload.RelativePath, payload.LogicalId);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllBytes(destination, payload.Content);

            components.Add(new TenantBundleComponent(
                payload.LogicalId,
                payload.ResourceClass,
                payload.ContentType,
                payload.Content.LongLength,
                Convert.ToHexString(SHA256.HashData(payload.Content)).ToLowerInvariant(),
                NormalizePath(payload.RelativePath),
                [.. payload.DependsOn.OrderBy(d => d, StringComparer.Ordinal)]));
        }

        var manifest = new TenantBundleManifest(
            TenantBundle.SchemaVersion,
            request.BundleId,
            request.CreatedUtc,
            request.SourceProductVersion,
            request.SourceProfile,
            request.TenantExportIdentity,
            request.ExportMode,
            request.ConsistencyPoint,
            [.. components.OrderBy(c => c.LogicalId, StringComparer.Ordinal)],
            [.. request.RequiredBindings.OrderBy(b => b.LogicalId, StringComparer.Ordinal)],
            [.. request.Exclusions.OrderBy(e => e.LogicalId, StringComparer.Ordinal)],
            new TenantBundleCounts(
                CountByClass(request.Payloads.Select(p => p.ResourceClass)),
                CountByClass(request.Exclusions.Select(e => e.ResourceClass))));

        File.WriteAllText(
            Path.Combine(root, TenantBundle.ManifestFileName),
            JsonSerializer.Serialize(manifest, JsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

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
        var comparable = manifest with
        {
            BundleId = string.Empty,
            CreatedUtc = DateTimeOffset.UnixEpoch
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
