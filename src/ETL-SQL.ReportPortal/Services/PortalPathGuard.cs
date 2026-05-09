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
}
