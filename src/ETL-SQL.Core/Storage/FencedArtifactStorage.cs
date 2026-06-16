using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Core.Storage;

/// <summary>
/// Wraps an <see cref="IArtifactStorage"/> with database-backed write-epoch fencing (Practical HA P1.8)
/// so a stale node cannot overwrite a newer node's artifact on shared storage that lacks native fencing
/// (SMB/UNC). Before a create/replace — a write or a move's destination — the writer's current fence
/// token must atomically claim the artifact's write epoch via <see cref="IWriteEpochStore"/>; a token
/// older than the latest writer's is rejected with <see cref="FencedWriteException"/>, and the byte write
/// never happens. Reads, enumeration, deletion, and leasing pass through unfenced.
///
/// <para>The fence token comes from <c>currentToken</c> — in a cluster this is the node's lease/heartbeat
/// fence token, monotonically advanced whenever ownership changes, so a paused-then-resumed node always
/// presents an older token than the node that superseded it.</para>
/// </summary>
public sealed class FencedArtifactStorage : IArtifactStorage
{
    private readonly IArtifactStorage _inner;
    private readonly IWriteEpochStore _epochs;
    private readonly Func<long> _currentToken;
    private readonly string _scope;

    /// <param name="inner">The underlying provider that holds the bytes.</param>
    /// <param name="epochs">The fencing authority (shared database).</param>
    /// <param name="currentToken">Supplies this node's current fence token at write time.</param>
    /// <param name="scope">Namespaces these epochs apart from other write-epoch uses.</param>
    public FencedArtifactStorage(
        IArtifactStorage inner, IWriteEpochStore epochs, Func<long> currentToken, string scope = "artifact")
    {
        _inner = inner;
        _epochs = epochs;
        _currentToken = currentToken;
        _scope = scope;
    }

    private async Task FenceAsync(ArtifactArea area, string path)
    {
        var token = _currentToken();
        var key = $"{area}/{ArtifactPath.Normalize(path)}";
        if (!await _epochs.TryClaimWriteEpochAsync(_scope, key, token))
            throw new FencedWriteException(
                $"Fenced write rejected for '{key}': this node's fence token ({token}) is older than a newer " +
                "writer's. A more recent node has taken over; refusing to overwrite its artifact.");
    }

    public async Task WriteAsync(ArtifactArea area, string path, Stream content, bool overwrite = true, CancellationToken ct = default)
    {
        await FenceAsync(area, path);
        await _inner.WriteAsync(area, path, content, overwrite, ct);
    }

    public async Task WriteAllBytesAsync(ArtifactArea area, string path, ReadOnlyMemory<byte> content, bool overwrite = true, CancellationToken ct = default)
    {
        await FenceAsync(area, path);
        await _inner.WriteAllBytesAsync(area, path, content, overwrite, ct);
    }

    public async Task WriteAllTextAsync(ArtifactArea area, string path, string content, bool overwrite = true, CancellationToken ct = default)
    {
        await FenceAsync(area, path);
        await _inner.WriteAllTextAsync(area, path, content, overwrite, ct);
    }

    public async Task MoveAsync(ArtifactArea area, string sourcePath, string destinationPath, bool overwrite = false, CancellationToken ct = default)
    {
        await FenceAsync(area, destinationPath);
        await _inner.MoveAsync(area, sourcePath, destinationPath, overwrite, ct);
    }

    // ── Unfenced pass-throughs (reads / enumerate / delete / lease) ──────────────
    public Task<bool> ExistsAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        _inner.ExistsAsync(area, path, ct);
    public Task<ArtifactInfo?> GetInfoAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        _inner.GetInfoAsync(area, path, ct);
    public IAsyncEnumerable<ArtifactInfo> EnumerateAsync(ArtifactArea area, string? prefix = null, bool recursive = true, CancellationToken ct = default) =>
        _inner.EnumerateAsync(area, prefix, recursive, ct);
    public Task<Stream> OpenReadAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        _inner.OpenReadAsync(area, path, ct);
    public Task<byte[]> ReadAllBytesAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        _inner.ReadAllBytesAsync(area, path, ct);
    public Task<string> ReadAllTextAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        _inner.ReadAllTextAsync(area, path, ct);
    public Task<bool> DeleteAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        _inner.DeleteAsync(area, path, ct);
    public Task<IArtifactLease> LeaseLocalCopyAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        _inner.LeaseLocalCopyAsync(area, path, ct);
}

/// <summary>Thrown when a write is rejected because a newer node has fenced this writer out (P1.8).</summary>
public sealed class FencedWriteException(string message) : IOException(message);
