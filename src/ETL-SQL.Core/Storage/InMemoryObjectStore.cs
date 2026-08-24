using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace ETL_SQL.Core.Storage;

/// <summary>Strongly consistent object store used by the provider-neutral hostile contract suite.</summary>
public sealed class InMemoryObjectStore : IObjectStore
{
    private sealed record Stored(byte[] Content, string Version, DateTimeOffset Modified, IReadOnlyDictionary<string, string> Metadata);
    private readonly ConcurrentDictionary<string, Stored> _items = new(StringComparer.Ordinal);
    private long _version;

    public Task<ObjectStoreItem?> GetAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!_items.TryGetValue(ValidateKey(key), out var value)) return Task.FromResult<ObjectStoreItem?>(null);
        return Task.FromResult<ObjectStoreItem?>(new ObjectStoreItem(ToEntry(key, value), new MemoryStream(value.Content, writable: false)));
    }

    public async IAsyncEnumerable<ObjectStoreEntry> ListAsync(
        string prefix, [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var pair in _items.Where(x => x.Key.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            yield return ToEntry(pair.Key, pair.Value);
            await Task.Yield();
        }
    }

    public async Task<ObjectStoreWriteResult> PutAsync(
        string key, Stream content, ObjectStoreWriteCondition condition,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        ValidateKey(key);
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct).ConfigureAwait(false);
        var stored = NewStored(buffer.ToArray(), metadata);
        while (true)
        {
            if (!_items.TryGetValue(key, out var current))
            {
                if (condition.IfVersion is not null) throw Failed(key);
                if (_items.TryAdd(key, stored)) return new(stored.Version);
                continue;
            }
            if (condition.IfAbsent || (condition.IfVersion is not null && current.Version != condition.IfVersion))
                throw Failed(key);
            if (_items.TryUpdate(key, stored, current)) return new(stored.Version);
        }
    }

    public async Task<ObjectStoreWriteResult> CopyAsync(
        string sourceKey, string destinationKey, ObjectStoreWriteCondition condition,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        var source = await GetAsync(sourceKey, ct).ConfigureAwait(false)
            ?? throw new FileNotFoundException("Source object does not exist.", sourceKey);
        await using (source.ConfigureAwait(false))
            return await PutAsync(destinationKey, source.Content, condition, metadata ?? source.Entry.Metadata, ct).ConfigureAwait(false);
    }

    public Task<bool> DeleteAsync(string key, string? ifVersion = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        while (_items.TryGetValue(ValidateKey(key), out var current))
        {
            if (ifVersion is not null && current.Version != ifVersion) throw Failed(key);
            if (_items.TryRemove(new KeyValuePair<string, Stored>(key, current))) return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    private Stored NewStored(byte[] bytes, IReadOnlyDictionary<string, string>? metadata) =>
        new(bytes, Interlocked.Increment(ref _version).ToString(), DateTimeOffset.UtcNow,
            metadata is null ? new Dictionary<string, string>() : new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase));
    private static ObjectStoreEntry ToEntry(string key, Stored value) =>
        new(key, value.Version, value.Content.LongLength, value.Modified, value.Metadata);
    private static ObjectStorePreconditionFailedException Failed(string key) => new($"Object precondition failed for '{key}'.");
    private static string ValidateKey(string key) =>
        string.IsNullOrWhiteSpace(key) || key.StartsWith('/') || key.Contains("..", StringComparison.Ordinal)
            ? throw new ArgumentException("Object keys must be non-empty relative keys without traversal.", nameof(key))
            : key;
}
