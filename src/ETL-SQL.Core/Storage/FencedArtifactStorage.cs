using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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
/// never happens. Destructive moves and deletes are fenced by the same policy.
///
/// <para>Portal hosts use the database-backed cluster-lock constructor, which serializes each artifact
/// mutation across healthy nodes and advances its epoch under that ownership lease. Callers that
/// already hold a database-issued job lease may instead supply its explicit fence token.</para>
/// </summary>
public sealed class FencedArtifactStorage : IArtifactStorage
{
    private readonly IArtifactStorage _inner;
    private readonly IWriteEpochStore _epochs;
    private readonly Func<long>? _currentToken;
    private readonly IClusterLockStore? _locks;
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

    /// <summary>Creates storage that obtains per-operation ownership from the shared database.</summary>
    public FencedArtifactStorage(
        IArtifactStorage inner, IWriteEpochStore epochs, IClusterLockStore locks, string scope = "artifact")
    {
        _inner = inner;
        _epochs = epochs;
        _locks = locks;
        _scope = scope;
    }

    private async Task FenceAsync(ArtifactArea area, string path)
    {
        var token = _currentToken!();
        var key = $"{area}/{ArtifactPath.Normalize(path)}";
        if (!await _epochs.TryClaimWriteEpochAsync(_scope, key, token))
            throw new FencedWriteException(
                $"Fenced write rejected for '{key}': this node's fence token ({token}) is older than a newer " +
                "writer's. A more recent node has taken over; refusing to overwrite its artifact.");
    }

    private async Task<T> ExecuteFencedAsync<T>(
        IReadOnlyList<(ArtifactArea Area, string Path)> resources,
        Func<Task<T>> action,
        CancellationToken ct)
    {
        if (_locks is null)
        {
            foreach (var resource in resources)
                await FenceAsync(resource.Area, resource.Path);
            return await action();
        }

        var keys = resources
            .Select(resource => $"{resource.Area}/{ArtifactPath.Normalize(resource.Path)}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
        var owner = Guid.NewGuid().ToString("N");
        var lockNames = keys.Select(key =>
            "artifact:" + Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes($"{_scope}:{key}"))).ToLowerInvariant())
            .ToArray();
        var acquiredLocks = new List<string>(lockNames.Length);
        CancellationTokenSource? renewalStop = null;
        Task<bool>? renewalTask = null;

        try
        {
            foreach (var lockName in lockNames)
            {
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (!await _locks.TryAcquireLockAsync(lockName, owner, TimeSpan.FromMinutes(5)))
                {
                    ct.ThrowIfCancellationRequested();
                    if (DateTime.UtcNow >= deadline)
                        throw new FencedWriteException(
                            $"Timed out waiting for the artifact ownership lease '{lockName}'.");
                    await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
                }
                acquiredLocks.Add(lockName);
            }

            renewalStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            renewalTask = RenewLeasesAsync(lockNames, owner, renewalStop.Token);

            foreach (var key in keys)
            {
                var current = await _epochs.GetWriteEpochAsync(_scope, key);
                if (current == long.MaxValue
                    || !await _epochs.TryClaimWriteEpochAsync(_scope, key, current + 1))
                    throw new FencedWriteException($"Could not advance the write epoch for '{key}'.");
            }
            var result = await action();
            if (renewalTask.IsCompletedSuccessfully && !renewalTask.Result)
                throw new FencedWriteException("The artifact ownership lease was lost during the mutation.");
            return result;
        }
        finally
        {
            if (renewalStop is not null)
            {
                renewalStop.Cancel();
                if (renewalTask is not null)
                {
                    try { await renewalTask; }
                    catch (OperationCanceledException) { }
                }
                renewalStop.Dispose();
            }
            for (var index = acquiredLocks.Count - 1; index >= 0; index--)
                await _locks.ReleaseLockAsync(acquiredLocks[index], owner);
        }
    }

    private async Task<bool> RenewLeasesAsync(
        IReadOnlyList<string> lockNames,
        string owner,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            foreach (var lockName in lockNames)
            {
                if (!await _locks!.TryRenewLockAsync(lockName, owner, TimeSpan.FromMinutes(5)))
                    return false;
            }
        }
    }

    public async Task WriteAsync(ArtifactArea area, string path, Stream content, bool overwrite = true, CancellationToken ct = default)
    {
        await ExecuteFencedAsync([(area, path)], async () =>
        {
            await _inner.WriteAsync(area, path, content, overwrite, ct);
            return true;
        }, ct);
    }

    public async Task WriteAllBytesAsync(ArtifactArea area, string path, ReadOnlyMemory<byte> content, bool overwrite = true, CancellationToken ct = default)
    {
        await ExecuteFencedAsync([(area, path)], async () =>
        {
            await _inner.WriteAllBytesAsync(area, path, content, overwrite, ct);
            return true;
        }, ct);
    }

    public async Task WriteAllTextAsync(ArtifactArea area, string path, string content, bool overwrite = true, CancellationToken ct = default)
    {
        await ExecuteFencedAsync([(area, path)], async () =>
        {
            await _inner.WriteAllTextAsync(area, path, content, overwrite, ct);
            return true;
        }, ct);
    }

    public async Task MoveAsync(ArtifactArea area, string sourcePath, string destinationPath, bool overwrite = false, CancellationToken ct = default)
    {
        await ExecuteFencedAsync([(area, sourcePath), (area, destinationPath)], async () =>
        {
            await _inner.MoveAsync(area, sourcePath, destinationPath, overwrite, ct);
            return true;
        }, ct);
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
        ExecuteFencedAsync([(area, path)], () => _inner.DeleteAsync(area, path, ct), ct);
    public Task<IArtifactLease> LeaseLocalCopyAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        _inner.LeaseLocalCopyAsync(area, path, ct);
}

/// <summary>Thrown when a write is rejected because a newer node has fenced this writer out (P1.8).</summary>
public sealed class FencedWriteException(string message) : IOException(message);
