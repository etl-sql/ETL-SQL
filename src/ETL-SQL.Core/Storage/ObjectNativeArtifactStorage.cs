using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Core.Storage;

/// <summary>
/// Publishes logical artifacts without rename semantics. Bytes first enter a unique staging key,
/// are copied to an immutable SHA-256 key, and become visible only through a conditionally replaced
/// commit record after the database fencing authority accepts the writer's token.
/// </summary>
public sealed class ObjectNativeArtifactStorage
{
    private const string Root = "etlsql/v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IObjectStore _objects;
    private readonly IWriteEpochStore _epochs;
    private readonly IClusterLockStore? _locks;
    private readonly string _scope;

    public ObjectNativeArtifactStorage(IObjectStore objects, IWriteEpochStore epochs, string scope = "object-artifact")
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _epochs = epochs ?? throw new ArgumentNullException(nameof(epochs));
        _scope = string.IsNullOrWhiteSpace(scope) ? throw new ArgumentException("Scope is required.", nameof(scope)) : scope;
    }

    /// <summary>
    /// Production HA constructor. The database lock closes the cross-system window between checking
    /// the epoch and conditionally publishing the object-store commit record.
    /// </summary>
    public ObjectNativeArtifactStorage(
        IObjectStore objects, IWriteEpochStore epochs, IClusterLockStore locks, string scope = "object-artifact")
        : this(objects, epochs, scope) => _locks = locks ?? throw new ArgumentNullException(nameof(locks));

    public async Task<ObjectArtifactCommit> PublishAsync(
        ArtifactArea area,
        string path,
        Stream content,
        long fenceToken,
        bool overwrite = true,
        string? contentType = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (fenceToken < 0 || (fenceToken == 0 && _locks is null))
            throw new ArgumentOutOfRangeException(nameof(fenceToken), "A positive external fence is required without database mutation locks.");
        var logicalPath = ArtifactPath.Normalize(path);
        var operationId = Guid.NewGuid().ToString("N");
        var stagingKey = $"{Root}/staging/{operationId}";
        var commitKey = CommitKey(area, logicalPath);
        var epochKey = $"{area}/{logicalPath}";
        string? hash = null;
        long length = 0;

        try
        {
            using var hashing = new HashingReadStream(content, bytes => length += bytes);
            await _objects.PutAsync(stagingKey, hashing, ObjectStoreWriteCondition.CreateOnly,
                new Dictionary<string, string> { ["operation_id"] = operationId }, ct).ConfigureAwait(false);
            hash = Convert.ToHexString(hashing.GetHash()).ToLowerInvariant();
            var objectKey = $"{Root}/objects/sha256/{hash[..2]}/{hash}";

            try
            {
                await _objects.CopyAsync(stagingKey, objectKey, ObjectStoreWriteCondition.CreateOnly,
                    new Dictionary<string, string> { ["sha256"] = hash }, ct).ConfigureAwait(false);
            }
            catch (ObjectStorePreconditionFailedException)
            {
                var existing = await _objects.GetAsync(objectKey, ct).ConfigureAwait(false);
                if (existing is null || !existing.Entry.Metadata.TryGetValue("sha256", out var existingHash)
                    || !string.Equals(existingHash, hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Immutable object collision or corrupt metadata at '{objectKey}'.");
                await existing.DisposeAsync().ConfigureAwait(false);
            }

            return await WithMutationLockAsync(epochKey, async () =>
            {
                if (fenceToken == 0)
                {
                    var currentEpoch = await _epochs.GetWriteEpochAsync(_scope, epochKey).ConfigureAwait(false);
                    if (currentEpoch == long.MaxValue) throw new FencedWriteException($"Fence space is exhausted for '{epochKey}'.");
                    fenceToken = currentEpoch + 1;
                }
                if (!await _epochs.TryClaimWriteEpochAsync(_scope, epochKey, fenceToken).ConfigureAwait(false))
                    throw new FencedWriteException($"Object artifact write for '{epochKey}' was rejected at fence {fenceToken}.");

                for (var attempt = 0; attempt < 8; attempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    var prior = await ReadCommitItemAsync(commitKey, ct).ConfigureAwait(false);
                    if (prior.Commit is not null && prior.Commit.FenceToken > fenceToken)
                        throw new FencedWriteException($"Object artifact '{epochKey}' is already committed at newer fence {prior.Commit.FenceToken}.");
                    if (!overwrite && prior.Commit is not null)
                        throw new IOException($"Artifact '{area}/{logicalPath}' already exists.");

                    var commit = new ObjectArtifactCommit(
                        area, logicalPath, hash, objectKey, length, contentType, fenceToken, operationId, DateTimeOffset.UtcNow);
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(commit, JsonOptions);
                    try
                    {
                        await using var payload = new MemoryStream(bytes, writable: false);
                        var condition = prior.Version is null
                            ? ObjectStoreWriteCondition.CreateOnly
                            : ObjectStoreWriteCondition.Match(prior.Version);
                        await _objects.PutAsync(commitKey, payload, condition,
                            new Dictionary<string, string> { ["sha256"] = hash, ["fence"] = fenceToken.ToString() }, ct)
                            .ConfigureAwait(false);
                        return commit;
                    }
                    catch (ObjectStorePreconditionFailedException) when (attempt < 7)
                    {
                        // Another writer changed the authority record. Re-read it and either fence
                        // this writer or retry against its new opaque version.
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // A timed-out PUT may have committed. Reconcile by operation id before
                        // surfacing the ambiguous provider response.
                        var observed = await ReadCommitItemAsync(commitKey, ct).ConfigureAwait(false);
                        if (observed.Commit?.OperationId == operationId && observed.Commit.ContentHash == hash)
                            return observed.Commit;
                        throw;
                    }
                }
                throw new IOException($"Artifact commit for '{epochKey}' did not converge after concurrent updates.");
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            try { await _objects.DeleteAsync(stagingKey, ct: CancellationToken.None).ConfigureAwait(false); }
            catch { /* GC owns abandoned staging after an outage. */ }
        }
    }

    public async Task<ObjectArtifactRead?> OpenReadAsync(
        ArtifactArea area, string path, CancellationToken ct = default)
    {
        var commit = (await ReadCommitItemAsync(CommitKey(area, ArtifactPath.Normalize(path)), ct).ConfigureAwait(false)).Commit;
        if (commit is null) return null;
        var item = await _objects.GetAsync(commit.ObjectKey, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Commit for '{area}/{path}' references missing object '{commit.ObjectKey}'.");
        return new ObjectArtifactRead(commit, item);
    }

    public async Task<bool> DeleteAsync(ArtifactArea area, string path, long fenceToken, CancellationToken ct = default)
    {
        var logicalPath = ArtifactPath.Normalize(path);
        var epochKey = $"{area}/{logicalPath}";
        return await WithMutationLockAsync(epochKey, async () =>
        {
            if (fenceToken == 0)
            {
                var currentEpoch = await _epochs.GetWriteEpochAsync(_scope, epochKey).ConfigureAwait(false);
                if (currentEpoch == long.MaxValue) throw new FencedWriteException($"Fence space is exhausted for '{epochKey}'.");
                fenceToken = currentEpoch + 1;
            }
            if (!await _epochs.TryClaimWriteEpochAsync(_scope, epochKey, fenceToken).ConfigureAwait(false))
                throw new FencedWriteException($"Object artifact delete for '{epochKey}' was rejected at fence {fenceToken}.");
            var current = await ReadCommitItemAsync(CommitKey(area, logicalPath), ct).ConfigureAwait(false);
            if (current.Commit is null) return false;
            if (current.Commit.FenceToken > fenceToken)
                throw new FencedWriteException($"Object artifact '{epochKey}' is committed at newer fence {current.Commit.FenceToken}.");
            return await _objects.DeleteAsync(CommitKey(area, logicalPath), current.Version, ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    private async Task<T> WithMutationLockAsync<T>(string epochKey, Func<Task<T>> action, CancellationToken ct)
    {
        if (_locks is null) return await action().ConfigureAwait(false);
        var lockName = "object-artifact:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{_scope}:{epochKey}"))).ToLowerInvariant();
        var owner = Guid.NewGuid().ToString("N");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (!await _locks.TryAcquireLockAsync(lockName, owner, TimeSpan.FromMinutes(5)).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            if (DateTimeOffset.UtcNow >= deadline)
                throw new FencedWriteException($"Timed out waiting for object artifact mutation lock '{lockName}'.");
            await Task.Delay(TimeSpan.FromMilliseconds(100), ct).ConfigureAwait(false);
        }
        try { return await action().ConfigureAwait(false); }
        finally { await _locks.ReleaseLockAsync(lockName, owner).ConfigureAwait(false); }
    }

    public async Task<ObjectArtifactReconciliationResult> ReconcileAsync(
        TimeSpan stagingRetention, DateTimeOffset now, CancellationToken ct = default)
    {
        if (stagingRetention < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(stagingRetention));
        var missing = new List<string>();
        var corrupt = new List<string>();
        var deletedStaging = 0;

        await foreach (var entry in _objects.ListAsync($"{Root}/commits/", ct).ConfigureAwait(false))
        {
            (ObjectArtifactCommit? Commit, string? Version) current;
            try { current = await ReadCommitItemAsync(entry.Key, ct).ConfigureAwait(false); }
            catch (InvalidDataException) { corrupt.Add(entry.Key); continue; }
            if (current.Commit is null) { corrupt.Add(entry.Key); continue; }
            var payload = await _objects.GetAsync(current.Commit.ObjectKey, ct).ConfigureAwait(false);
            if (payload is null) { missing.Add(entry.Key); continue; }
            var actualHash = await SHA256.HashDataAsync(payload.Content, ct).ConfigureAwait(false);
            var actualHashText = Convert.ToHexString(actualHash).ToLowerInvariant();
            if (payload.Entry.Length != current.Commit.Length
                || !payload.Entry.Metadata.TryGetValue("sha256", out var storedHash)
                || !string.Equals(storedHash, current.Commit.ContentHash, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(actualHashText, current.Commit.ContentHash, StringComparison.OrdinalIgnoreCase))
                corrupt.Add(entry.Key);
            await payload.DisposeAsync().ConfigureAwait(false);
        }

        await foreach (var entry in _objects.ListAsync($"{Root}/staging/", ct).ConfigureAwait(false))
        {
            if (now - entry.LastModified >= stagingRetention
                && await _objects.DeleteAsync(entry.Key, entry.Version, ct).ConfigureAwait(false))
                deletedStaging++;
        }
        return new ObjectArtifactReconciliationResult(missing, corrupt, deletedStaging);
    }

    public async IAsyncEnumerable<ObjectArtifactCommit> EnumerateCommitsAsync(
        ArtifactArea area,
        string? prefix = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var normalizedPrefix = prefix is null ? null : ArtifactPath.Normalize(prefix);
        await foreach (var entry in _objects.ListAsync($"{Root}/commits/{area}/", ct).ConfigureAwait(false))
        {
            var commit = (await ReadCommitItemAsync(entry.Key, ct).ConfigureAwait(false)).Commit;
            if (commit is not null && (normalizedPrefix is null
                || commit.Path.StartsWith(normalizedPrefix.TrimEnd('/') + "/", StringComparison.Ordinal)
                || string.Equals(commit.Path, normalizedPrefix, StringComparison.Ordinal)))
                yield return commit;
        }
    }

    private async Task<(ObjectArtifactCommit? Commit, string? Version)> ReadCommitItemAsync(
        string key, CancellationToken ct)
    {
        var item = await _objects.GetAsync(key, ct).ConfigureAwait(false);
        if (item is null) return (null, null);
        await using (item.ConfigureAwait(false))
        {
            try
            {
                var commit = await JsonSerializer.DeserializeAsync<ObjectArtifactCommit>(item.Content, JsonOptions, ct)
                    .ConfigureAwait(false);
                return (commit ?? throw new InvalidDataException($"Empty commit record '{key}'."), item.Entry.Version);
            }
            catch (JsonException ex) { throw new InvalidDataException($"Invalid commit record '{key}'.", ex); }
        }
    }

    private static string CommitKey(ArtifactArea area, string path) =>
        $"{Root}/commits/{area}/{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path))).ToLowerInvariant()}";

    private sealed class HashingReadStream(Stream inner, Action<int> count) : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        public byte[] GetHash() => _hash.GetHashAndReset();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var read = await inner.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read > 0) { _hash.AppendData(buffer.Span[..read]); count(read); }
            return read;
        }
        public override int Read(byte[] buffer, int offset, int countValue)
        {
            var read = inner.Read(buffer, offset, countValue);
            if (read > 0) { _hash.AppendData(buffer, offset, read); count(read); }
            return read;
        }
        protected override void Dispose(bool disposing) { if (disposing) _hash.Dispose(); base.Dispose(disposing); }
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int countValue) => throw new NotSupportedException();
    }
}

public sealed record ObjectArtifactCommit(
    ArtifactArea Area,
    string Path,
    string ContentHash,
    string ObjectKey,
    long Length,
    string? ContentType,
    long FenceToken,
    string OperationId,
    DateTimeOffset CommittedAt);

public sealed class ObjectArtifactRead(ObjectArtifactCommit commit, ObjectStoreItem item) : IAsyncDisposable
{
    public ObjectArtifactCommit Commit { get; } = commit;
    public Stream Content => item.Content;
    public ValueTask DisposeAsync() => item.DisposeAsync();
}

public sealed record ObjectArtifactReconciliationResult(
    IReadOnlyList<string> MissingObjects,
    IReadOnlyList<string> CorruptObjects,
    int DeletedStagingObjects)
{
    public bool IsHealthy => MissingObjects.Count == 0 && CorruptObjects.Count == 0;
}
