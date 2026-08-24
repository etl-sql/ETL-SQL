using System.Net;
using System.Runtime.CompilerServices;
using Amazon.S3;
using Amazon.S3.Model;
using ETL_SQL.Core.Storage;

namespace ETL_SQL.Connectors.ObjectStorage;

/// <summary>Object-native artifact primitive backed by one S3 bucket and optional key prefix.</summary>
public sealed class S3ObjectStore(IAmazonS3 client, string bucket, string prefix = "") : IObjectStore
{
    public async Task<ObjectStoreItem?> GetAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var response = await client.GetObjectAsync(bucket, Key(key), ct).ConfigureAwait(false);
            var metadata = response.Metadata.Keys.ToDictionary(NormalizeMetadataKey, k => response.Metadata[k], StringComparer.OrdinalIgnoreCase);
            return new ObjectStoreItem(new ObjectStoreEntry(key, response.ETag, response.ContentLength,
                new DateTimeOffset(response.LastModified ?? DateTime.MinValue, TimeSpan.Zero), metadata), response.ResponseStream);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
    }

    public async IAsyncEnumerable<ObjectStoreEntry> ListAsync(
        string requestedPrefix, [EnumeratorCancellation] CancellationToken ct = default)
    {
        string? token = null;
        do
        {
            var response = await client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = bucket,
                Prefix = Key(requestedPrefix),
                ContinuationToken = token
            }, ct).ConfigureAwait(false);
            foreach (var item in response.S3Objects ?? [])
                yield return new(StripPrefix(item.Key), item.ETag ?? string.Empty, item.Size ?? 0,
                    new DateTimeOffset(item.LastModified ?? DateTime.MinValue, TimeSpan.Zero),
                    new Dictionary<string, string>());
            token = response.IsTruncated == true ? response.NextContinuationToken : null;
        } while (token is not null);
    }

    public async Task<ObjectStoreWriteResult> PutAsync(
        string key, Stream content, ObjectStoreWriteCondition condition,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        string? temporaryPath = null;
        try
        {
            Stream upload = content;
            if (!content.CanSeek)
            {
                temporaryPath = Path.Combine(Path.GetTempPath(), $"etlsql-s3-object-{Guid.NewGuid():N}.tmp");
                var staging = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite,
                    FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await content.CopyToAsync(staging, ct).ConfigureAwait(false);
                staging.Position = 0;
                upload = staging;
            }
            await using var ownedUpload = upload == content ? null : upload;
            var request = new PutObjectRequest
            {
                BucketName = bucket,
                Key = Key(key),
                InputStream = upload,
                AutoCloseStream = false
            };
            if (condition.IfAbsent) request.IfNoneMatch = "*";
            if (condition.IfVersion is not null) request.IfMatch = condition.IfVersion;
            if (metadata is not null) foreach (var pair in metadata) request.Metadata[pair.Key] = pair.Value;
            var response = await client.PutObjectAsync(request, ct).ConfigureAwait(false);
            return new(response.ETag);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
        { throw Failed(key, ex); }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch { }
            }
        }
    }

    public async Task<ObjectStoreWriteResult> CopyAsync(
        string sourceKey, string destinationKey, ObjectStoreWriteCondition condition,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        // Destination conditional-copy support differs across S3-compatible implementations. A
        // streamed GET + conditional PUT has the same correctness contract: the CAS object is not
        // authoritative until its separate commit record succeeds.
        var source = await GetAsync(sourceKey, ct).ConfigureAwait(false)
            ?? throw new FileNotFoundException("Source S3 object does not exist.", sourceKey);
        await using (source.ConfigureAwait(false))
            return await PutAsync(destinationKey, source.Content, condition, metadata ?? source.Entry.Metadata, ct).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string key, string? ifVersion = null, CancellationToken ct = default)
    {
        try
        {
            var existing = await GetAsync(key, ct).ConfigureAwait(false);
            if (existing is null) return false;
            await existing.DisposeAsync().ConfigureAwait(false);
            await client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = bucket,
                Key = Key(key),
                IfMatch = ifVersion
            }, ct).ConfigureAwait(false);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw Failed(key, ex);
        }
    }

    private string Key(string key)
    {
        var validated = ObjectStoreKey.Validate(key);
        return string.IsNullOrWhiteSpace(prefix) ? validated : $"{ObjectStoreKey.Validate(prefix.Trim('/'))}/{validated}";
    }
    private string StripPrefix(string key) => string.IsNullOrWhiteSpace(prefix) ? key : key[(prefix.Trim('/').Length + 1)..];
    private static string NormalizeMetadataKey(string key) =>
        key.StartsWith("x-amz-meta-", StringComparison.OrdinalIgnoreCase) ? key[11..] : key;
    private static ObjectStorePreconditionFailedException Failed(string key, Exception? ex) =>
        new($"S3 precondition failed for '{key}'{(ex is null ? "." : $": {ex.Message}")}");
}
