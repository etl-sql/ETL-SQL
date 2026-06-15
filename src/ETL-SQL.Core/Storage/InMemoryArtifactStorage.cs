using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Storage;

/// <summary>
/// An in-memory <see cref="IArtifactStorage"/> reference implementation: the contract's executable
/// specification and the test double for code that depends on storage. It is the canonical behavior
/// the filesystem providers (P1.5) must match — atomic writes, area isolation, traversal rejection.
/// </summary>
public sealed class InMemoryArtifactStorage : IArtifactStorage
{
    private sealed record Entry(byte[] Data, DateTimeOffset LastModifiedUtc);

    // Key = "<area>/<normalized-path>"; the dictionary swap on write is what makes writes atomic.
    private readonly ConcurrentDictionary<string, Entry> _store = new(StringComparer.Ordinal);

    private static string Key(ArtifactArea area, string path) => $"{area}/{ArtifactPath.Normalize(path)}";

    public Task<bool> ExistsAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        Task.FromResult(_store.ContainsKey(Key(area, path)));

    public Task<ArtifactInfo?> GetInfoAsync(ArtifactArea area, string path, CancellationToken ct = default)
    {
        var normalized = ArtifactPath.Normalize(path);
        return Task.FromResult(_store.TryGetValue($"{area}/{normalized}", out var e)
            ? new ArtifactInfo(normalized, e.Data.LongLength, e.LastModifiedUtc)
            : (ArtifactInfo?)null);
    }

    public async IAsyncEnumerable<ArtifactInfo> EnumerateAsync(
        ArtifactArea area, string? prefix = null, bool recursive = true,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        var areaPrefix = $"{area}/";
        var rel = string.IsNullOrWhiteSpace(prefix) ? "" : ArtifactPath.Normalize(prefix) + "/";

        foreach (var kvp in _store)
        {
            ct.ThrowIfCancellationRequested();
            if (!kvp.Key.StartsWith(areaPrefix, StringComparison.Ordinal)) continue;

            var path = kvp.Key[areaPrefix.Length..];
            if (rel.Length > 0 && !path.StartsWith(rel, StringComparison.Ordinal)) continue;
            // Non-recursive: skip artifacts nested below the (prefix-relative) level.
            if (!recursive && path[rel.Length..].Contains('/')) continue;

            yield return new ArtifactInfo(path, kvp.Value.Data.LongLength, kvp.Value.LastModifiedUtc);
        }
    }

    public async Task<Stream> OpenReadAsync(ArtifactArea area, string path, CancellationToken ct = default)
    {
        await Task.CompletedTask;
        return new MemoryStream(Get(area, path).Data, writable: false);
    }

    public Task<byte[]> ReadAllBytesAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        // Copy so a caller cannot mutate stored content.
        Task.FromResult((byte[])Get(area, path).Data.Clone());

    public Task<string> ReadAllTextAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        Task.FromResult(Encoding.UTF8.GetString(Get(area, path).Data));

    public async Task WriteAsync(
        ArtifactArea area, string path, Stream content, bool overwrite = true, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        await WriteAllBytesAsync(area, path, buffer.ToArray(), overwrite, ct);
    }

    public Task WriteAllBytesAsync(
        ArtifactArea area, string path, ReadOnlyMemory<byte> content, bool overwrite = true, CancellationToken ct = default)
    {
        var key = Key(area, path);
        var entry = new Entry(content.ToArray(), DateTimeOffset.UtcNow);
        if (overwrite)
        {
            _store[key] = entry;
        }
        else if (!_store.TryAdd(key, entry))
        {
            throw new IOException($"Artifact already exists: {area}/{ArtifactPath.Normalize(path)}");
        }
        return Task.CompletedTask;
    }

    public Task WriteAllTextAsync(
        ArtifactArea area, string path, string content, bool overwrite = true, CancellationToken ct = default) =>
        WriteAllBytesAsync(area, path, Encoding.UTF8.GetBytes(content), overwrite, ct);

    public Task<bool> DeleteAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        Task.FromResult(_store.TryRemove(Key(area, path), out _));

    public Task MoveAsync(
        ArtifactArea area, string sourcePath, string destinationPath, bool overwrite = false, CancellationToken ct = default)
    {
        var srcKey = Key(area, sourcePath);
        var dstKey = Key(area, destinationPath);
        if (!_store.TryGetValue(srcKey, out var entry))
            throw new FileNotFoundException($"Artifact not found: {area}/{ArtifactPath.Normalize(sourcePath)}");

        if (!overwrite && _store.ContainsKey(dstKey))
            throw new IOException($"Destination already exists: {area}/{ArtifactPath.Normalize(destinationPath)}");

        _store[dstKey] = entry;
        _store.TryRemove(srcKey, out _);
        return Task.CompletedTask;
    }

    public async Task<IArtifactLease> LeaseLocalCopyAsync(ArtifactArea area, string path, CancellationToken ct = default)
    {
        if (area == ArtifactArea.Keys)
            throw new InvalidOperationException("Key material cannot be leased to a local file.");

        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"etlsql-artifact-{Guid.NewGuid():N}.tmp");
        await File.WriteAllBytesAsync(temp, Get(area, path).Data, ct);
        return new TempFileLease(temp);
    }

    private Entry Get(ArtifactArea area, string path) =>
        _store.TryGetValue(Key(area, path), out var e)
            ? e
            : throw new FileNotFoundException($"Artifact not found: {area}/{ArtifactPath.Normalize(path)}");

    private sealed class TempFileLease(string localPath) : IArtifactLease
    {
        public string LocalPath { get; } = localPath;

        public ValueTask DisposeAsync()
        {
            try { if (File.Exists(LocalPath)) File.Delete(LocalPath); } catch { /* best-effort cleanup */ }
            return ValueTask.CompletedTask;
        }
    }
}
