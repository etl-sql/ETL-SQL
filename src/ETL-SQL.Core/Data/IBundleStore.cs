using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Multitenancy;

namespace ETL_SQL.Core.Data;

public record BundleVersionInfo(
    string BundleName,
    int Version,
    string EntryPath,
    string ContentHash,
    DateTime PublishedAt,
    string? Publisher,
    string? Description);

public record BundleFileInfo(
    string BundleName,
    int Version,
    string VirtualPath,
    string Content,
    string ContentHash,
    long SizeBytes,
    string ContentType);

public record BundleDependencyInfo(
    string BundleName,
    int Version,
    string FromPath,
    string ToPath);

public record BundlePublishFile(
    string VirtualPath,
    string Content,
    string ContentHash,
    long SizeBytes,
    string ContentType);

public record BundlePublishRequest(
    string BundleName,
    string EntryPath,
    IReadOnlyList<BundlePublishFile> Files,
    IReadOnlyList<BundleDependencyInfo> Dependencies,
    string ContentHash,
    string EncryptionMode,
    string? EncryptionMetadata,
    string? Publisher,
    string? Description);

public interface IBundleStore
{
    Task InitializeAsync();
    Task<BundleVersionInfo> PublishBundleAsync(BundlePublishRequest request);
    Task<BundleVersionInfo?> GetLatestVersionAsync(string bundleName);
    Task<BundleVersionInfo?> GetVersionAsync(string bundleName, int version);
    Task<BundleFileInfo?> GetFileAsync(string bundleName, int version, string virtualPath);
    Task<IEnumerable<BundleVersionInfo>> GetBundlesAsync();
    Task<IEnumerable<BundleVersionInfo>> GetVersionsAsync(string bundleName);
    Task<IEnumerable<BundleFileInfo>> GetFilesAsync(string bundleName, int version);
    Task<IEnumerable<BundleDependencyInfo>> GetDependenciesAsync(string bundleName, int version);
}

/// <summary>
/// Tenant-qualified Orchestrator bundle store for shared and dedicated deployments.
/// Implementations must bind every publish write and filter every read and retention query
/// by <see cref="TenantContext"/> in the underlying database query.
/// </summary>
public interface ITenantBundleStore
{
    Task InitializeAsync();
    Task<BundleVersionInfo> PublishBundleAsync(TenantContext tenant, BundlePublishRequest request);
    Task<BundleVersionInfo?> GetLatestVersionAsync(TenantContext tenant, string bundleName);
    Task<BundleVersionInfo?> GetVersionAsync(TenantContext tenant, string bundleName, int version);
    Task<BundleFileInfo?> GetFileAsync(TenantContext tenant, string bundleName, int version, string virtualPath);
    Task<IEnumerable<BundleVersionInfo>> GetBundlesAsync(TenantContext tenant);
    Task<IEnumerable<BundleVersionInfo>> GetVersionsAsync(TenantContext tenant, string bundleName);
    Task<IEnumerable<BundleFileInfo>> GetFilesAsync(TenantContext tenant, string bundleName, int version);
    Task<IEnumerable<BundleDependencyInfo>> GetDependenciesAsync(TenantContext tenant, string bundleName, int version);
    Task<IReadOnlyList<BundleVersionInfo>> GetUnassignedDefaultBundlesAsync(CancellationToken cancellationToken = default);
    Task<int> ReassignDefaultBundlesAsync(
        TenantContext targetTenant,
        string? bundleName = null,
        bool includeUnbound = false,
        CancellationToken cancellationToken = default);
}
