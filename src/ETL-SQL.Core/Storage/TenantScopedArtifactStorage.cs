using System.Runtime.CompilerServices;
using ETL_SQL.Core.Multitenancy;

namespace ETL_SQL.Core.Storage;

/// <summary>
/// Transparently places every logical artifact key below a server-derived tenant prefix. Callers
/// continue to use area-relative keys and therefore cannot select or escape the physical tenant
/// namespace.
/// </summary>
public sealed class TenantScopedArtifactStorage(
    IArtifactStorage inner,
    TenantContext tenant,
    bool requireExclusiveBackend = true) : IArtifactStorage
{
    private readonly string _prefix = tenant.Tenant.Value + "/";

    public async Task<bool> ExistsAsync(ArtifactArea area, string path, CancellationToken ct = default)
    {
        await RejectLegacyCollisionAsync(area, path, ct);
        return await inner.ExistsAsync(area, Physical(path), ct);
    }

    public async Task<ArtifactInfo?> GetInfoAsync(
        ArtifactArea area, string path, CancellationToken ct = default)
    {
        await RejectLegacyCollisionAsync(area, path, ct);
        var info = await inner.GetInfoAsync(area, Physical(path), ct);
        return info is null ? null : Logical(info.Value);
    }

    public async IAsyncEnumerable<ArtifactInfo> EnumerateAsync(
        ArtifactArea area,
        string? prefix = null,
        bool recursive = true,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (requireExclusiveBackend)
            await RejectUnscopedArtifactsAsync(area, ct);
        var physicalPrefix = string.IsNullOrWhiteSpace(prefix)
            ? _prefix.TrimEnd('/')
            : Physical(prefix);
        await foreach (var info in inner.EnumerateAsync(area, physicalPrefix, recursive, ct))
        {
            if (info.Path.StartsWith(_prefix, StringComparison.Ordinal))
                yield return Logical(info);
        }
    }

    public async Task<Stream> OpenReadAsync(
        ArtifactArea area, string path, CancellationToken ct = default)
    {
        await RejectLegacyCollisionAsync(area, path, ct);
        return await inner.OpenReadAsync(area, Physical(path), ct);
    }

    public async Task<byte[]> ReadAllBytesAsync(
        ArtifactArea area, string path, CancellationToken ct = default)
    {
        await RejectLegacyCollisionAsync(area, path, ct);
        return await inner.ReadAllBytesAsync(area, Physical(path), ct);
    }

    public async Task<string> ReadAllTextAsync(
        ArtifactArea area, string path, CancellationToken ct = default)
    {
        await RejectLegacyCollisionAsync(area, path, ct);
        return await inner.ReadAllTextAsync(area, Physical(path), ct);
    }

    public async Task WriteAsync(
        ArtifactArea area,
        string path,
        Stream content,
        bool overwrite = true,
        CancellationToken ct = default)
    {
        await RejectLegacyCollisionAsync(area, path, ct);
        await inner.WriteAsync(area, Physical(path), content, overwrite, ct);
    }

    public async Task WriteAllBytesAsync(
        ArtifactArea area,
        string path,
        ReadOnlyMemory<byte> content,
        bool overwrite = true,
        CancellationToken ct = default)
    {
        await RejectLegacyCollisionAsync(area, path, ct);
        await inner.WriteAllBytesAsync(area, Physical(path), content, overwrite, ct);
    }

    public async Task WriteAllTextAsync(
        ArtifactArea area,
        string path,
        string content,
        bool overwrite = true,
        CancellationToken ct = default)
    {
        await RejectLegacyCollisionAsync(area, path, ct);
        await inner.WriteAllTextAsync(area, Physical(path), content, overwrite, ct);
    }

    public async Task<bool> DeleteAsync(
        ArtifactArea area, string path, CancellationToken ct = default)
    {
        await RejectLegacyCollisionAsync(area, path, ct);
        return await inner.DeleteAsync(area, Physical(path), ct);
    }

    public async Task MoveAsync(
        ArtifactArea area,
        string sourcePath,
        string destinationPath,
        bool overwrite = false,
        CancellationToken ct = default)
    {
        await RejectLegacyCollisionAsync(area, sourcePath, ct);
        await RejectLegacyCollisionAsync(area, destinationPath, ct);
        await inner.MoveAsync(area, Physical(sourcePath), Physical(destinationPath), overwrite, ct);
    }

    public async Task<IArtifactLease> LeaseLocalCopyAsync(
        ArtifactArea area, string path, CancellationToken ct = default)
    {
        await RejectLegacyCollisionAsync(area, path, ct);
        return await inner.LeaseLocalCopyAsync(area, Physical(path), ct);
    }

    private async Task RejectLegacyCollisionAsync(
        ArtifactArea area,
        string logicalPath,
        CancellationToken ct)
    {
        var normalized = ArtifactPath.Normalize(logicalPath);
        if (await inner.ExistsAsync(area, normalized, ct))
            throw LegacyArtifactException(area, normalized);
    }

    private async Task RejectUnscopedArtifactsAsync(ArtifactArea area, CancellationToken ct)
    {
        await foreach (var info in inner.EnumerateAsync(area, prefix: null, recursive: true, ct))
        {
            if (!info.Path.StartsWith(_prefix, StringComparison.Ordinal))
                throw LegacyArtifactException(area, info.Path);
        }
    }

    private InvalidOperationException LegacyArtifactException(ArtifactArea area, string path) =>
        new($"Unscoped artifact '{area}/{path}' exists outside dedicated tenant prefix " +
            $"'{_prefix}'. Migrate or quarantine legacy artifacts before serving this tenant.");

    private string Physical(string logicalPath) =>
        _prefix + ArtifactPath.Normalize(logicalPath);

    private ArtifactInfo Logical(ArtifactInfo physical) =>
        new(physical.Path[_prefix.Length..], physical.Length, physical.LastModifiedUtc);
}

/// <summary>Creates tenant views over one provider backend from server-derived context.</summary>
public interface ITenantArtifactStorageFactory
{
    IArtifactStorage ForTenant(TenantContext tenant);
}

public sealed class TenantArtifactStorageFactory(
    IArtifactStorage backend,
    bool requireExclusiveBackend = false)
    : ITenantArtifactStorageFactory
{
    public IArtifactStorage ForTenant(TenantContext tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        return new TenantScopedArtifactStorage(backend, tenant, requireExclusiveBackend);
    }
}
