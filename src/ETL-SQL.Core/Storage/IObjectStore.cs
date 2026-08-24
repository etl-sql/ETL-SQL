using System.Runtime.CompilerServices;

namespace ETL_SQL.Core.Storage;

/// <summary>
/// Smallest provider-neutral object-store surface needed by object-native artifact publication.
/// Versions are opaque provider ETags/version IDs. Keys are object keys, never filesystem paths.
/// </summary>
public interface IObjectStore
{
    Task<ObjectStoreItem?> GetAsync(string key, CancellationToken ct = default);

    IAsyncEnumerable<ObjectStoreEntry> ListAsync(
        string prefix, CancellationToken ct = default);

    Task<ObjectStoreWriteResult> PutAsync(
        string key,
        Stream content,
        ObjectStoreWriteCondition condition,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default);

    Task<ObjectStoreWriteResult> CopyAsync(
        string sourceKey,
        string destinationKey,
        ObjectStoreWriteCondition condition,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default);

    Task<bool> DeleteAsync(string key, string? ifVersion = null, CancellationToken ct = default);
}

public sealed record ObjectStoreWriteCondition(string? IfVersion = null, bool IfAbsent = false)
{
    public static ObjectStoreWriteCondition Unconditional { get; } = new();
    public static ObjectStoreWriteCondition CreateOnly { get; } = new(IfAbsent: true);
    public static ObjectStoreWriteCondition Match(string version) => new(version);
}

public sealed record ObjectStoreWriteResult(string Version);

public sealed record ObjectStoreEntry(
    string Key,
    string Version,
    long Length,
    DateTimeOffset LastModified,
    IReadOnlyDictionary<string, string> Metadata);

public sealed class ObjectStoreItem : IAsyncDisposable
{
    public ObjectStoreItem(ObjectStoreEntry entry, Stream content)
    {
        Entry = entry;
        Content = content;
    }

    public ObjectStoreEntry Entry { get; }
    public Stream Content { get; }

    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public static class ObjectStoreKey
{
    public static string Validate(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key[0] is '/' or '\\' || key.Contains('\\')
            || key.Split('/', StringSplitOptions.None).Any(segment => segment is "." or ".."))
            throw new ArgumentException("Object keys must be non-empty relative keys without traversal or backslashes.", nameof(key));
        return key;
    }
}

/// <summary>Raised when an If-Match or create-only object mutation loses its race.</summary>
public sealed class ObjectStorePreconditionFailedException(string message) : IOException(message);
