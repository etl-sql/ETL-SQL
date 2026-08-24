using System.Buffers;
using System.Security.Cryptography;
using ETL_SQL.Core.Storage;

namespace ETL_SQL.Core.Portability;

public sealed record TenantContentChunk(
    int Index, long PlaintextOffset, int PlaintextLength, string PlaintextSha256,
    string StoredSha256, long StoredLength, string ArtifactPath);

public sealed record TenantChunkedContent(
    string OperationId, string TenantId, string StableId, long TotalLength, int ChunkSize,
    string ContentSha256, IReadOnlyList<TenantContentChunk> Chunks);

/// <summary>Optional per-chunk protection. Implementations must be deterministic only on decrypt.</summary>
public interface ITenantChunkProtector
{
    Task<byte[]> ProtectAsync(ReadOnlyMemory<byte> plaintext, CancellationToken cancellationToken = default);
    Task<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedContent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resumable large-content transfer over authoritative object-native commits. Chunk paths are stable
/// for an operation and plaintext hash, so a retry verifies and reuses complete commits and replaces
/// neither a partial object nor content belonging to another operation.
/// </summary>
public static class TenantChunkTransfer
{
    public const int DefaultChunkSize = 8 * 1024 * 1024;

    public static async Task<TenantChunkedContent> ExportAsync(
        ObjectNativeArtifactStorage storage,
        string tenantId,
        string operationId,
        string stableId,
        Stream source,
        long fenceToken,
        int chunkSize = DefaultChunkSize,
        ITenantChunkProtector? protector = null,
        int maxParallelism = 4,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stableId);
        if (chunkSize < 64 * 1024) throw new ArgumentOutOfRangeException(nameof(chunkSize));
        if (maxParallelism < 1 || maxParallelism > 32) throw new ArgumentOutOfRangeException(nameof(maxParallelism));

        var safeTenant = Segment(tenantId);
        var safeOperation = Segment(operationId);
        var safeStableId = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(stableId)))
            .ToLowerInvariant();
        var chunks = new List<TenantContentChunk>();
        using var wholeHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var rented = ArrayPool<byte>.Shared.Rent(chunkSize);
        var pending = new List<Task>();
        long offset = 0;
        try
        {
            while (true)
            {
                var read = 0;
                while (read < chunkSize)
                {
                    var count = await source.ReadAsync(rented.AsMemory(read, chunkSize - read), cancellationToken)
                        .ConfigureAwait(false);
                    if (count == 0) break;
                    read += count;
                }
                if (read == 0) break;

                wholeHash.AppendData(rented, 0, read);
                var plaintextHash = Convert.ToHexString(SHA256.HashData(rented.AsSpan(0, read))).ToLowerInvariant();
                var stored = protector is null
                    ? rented.AsMemory(0, read).ToArray()
                    : await protector.ProtectAsync(rented.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                var storedHash = Convert.ToHexString(SHA256.HashData(stored)).ToLowerInvariant();
                var path = $"portability/{safeTenant}/{safeOperation}/{safeStableId}/{chunks.Count:D8}-{plaintextHash}.chunk";

                chunks.Add(new TenantContentChunk(chunks.Count, offset, read, plaintextHash,
                    storedHash, stored.LongLength, path));
                pending.Add(EnsureCommittedAsync(storage, path, stored, storedHash, fenceToken, cancellationToken));
                if (pending.Count >= maxParallelism)
                {
                    var completed = await Task.WhenAny(pending).ConfigureAwait(false);
                    pending.Remove(completed);
                    await completed.ConfigureAwait(false);
                }
                offset += read;
                if (read < chunkSize) break;
            }
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
        catch
        {
            try { await Task.WhenAll(pending).ConfigureAwait(false); } catch { }
            throw;
        }
        finally { ArrayPool<byte>.Shared.Return(rented, clearArray: true); }

        return new TenantChunkedContent(operationId, tenantId, stableId, offset, chunkSize,
            Convert.ToHexString(wholeHash.GetHashAndReset()).ToLowerInvariant(), chunks);
    }

    public static async Task ImportAsync(
        ObjectNativeArtifactStorage storage,
        TenantChunkedContent content,
        Stream destination,
        ITenantChunkProtector? protector = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(destination);
        using var wholeHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long offset = 0;
        foreach (var chunk in content.Chunks.OrderBy(x => x.Index))
        {
            if (chunk.Index < 0 || chunk.PlaintextOffset != offset)
                throw new InvalidDataException("Chunk indexes or offsets are not contiguous.");
            await using var read = await storage.OpenReadAsync(ArtifactArea.Datasets, chunk.ArtifactPath, cancellationToken)
                .ConfigureAwait(false) ?? throw new InvalidDataException($"Missing chunk '{chunk.ArtifactPath}'.");
            using var buffer = new MemoryStream();
            await read.Content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            var stored = buffer.ToArray();
            var storedHash = Convert.ToHexString(SHA256.HashData(stored)).ToLowerInvariant();
            if (stored.LongLength != chunk.StoredLength || !storedHash.Equals(chunk.StoredSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Stored chunk {chunk.Index} does not match its index.");
            var plaintext = protector is null ? stored
                : await protector.UnprotectAsync(stored, cancellationToken).ConfigureAwait(false);
            var plaintextHash = Convert.ToHexString(SHA256.HashData(plaintext)).ToLowerInvariant();
            if (plaintext.Length != chunk.PlaintextLength || !plaintextHash.Equals(chunk.PlaintextSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Plaintext chunk {chunk.Index} does not match its index.");
            wholeHash.AppendData(plaintext);
            await destination.WriteAsync(plaintext, cancellationToken).ConfigureAwait(false);
            offset += plaintext.Length;
        }
        var fullHash = Convert.ToHexString(wholeHash.GetHashAndReset()).ToLowerInvariant();
        if (offset != content.TotalLength || !fullHash.Equals(content.ContentSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Reassembled content does not match its declared length and hash.");
    }

    private static string Segment(string value)
    {
        if (value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')) || value is "." or "..")
            throw new ArgumentException($"'{value}' is not a safe object-key segment.");
        return value;
    }

    private static async Task EnsureCommittedAsync(ObjectNativeArtifactStorage storage, string path,
        byte[] stored, string storedHash, long fenceToken, CancellationToken cancellationToken)
    {
        var existing = await storage.OpenReadAsync(ArtifactArea.Datasets, path, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            await using (existing.ConfigureAwait(false))
            {
                if (!string.Equals(existing.Commit.ContentHash, storedHash, StringComparison.OrdinalIgnoreCase)
                    || existing.Commit.Length != stored.LongLength)
                    throw new InvalidDataException($"Resume chunk '{path}' conflicts with already committed content.");
            }
            return;
        }
        await using var payload = new MemoryStream(stored, writable: false);
        await storage.PublishAsync(ArtifactArea.Datasets, path, payload, fenceToken,
            overwrite: false, contentType: "application/x-etlsql-portability-chunk", ct: cancellationToken)
            .ConfigureAwait(false);
    }
}
