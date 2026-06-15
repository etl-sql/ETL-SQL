using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Storage;

/// <summary>
/// Provider-agnostic store for the deployment's artifacts — scripts, snapshots, cached datasets, maps,
/// and key material (<see cref="ArtifactArea"/>) — replacing direct filesystem access so multiple
/// nodes can share storage (Practical HA, Gap #3). Concrete providers (local disk, SMB/UNC; P1.5) sit
/// behind this contract, and the path-traversal / immutability guardrails (P1.6) are enforced here
/// rather than reimplemented per call site.
///
/// <para><b>Paths</b> are always relative to an area root and provider-agnostic: callers may pass
/// either separator, but every returned <see cref="ArtifactInfo.Path"/> is normalized to forward
/// slashes. Implementations MUST reject absolute paths and any path that escapes its area root.</para>
///
/// <para><b>Writes are atomic.</b> A reader concurrent with a write observes either the previous
/// content or the complete new content, never a partial artifact (filesystem providers achieve this by
/// writing to a temporary file and renaming into place).</para>
/// </summary>
public interface IArtifactStorage
{
    /// <summary>True if an artifact exists at <paramref name="path"/> within <paramref name="area"/>.</summary>
    Task<bool> ExistsAsync(ArtifactArea area, string path, CancellationToken ct = default);

    /// <summary>Metadata for the artifact, or <c>null</c> if it does not exist.</summary>
    Task<ArtifactInfo?> GetInfoAsync(ArtifactArea area, string path, CancellationToken ct = default);

    /// <summary>
    /// Enumerates artifacts in an area. <paramref name="prefix"/> (relative, optional) limits results
    /// to that subtree; <paramref name="recursive"/> controls whether nested artifacts are included.
    /// Filter by extension/name on the returned <see cref="ArtifactInfo.Path"/>.
    /// </summary>
    IAsyncEnumerable<ArtifactInfo> EnumerateAsync(
        ArtifactArea area, string? prefix = null, bool recursive = true, CancellationToken ct = default);

    /// <summary>Opens a readable, seekable-where-possible stream over the artifact. Caller disposes it.</summary>
    Task<Stream> OpenReadAsync(ArtifactArea area, string path, CancellationToken ct = default);

    /// <summary>Reads the whole artifact as bytes.</summary>
    Task<byte[]> ReadAllBytesAsync(ArtifactArea area, string path, CancellationToken ct = default);

    /// <summary>Reads the whole artifact as UTF-8 text.</summary>
    Task<string> ReadAllTextAsync(ArtifactArea area, string path, CancellationToken ct = default);

    /// <summary>
    /// Atomically writes the stream's content to <paramref name="path"/>. When
    /// <paramref name="overwrite"/> is false and the artifact already exists, throws
    /// <see cref="IOException"/> without modifying the existing content.
    /// </summary>
    Task WriteAsync(
        ArtifactArea area, string path, Stream content, bool overwrite = true, CancellationToken ct = default);

    /// <summary>Atomically writes bytes. See <see cref="WriteAsync"/> for overwrite semantics.</summary>
    Task WriteAllBytesAsync(
        ArtifactArea area, string path, ReadOnlyMemory<byte> content, bool overwrite = true, CancellationToken ct = default);

    /// <summary>Atomically writes UTF-8 text. See <see cref="WriteAsync"/> for overwrite semantics.</summary>
    Task WriteAllTextAsync(
        ArtifactArea area, string path, string content, bool overwrite = true, CancellationToken ct = default);

    /// <summary>Deletes the artifact if present. Returns true if an artifact was removed.</summary>
    Task<bool> DeleteAsync(ArtifactArea area, string path, CancellationToken ct = default);

    /// <summary>
    /// Atomically moves/renames an artifact within the same area (e.g. a staging file to its final
    /// name). When <paramref name="overwrite"/> is false and the destination exists, throws
    /// <see cref="IOException"/>.
    /// </summary>
    Task MoveAsync(
        ArtifactArea area, string sourcePath, string destinationPath, bool overwrite = false, CancellationToken ct = default);

    /// <summary>
    /// Materializes the artifact as a real local file and returns a lease exposing its
    /// <see cref="IArtifactLease.LocalPath"/>, for the consumers that must hand a filesystem path to a
    /// path-based reader (e.g. Parquet/Excel readers, connectors). A local provider returns the
    /// artifact's own path; a remote provider downloads to a temporary file. The temporary copy (if
    /// any) is removed when the lease is disposed. Not supported for <see cref="ArtifactArea.Keys"/>.
    /// </summary>
    Task<IArtifactLease> LeaseLocalCopyAsync(ArtifactArea area, string path, CancellationToken ct = default);
}

/// <summary>
/// A scoped, read-only local materialization of an artifact. Dispose to release it (deleting the
/// temporary copy a remote provider created; a no-op for a local provider).
/// </summary>
public interface IArtifactLease : IAsyncDisposable
{
    /// <summary>Absolute path to a local file holding the artifact's content for the lease's lifetime.</summary>
    string LocalPath { get; }
}
