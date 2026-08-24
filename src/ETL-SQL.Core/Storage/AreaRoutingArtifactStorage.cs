namespace ETL_SQL.Core.Storage;

/// <summary>Routes complete artifact areas to different providers without splitting an operation.</summary>
public sealed class AreaRoutingArtifactStorage(
    IArtifactStorage defaultStorage,
    IReadOnlyDictionary<ArtifactArea, IArtifactStorage> routes) : IArtifactStorage
{
    private IArtifactStorage For(ArtifactArea area) => routes.TryGetValue(area, out var storage) ? storage : defaultStorage;
    public Task<bool> ExistsAsync(ArtifactArea area, string path, CancellationToken ct = default) => For(area).ExistsAsync(area, path, ct);
    public Task<ArtifactInfo?> GetInfoAsync(ArtifactArea area, string path, CancellationToken ct = default) => For(area).GetInfoAsync(area, path, ct);
    public IAsyncEnumerable<ArtifactInfo> EnumerateAsync(ArtifactArea area, string? prefix = null, bool recursive = true, CancellationToken ct = default) => For(area).EnumerateAsync(area, prefix, recursive, ct);
    public Task<Stream> OpenReadAsync(ArtifactArea area, string path, CancellationToken ct = default) => For(area).OpenReadAsync(area, path, ct);
    public Task<byte[]> ReadAllBytesAsync(ArtifactArea area, string path, CancellationToken ct = default) => For(area).ReadAllBytesAsync(area, path, ct);
    public Task<string> ReadAllTextAsync(ArtifactArea area, string path, CancellationToken ct = default) => For(area).ReadAllTextAsync(area, path, ct);
    public Task WriteAsync(ArtifactArea area, string path, Stream content, bool overwrite = true, CancellationToken ct = default) => For(area).WriteAsync(area, path, content, overwrite, ct);
    public Task WriteAllBytesAsync(ArtifactArea area, string path, ReadOnlyMemory<byte> content, bool overwrite = true, CancellationToken ct = default) => For(area).WriteAllBytesAsync(area, path, content, overwrite, ct);
    public Task WriteAllTextAsync(ArtifactArea area, string path, string content, bool overwrite = true, CancellationToken ct = default) => For(area).WriteAllTextAsync(area, path, content, overwrite, ct);
    public Task<bool> DeleteAsync(ArtifactArea area, string path, CancellationToken ct = default) => For(area).DeleteAsync(area, path, ct);
    public Task MoveAsync(ArtifactArea area, string sourcePath, string destinationPath, bool overwrite = false, CancellationToken ct = default) => For(area).MoveAsync(area, sourcePath, destinationPath, overwrite, ct);
    public Task<IArtifactLease> LeaseLocalCopyAsync(ArtifactArea area, string path, CancellationToken ct = default) => For(area).LeaseLocalCopyAsync(area, path, ct);
}
