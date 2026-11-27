using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Multitenancy;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Engine.Handlers;

/// <summary>
/// Routes Engine bundle reads and publishes through <see cref="ITenantBundleStore"/> whenever the
/// execution context carries a tenant authority, failing closed if no tenant-scoped store is present.
/// </summary>
internal static class BundleStoreAccessor
{
    public static TenantContext? ResolveTenant(IExecutionContext? context)
    {
        if (context is null)
            return null;
        if (context.StorageCapability != null)
            return context.StorageCapability.Tenant;
        if (!string.IsNullOrWhiteSpace(context.ExecutionIdentity?.TenantId))
            return TenantContext.FromVerifiedCredential(context.ExecutionIdentity.TenantId);
        return null;
    }

    public static bool HasStore(IBundleStore? store, IExecutionContext? context) =>
        store is not null || context?.ServiceProvider?.GetService<ITenantBundleStore>() is not null;

    public static Task<BundleVersionInfo> PublishBundleAsync(
        IBundleStore? store,
        IExecutionContext? context,
        BundlePublishRequest request)
    {
        if (ResolveTenant(context) is { } tenant)
            return RequireTenantStore(store, context).PublishBundleAsync(tenant, request);
        return RequireUntenantedStore(store).PublishBundleAsync(request);
    }

    public static Task<BundleVersionInfo?> GetLatestVersionAsync(
        IBundleStore? store,
        IExecutionContext? context,
        string bundleName)
    {
        if (ResolveTenant(context) is { } tenant)
            return RequireTenantStore(store, context).GetLatestVersionAsync(tenant, bundleName);
        return RequireUntenantedStore(store).GetLatestVersionAsync(bundleName);
    }

    public static Task<BundleVersionInfo?> GetVersionAsync(
        IBundleStore? store,
        IExecutionContext? context,
        string bundleName,
        int version)
    {
        if (ResolveTenant(context) is { } tenant)
            return RequireTenantStore(store, context).GetVersionAsync(tenant, bundleName, version);
        return RequireUntenantedStore(store).GetVersionAsync(bundleName, version);
    }

    public static Task<BundleFileInfo?> GetFileAsync(
        IBundleStore? store,
        IExecutionContext? context,
        string bundleName,
        int version,
        string virtualPath)
    {
        if (ResolveTenant(context) is { } tenant)
            return RequireTenantStore(store, context).GetFileAsync(tenant, bundleName, version, virtualPath);
        return RequireUntenantedStore(store).GetFileAsync(bundleName, version, virtualPath);
    }

    public static Task<IEnumerable<BundleVersionInfo>> GetBundlesAsync(
        IBundleStore? store,
        IExecutionContext? context)
    {
        if (ResolveTenant(context) is { } tenant)
            return RequireTenantStore(store, context).GetBundlesAsync(tenant);
        return RequireUntenantedStore(store).GetBundlesAsync();
    }

    public static Task<IEnumerable<BundleVersionInfo>> GetVersionsAsync(
        IBundleStore? store,
        IExecutionContext? context,
        string bundleName)
    {
        if (ResolveTenant(context) is { } tenant)
            return RequireTenantStore(store, context).GetVersionsAsync(tenant, bundleName);
        return RequireUntenantedStore(store).GetVersionsAsync(bundleName);
    }

    public static Task<IEnumerable<BundleFileInfo>> GetFilesAsync(
        IBundleStore? store,
        IExecutionContext? context,
        string bundleName,
        int version)
    {
        if (ResolveTenant(context) is { } tenant)
            return RequireTenantStore(store, context).GetFilesAsync(tenant, bundleName, version);
        return RequireUntenantedStore(store).GetFilesAsync(bundleName, version);
    }

    public static Task<IEnumerable<BundleDependencyInfo>> GetDependenciesAsync(
        IBundleStore? store,
        IExecutionContext? context,
        string bundleName,
        int version)
    {
        if (ResolveTenant(context) is { } tenant)
            return RequireTenantStore(store, context).GetDependenciesAsync(tenant, bundleName, version);
        return RequireUntenantedStore(store).GetDependenciesAsync(bundleName, version);
    }

    private static ITenantBundleStore RequireTenantStore(IBundleStore? store, IExecutionContext? context) =>
        store as ITenantBundleStore
        ?? context?.ServiceProvider?.GetService<ITenantBundleStore>()
        ?? throw new ExecutionException(
            "Tenant-bound bundle operations require a tenant-partitioned bundle store (ITenantBundleStore).");

    private static IBundleStore RequireUntenantedStore(IBundleStore? store) =>
        store ?? throw new ExecutionException("No bundle store is registered.");
}
