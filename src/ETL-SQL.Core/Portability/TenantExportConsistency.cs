using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ETL_SQL.Core.Portability;

/// <summary>A revision from one authoritative system participating in a tenant export.</summary>
public sealed record TenantExportRevision(string System, string Revision);

/// <summary>
/// The declared cross-system boundary carried by a bundle. The digest binds the tenant, database
/// revisions, artifact commit set, fence epoch, and whether mutations were drained before capture.
/// </summary>
public sealed record TenantExportConsistencyPoint(
    string TenantId,
    IReadOnlyList<TenantExportRevision> Revisions,
    IReadOnlyList<string> ArtifactCommitIds,
    long FenceEpoch,
    bool MutationsFenced,
    DateTimeOffset CapturedAtUtc,
    string Digest);

/// <summary>
/// Source-side seam for coordinated export. Implementations capture database revisions inside their
/// native snapshot transaction and expose immutable artifact commit ids, never mutable file names.
/// </summary>
public interface ITenantExportConsistencySource
{
    Task<IReadOnlyList<TenantExportRevision>> ReadRevisionsAsync(
        string tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ReadArtifactCommitIdsAsync(
        string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enters final-migration drain/fence mode and returns the durable epoch. New mutations and new
    /// schedule admissions for the tenant must be rejected until the fence is released or transferred.
    /// </summary>
    Task<long> FenceMutationsAsync(string tenantId, string operationId,
        CancellationToken cancellationToken = default);
}

/// <summary>Captures a stable cross-system export boundary by observation, or after a durable fence.</summary>
public static class TenantExportConsistencyCoordinator
{
    public static TenantExportConsistencyPoint Declare(
        string tenantId,
        IEnumerable<TenantExportRevision> revisions,
        IEnumerable<string> artifactCommitIds,
        long fenceEpoch,
        bool mutationsFenced,
        DateTimeOffset capturedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(revisions);
        ArgumentNullException.ThrowIfNull(artifactCommitIds);
        var normalizedRevisions = Normalize(revisions);
        var commits = artifactCommitIds.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        return new TenantExportConsistencyPoint(tenantId, normalizedRevisions, commits, fenceEpoch,
            mutationsFenced, capturedAtUtc,
            ComputeDigest(tenantId, normalizedRevisions, commits, fenceEpoch, mutationsFenced));
    }

    public static async Task<TenantExportConsistencyPoint> CaptureAsync(
        ITenantExportConsistencySource source,
        string tenantId,
        string operationId,
        bool finalCutover,
        DateTimeOffset capturedAtUtc,
        int maxAttempts = 8,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));

        var fence = finalCutover
            ? await source.FenceMutationsAsync(tenantId, operationId, cancellationToken).ConfigureAwait(false)
            : 0;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = Normalize(await source.ReadRevisionsAsync(tenantId, cancellationToken).ConfigureAwait(false));
            var commits = (await source.ReadArtifactCommitIdsAsync(tenantId, cancellationToken).ConfigureAwait(false))
                .OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var after = Normalize(await source.ReadRevisionsAsync(tenantId, cancellationToken).ConfigureAwait(false));
            if (!before.SequenceEqual(after)) continue;

            return Declare(tenantId, after, commits, fence, finalCutover, capturedAtUtc);
        }

        throw new InvalidOperationException(
            $"Tenant '{tenantId}' did not reach a stable cross-system export boundary after " +
            $"{maxAttempts} attempts. Retry, or use a final cutover fence.");
    }

    public static bool Verify(TenantExportConsistencyPoint point) =>
        string.Equals(point.Digest, ComputeDigest(point.TenantId, Normalize(point.Revisions),
            point.ArtifactCommitIds.OrderBy(x => x, StringComparer.Ordinal), point.FenceEpoch,
            point.MutationsFenced), StringComparison.OrdinalIgnoreCase);

    private static TenantExportRevision[] Normalize(IEnumerable<TenantExportRevision> revisions) =>
        revisions.OrderBy(x => x.System, StringComparer.Ordinal).ThenBy(x => x.Revision, StringComparer.Ordinal).ToArray();

    private static string ComputeDigest(string tenantId, IEnumerable<TenantExportRevision> revisions,
        IEnumerable<string> commits, long fence, bool fenced)
    {
        var canonical = JsonSerializer.Serialize(new { tenantId, revisions, commits, fence, fenced });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
