using System.Runtime.CompilerServices;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Core.Storage;

namespace ETL_SQL.Portal.Services;

/// <summary>Provider backend retained separately from the tenant-routing public storage service.</summary>
public sealed record PortalArtifactStorageBackend(IArtifactStorage Storage);

/// <summary>
/// Routes request artifact operations through the verified tenant context on Shared hosts and the
/// immutable configured tenant on Dedicated hosts. Shared background work uses
/// <see cref="ITenantArtifactStorageFactory"/> with its persisted server-owned tenant instead.
/// </summary>
public sealed class PortalTenantArtifactStorage(
    PortalArtifactStorageBackend backend,
    ITenantArtifactStorageFactory tenants,
    PortalConfig config,
    IHttpContextAccessor httpContextAccessor) : IArtifactStorage
{
    private IArtifactStorage Current
    {
        get
        {
            if (config.SharedTenancy.Enabled)
            {
                var context = httpContextAccessor.HttpContext?.RequestServices
                    .GetService<TenantContext>();
                if (context is null || context.Origin != TenantContextOrigin.VerifiedCredential)
                    throw new UnauthorizedAccessException(
                        "Shared artifact storage requires a verified tenant context.");
                return tenants.ForTenant(context);
            }

            return string.IsNullOrWhiteSpace(config.TenantId)
                ? backend.Storage
                : tenants.ForTenant(TenantContext.FromHostConfiguration(config.TenantId));
        }
    }

    public Task<bool> ExistsAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        Current.ExistsAsync(area, path, ct);
    public Task<ArtifactInfo?> GetInfoAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        Current.GetInfoAsync(area, path, ct);
    public async IAsyncEnumerable<ArtifactInfo> EnumerateAsync(
        ArtifactArea area, string? prefix = null, bool recursive = true,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var storage = Current;
        await foreach (var info in storage.EnumerateAsync(area, prefix, recursive, ct))
            yield return info;
    }
    public Task<Stream> OpenReadAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        Current.OpenReadAsync(area, path, ct);
    public Task<byte[]> ReadAllBytesAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        Current.ReadAllBytesAsync(area, path, ct);
    public Task<string> ReadAllTextAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        Current.ReadAllTextAsync(area, path, ct);
    public Task WriteAsync(ArtifactArea area, string path, Stream content, bool overwrite = true,
        CancellationToken ct = default) => Current.WriteAsync(area, path, content, overwrite, ct);
    public Task WriteAllBytesAsync(ArtifactArea area, string path, ReadOnlyMemory<byte> content,
        bool overwrite = true, CancellationToken ct = default) =>
        Current.WriteAllBytesAsync(area, path, content, overwrite, ct);
    public Task WriteAllTextAsync(ArtifactArea area, string path, string content,
        bool overwrite = true, CancellationToken ct = default) =>
        Current.WriteAllTextAsync(area, path, content, overwrite, ct);
    public Task<bool> DeleteAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        Current.DeleteAsync(area, path, ct);
    public Task MoveAsync(ArtifactArea area, string sourcePath, string destinationPath,
        bool overwrite = false, CancellationToken ct = default) =>
        Current.MoveAsync(area, sourcePath, destinationPath, overwrite, ct);
    public Task<IArtifactLease> LeaseLocalCopyAsync(
        ArtifactArea area, string path, CancellationToken ct = default) =>
        Current.LeaseLocalCopyAsync(area, path, ct);
}
