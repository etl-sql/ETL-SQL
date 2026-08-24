using System.Text;

namespace ETL_SQL.Core.Storage;

/// <summary>
/// Adapts object-native publication to shared artifact consumers that use reads, direct writes,
/// enumeration, deletion, and local-copy leases. Atomic rename is intentionally unsupported because
/// an object provider cannot satisfy that filesystem contract.
/// </summary>
public sealed class ObjectNativeArtifactStorageAdapter(
    ObjectNativeArtifactStorage storage,
    Func<long>? currentFenceToken = null) : IArtifactStorage
{
    public async Task<bool> ExistsAsync(ArtifactArea area, string path, CancellationToken ct = default)
    {
        await using var read = await storage.OpenReadAsync(area, path, ct).ConfigureAwait(false);
        return read is not null;
    }

    public async Task<ArtifactInfo?> GetInfoAsync(ArtifactArea area, string path, CancellationToken ct = default)
    {
        await using var read = await storage.OpenReadAsync(area, path, ct).ConfigureAwait(false);
        return read is null ? null : new ArtifactInfo(read.Commit.Path, read.Commit.Length, read.Commit.CommittedAt);
    }

    public async IAsyncEnumerable<ArtifactInfo> EnumerateAsync(
        ArtifactArea area, string? prefix = null, bool recursive = true,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var normalizedPrefix = prefix is null ? null : ArtifactPath.Normalize(prefix).TrimEnd('/');
        await foreach (var commit in storage.EnumerateCommitsAsync(area, prefix, ct).ConfigureAwait(false))
        {
            if (!recursive && normalizedPrefix is not null
                && commit.Path[(normalizedPrefix.Length + 1)..].Contains('/'))
                continue;
            yield return new ArtifactInfo(commit.Path, commit.Length, commit.CommittedAt);
        }
    }

    public async Task<Stream> OpenReadAsync(ArtifactArea area, string path, CancellationToken ct = default)
    {
        var read = await storage.OpenReadAsync(area, path, ct).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"Artifact '{area}/{path}' does not exist.", path);
        return new ReadLeaseStream(read);
    }

    public async Task<byte[]> ReadAllBytesAsync(ArtifactArea area, string path, CancellationToken ct = default)
    {
        await using var stream = await OpenReadAsync(area, path, ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
        return buffer.ToArray();
    }

    public async Task<string> ReadAllTextAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        Encoding.UTF8.GetString(await ReadAllBytesAsync(area, path, ct).ConfigureAwait(false));

    public Task WriteAsync(ArtifactArea area, string path, Stream content, bool overwrite = true, CancellationToken ct = default) =>
        storage.PublishAsync(area, path, content, currentFenceToken?.Invoke() ?? 0, overwrite, ct: ct);

    public Task WriteAllBytesAsync(ArtifactArea area, string path, ReadOnlyMemory<byte> content, bool overwrite = true, CancellationToken ct = default) =>
        WriteAsync(area, path, new MemoryStream(content.ToArray(), writable: false), overwrite, ct);

    public Task WriteAllTextAsync(ArtifactArea area, string path, string content, bool overwrite = true, CancellationToken ct = default) =>
        WriteAllBytesAsync(area, path, Encoding.UTF8.GetBytes(content), overwrite, ct);

    public Task<bool> DeleteAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        storage.DeleteAsync(area, path, currentFenceToken?.Invoke() ?? 0, ct);

    public Task MoveAsync(ArtifactArea area, string sourcePath, string destinationPath, bool overwrite = false, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "Object-native storage does not emulate atomic rename with copy/delete. Publish directly to the destination key.");

    public async Task<IArtifactLease> LeaseLocalCopyAsync(ArtifactArea area, string path, CancellationToken ct = default)
    {
        if (area == ArtifactArea.Keys) throw new InvalidOperationException("Key artifacts cannot be leased to local disk.");
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"etlsql-object-lease-{Guid.NewGuid():N}.tmp");
        try
        {
            await using var source = await OpenReadAsync(area, path, ct).ConfigureAwait(false);
            await using var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(destination, ct).ConfigureAwait(false);
            return new TemporaryLease(temporaryPath);
        }
        catch { try { File.Delete(temporaryPath); } catch { } throw; }
    }

    private sealed class ReadLeaseStream(ObjectArtifactRead lease) : Stream
    {
        private Stream Inner => lease.Content;
        public override bool CanRead => Inner.CanRead; public override bool CanSeek => Inner.CanSeek; public override bool CanWrite => false;
        public override long Length => Inner.Length; public override long Position { get => Inner.Position; set => Inner.Position = value; }
        public override void Flush() => Inner.Flush(); public override int Read(byte[] b, int o, int c) => Inner.Read(b, o, c);
        public override ValueTask<int> ReadAsync(Memory<byte> b, CancellationToken ct = default) => Inner.ReadAsync(b, ct);
        public override long Seek(long o, SeekOrigin so) => Inner.Seek(o, so); public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) Inner.Dispose(); base.Dispose(disposing); }
        public override async ValueTask DisposeAsync() { await lease.DisposeAsync().ConfigureAwait(false); GC.SuppressFinalize(this); }
    }

    private sealed class TemporaryLease(string path) : IArtifactLease
    {
        public string LocalPath { get; } = path;
        public ValueTask DisposeAsync() { try { File.Delete(LocalPath); } catch { } return ValueTask.CompletedTask; }
    }
}
