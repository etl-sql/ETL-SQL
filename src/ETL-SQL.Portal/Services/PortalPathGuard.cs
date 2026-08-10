using System.IO;
using System.Text.RegularExpressions;
using ETL_SQL.Core;

namespace ETL_SQL.Portal.Services;

public static class PortalPathGuard
{
    public static bool TryResolveScript(PortalConfig config, string path, out string resolved) =>
        TryResolveWithinRoot(config.ScriptRootPath, path, out resolved);

    public static bool TryResolveScript(
        PortalConfig config,
        string tenantId,
        string path,
        out string resolved) =>
        TryResolveWithinRoot(TenantAreaRoot(config, config.ScriptRootPath, tenantId), path, out resolved);

    public static bool TryResolveSnapshot(PortalConfig config, string path, out string resolved) =>
        TryResolveWithinRoot(config.SnapshotDirectory, path, out resolved);

    public static bool TryResolveMap(PortalConfig config, string path, out string resolved) =>
        TryResolveWithinRoot(config.MapRootPath, path, out resolved);

    public static bool TryResolveDataset(PortalConfig config, string path, out string resolved) =>
        TryResolveWithinRoot(config.DatasetRootPath, path, out resolved);

    // ── Area-relative keys for IArtifactStorage ───────────────────────────────
    // A stored path may be absolute (older publish/execute rows) or relative (uploads); these convert
    // either form into an area-relative key, or null if it escapes the configured root. They reuse the
    // within-root guards above, so routing call sites through IArtifactStorage needs no data migration.

    public static string? ToScriptKey(PortalConfig config, string? path) =>
        ToAreaKey(config.ScriptRootPath, path, TryResolveScript, config);

    public static string? ToScriptKey(PortalConfig config, string tenantId, string? path)
    {
        var root = TenantAreaRoot(config, config.ScriptRootPath, tenantId);
        if (string.IsNullOrWhiteSpace(path)
            || !TryResolveWithinRoot(root, path, out var resolved))
            return null;
        return Path.GetRelativePath(root, resolved).Replace('\\', '/');
    }

    public static string? ToSnapshotKey(PortalConfig config, string? path) =>
        ToAreaKey(config.SnapshotDirectory, path, TryResolveSnapshot, config);

    private delegate bool Resolver(PortalConfig config, string path, out string resolved);

    private static bool TryResolveWithinRoot(string root, string path, out string resolved)
    {
        resolved = string.Empty;

        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path))
            return false;

        if (!OperatingSystem.IsWindows() && IsWindowsRootedPath(path))
            return false;

        var normalized = path.Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        return SafePath.TryResolveWithinRoot(root, normalized, out resolved);
    }

    private static bool IsWindowsRootedPath(string path) =>
        Regex.IsMatch(path, @"^[A-Za-z]:[\\/]")
        || path.StartsWith(@"\\", StringComparison.Ordinal)
        || path.StartsWith("//", StringComparison.Ordinal);

    public static string TenantAreaRoot(PortalConfig config, string configuredRoot, string tenantId)
    {
        var root = Path.GetFullPath(configuredRoot);
        if (!config.SharedTenancy.Enabled)
            return root;
        var tenant = ETL_SQL.Core.Multitenancy.TenantId.FromTrustedSource(tenantId);
        return Path.Combine(root, tenant.Value);
    }

    private static string? ToAreaKey(string? root, string? path, Resolver resolver, PortalConfig config)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
            return null;
        if (!resolver(config, path, out var resolved))
            return null;
        return Path.GetRelativePath(Path.GetFullPath(root), resolved).Replace('\\', '/');
    }
}
