using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Azure.Storage.Blobs;
using ETL_SQL.Core.Storage;

namespace ETL_SQL.Connectors.ObjectStorage;

public static class ObjectStoreProviderFactory
{
    public static IObjectStore CreateS3(
        string bucket,
        string? region = null,
        string? serviceUrl = null,
        bool forcePathStyle = false,
        string? accessKey = null,
        string? secretKey = null,
        string? prefix = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucket);
        if (string.IsNullOrWhiteSpace(accessKey) != string.IsNullOrWhiteSpace(secretKey))
            throw new ArgumentException("S3 access key and secret key must be supplied together.");
        var config = new AmazonS3Config { ForcePathStyle = forcePathStyle };
        if (!string.IsNullOrWhiteSpace(serviceUrl)) config.ServiceURL = serviceUrl;
        else config.RegionEndpoint = RegionEndpoint.GetBySystemName(region ?? "us-east-1");
        IAmazonS3 client = string.IsNullOrWhiteSpace(accessKey)
            ? new AmazonS3Client(config)
            : new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), config);
        return new S3ObjectStore(client, bucket, prefix ?? string.Empty);
    }

    public static IObjectStore CreateAzureBlob(string connectionString, string container, string? prefix = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(container);
        var client = new BlobContainerClient(connectionString, container);
        return string.IsNullOrWhiteSpace(prefix)
            ? new AzureBlobObjectStore(client)
            : new PrefixedObjectStore(new AzureBlobObjectStore(client), prefix);
    }

    private sealed class PrefixedObjectStore(IObjectStore inner, string prefix) : IObjectStore
    {
        private readonly string _prefix = ObjectStoreKey.Validate(prefix.Trim('/'));
        private string Key(string key) => $"{_prefix}/{ObjectStoreKey.Validate(key)}";
        private string Strip(string key) => key[(_prefix.Length + 1)..];
        public Task<ObjectStoreItem?> GetAsync(string key, CancellationToken ct = default) => inner.GetAsync(Key(key), ct);
        public async IAsyncEnumerable<ObjectStoreEntry> ListAsync(string requestedPrefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var item in inner.ListAsync(Key(requestedPrefix), ct).ConfigureAwait(false))
                yield return item with { Key = Strip(item.Key) };
        }
        public Task<ObjectStoreWriteResult> PutAsync(string key, Stream content, ObjectStoreWriteCondition condition, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default) => inner.PutAsync(Key(key), content, condition, metadata, ct);
        public Task<ObjectStoreWriteResult> CopyAsync(string sourceKey, string destinationKey, ObjectStoreWriteCondition condition, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default) => inner.CopyAsync(Key(sourceKey), Key(destinationKey), condition, metadata, ct);
        public Task<bool> DeleteAsync(string key, string? ifVersion = null, CancellationToken ct = default) => inner.DeleteAsync(Key(key), ifVersion, ct);
    }
}
