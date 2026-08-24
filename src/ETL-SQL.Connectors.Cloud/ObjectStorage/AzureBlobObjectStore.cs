using System.Runtime.CompilerServices;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ETL_SQL.Core.Storage;

namespace ETL_SQL.Connectors.ObjectStorage;

/// <summary>Object-native artifact primitive backed by one Azure Blob container.</summary>
public sealed class AzureBlobObjectStore(BlobContainerClient container) : IObjectStore
{
    public async Task<ObjectStoreItem?> GetAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var response = await container.GetBlobClient(ObjectStoreKey.Validate(key)).DownloadStreamingAsync(cancellationToken: ct).ConfigureAwait(false);
            var details = response.Value.Details;
            return new ObjectStoreItem(new ObjectStoreEntry(key, details.ETag.ToString(), details.ContentLength,
                details.LastModified, new Dictionary<string, string>(details.Metadata, StringComparer.OrdinalIgnoreCase)), response.Value.Content);
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { return null; }
    }

    public async IAsyncEnumerable<ObjectStoreEntry> ListAsync(
        string prefix, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var blob in container.GetBlobsAsync(BlobTraits.Metadata, BlobStates.None, ObjectStoreKey.Validate(prefix), ct).ConfigureAwait(false))
            yield return new(blob.Name, blob.Properties.ETag?.ToString() ?? string.Empty,
                blob.Properties.ContentLength ?? 0, blob.Properties.LastModified ?? DateTimeOffset.MinValue,
                new Dictionary<string, string>(blob.Metadata, StringComparer.OrdinalIgnoreCase));
    }

    public async Task<ObjectStoreWriteResult> PutAsync(
        string key, Stream content, ObjectStoreWriteCondition condition,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        try
        {
            var response = await container.GetBlobClient(ObjectStoreKey.Validate(key)).UploadAsync(content, new BlobUploadOptions
            {
                Conditions = Conditions(condition),
                Metadata = metadata is null ? null : new Dictionary<string, string>(metadata)
            }, ct).ConfigureAwait(false);
            return new(response.Value.ETag.ToString());
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412) { throw Failed(key, ex); }
    }

    public async Task<ObjectStoreWriteResult> CopyAsync(
        string sourceKey, string destinationKey, ObjectStoreWriteCondition condition,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        var source = await GetAsync(sourceKey, ct).ConfigureAwait(false)
            ?? throw new FileNotFoundException("Source blob does not exist.", sourceKey);
        await using (source.ConfigureAwait(false))
            return await PutAsync(destinationKey, source.Content, condition, metadata ?? source.Entry.Metadata, ct).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string key, string? ifVersion = null, CancellationToken ct = default)
    {
        try
        {
            var response = await container.GetBlobClient(ObjectStoreKey.Validate(key)).DeleteIfExistsAsync(
                conditions: ifVersion is null ? null : new BlobRequestConditions { IfMatch = new ETag(ifVersion) },
                cancellationToken: ct).ConfigureAwait(false);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 412) { throw Failed(key, ex); }
    }

    private static BlobRequestConditions Conditions(ObjectStoreWriteCondition condition) => new()
    {
        IfMatch = condition.IfVersion is null ? null : new ETag(condition.IfVersion),
        IfNoneMatch = condition.IfAbsent ? ETag.All : null
    };

    private static ObjectStorePreconditionFailedException Failed(string key, Exception inner) =>
        new($"Azure Blob precondition failed for '{key}': {inner.Message}");
}
