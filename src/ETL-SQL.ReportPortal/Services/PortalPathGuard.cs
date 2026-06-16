using System.IO;
using ETL_SQL.Core;

namespace ETL_SQL.ReportPortal.Services;

public static class PortalPathGuard
{
    public static bool TryResolveScript(PortalConfig config, string path, out string resolved) =>
        SafePath.TryResolveWithinRoot(config.ScriptRootPath, path, out resolved);

    public static bool TryResolveSnapshot(PortalConfig config, string path, out string resolved) =>
        SafePath.TryResolveWithinRoot(config.SnapshotDirectory, path, out resolved);

    public static bool TryResolveMap(PortalConfig config, string path, out string resolved) =>
        SafePath.TryResolveWithinRoot(config.MapRootPath, path, out resolved);

    public static bool TryResolveDataset(PortalConfig config, string path, out string resolved) =>
        SafePath.TryResolveWithinRoot(config.DatasetRootPath, path, out resolved);

    // ── Area-relative keys for IArtifactStorage ───────────────────────────────
    // A stored path may be absolute (older publish/execute rows) or relative (uploads); these convert
    // either form into an area-relative key, or null if it escapes the configured root. They reuse the
    // within-root guards above, so routing call sites through IArtifactStorage needs no data migration.

    public static string? ToScriptKey(PortalConfig config, string? path) =>
        ToAreaKey(config.ScriptRootPath, path, TryResolveScript, config);

    public static string? ToSnapshotKey(PortalConfig config, string? path) =>
        ToAreaKey(config.SnapshotDirectory, path, TryResolveSnapshot, config);

    private delegate bool Resolver(PortalConfig config, string path, out string resolved);

    private static string? ToAreaKey(string? root, string? path, Resolver resolver, PortalConfig config)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
            return null;
        if (!resolver(config, path, out var resolved))
            return null;
        return Path.GetRelativePath(Path.GetFullPath(root), resolved).Replace('\\', '/');
    }
}
