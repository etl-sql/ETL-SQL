using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using ETL_SQL.Orchestrator.Storage;

namespace ETL_SQL.App
{
    /// <summary>
    /// A single runtime-data location that <c>etl-sql purge</c> can remove.
    /// </summary>
    /// <param name="Path">Absolute, normalized path.</param>
    /// <param name="IsDirectory">True for directory trees; false for individual files.</param>
    /// <param name="Description">Human-readable description shown in listings.</param>
    public sealed record PurgeTarget(string Path, bool IsDirectory, string Description);

    /// <summary>Outcome of deleting (or previewing) a single <see cref="PurgeTarget"/>.</summary>
    public sealed record PurgeResult(PurgeTarget Target, bool Existed, long Bytes, bool Deleted, string? Error);

    /// <summary>
    /// Resolves and removes the runtime data ETL-SQL writes outside the installer's tracked payload
    /// (logs, snapshots, published reports, portal/orchestrator databases, sessions, portal data dirs).
    ///
    /// This is the single cross-platform source of truth for "delete all data" so the Windows MSI,
    /// Linux .deb, macOS bundle, and ad-hoc CLI users all wipe the same set. Path resolution honors
    /// configuration overrides and falls back to the same defaults the engine uses at runtime, so an
    /// installed (working-dir-anchored) layout and a LocalApplicationData default layout are both covered.
    /// </summary>
    public static class DataPurgeService
    {
        /// <summary>
        /// Resolves every candidate data target from configuration plus known defaults. Relative paths
        /// resolve against <paramref name="baseDir"/> (the install/working directory). Targets are returned
        /// whether or not they currently exist; deduplicated by normalized full path.
        /// </summary>
        public static IReadOnlyList<PurgeTarget> ResolveTargets(IConfiguration config, string baseDir)
        {
            if (string.IsNullOrWhiteSpace(baseDir)) baseDir = AppContext.BaseDirectory;

            string Resolve(string p) =>
                Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(baseDir, p));

            var targets = new List<PurgeTarget>();

            void AddDir(string? p, string desc)
            {
                if (!string.IsNullOrWhiteSpace(p)) targets.Add(new PurgeTarget(Resolve(p!), true, desc));
            }

            // SQLite leaves -wal/-shm sidecars next to the main db; remove all three.
            void AddDb(string? p, string desc)
            {
                if (string.IsNullOrWhiteSpace(p)) return;
                var full = Resolve(p!);
                targets.Add(new PurgeTarget(full, false, desc));
                targets.Add(new PurgeTarget(full + "-wal", false, desc + " (WAL)"));
                targets.Add(new PurgeTarget(full + "-shm", false, desc + " (SHM)"));
            }

            // Logs
            AddDir(config["Logging:AppLog:Directory"]    ?? "logs/app",     "Application logs");
            AddDir(config["Logging:ScriptLog:Directory"] ?? "logs/scripts", "Script logs");
            AddDir(config["Logging:TestLog:Directory"]   ?? "logs/tests",   "Test logs");

            // Portal content + data directories
            AddDir(config["Portal:SnapshotDirectory"] ?? "./Snapshots",    "Report snapshots");
            AddDir(config["Portal:ScriptRootPath"]    ?? "./Reports",      "Published reports");
            AddDir(config["Portal:MapRootPath"]       ?? "./data/maps",    "Portal map files");
            AddDir(config["Portal:DatasetRootPath"]   ?? "./data/datasets","Portal dataset files");

            // Databases
            AddDb(config["Portal:DatabasePath"] ?? "./portal.db", "Portal database");
            AddDb(config["Portal:Orchestrator:DatabasePath"]
                  ?? config["Orchestrator:HistoryDbPath"]
                  ?? SQLiteJobHistoryStore.DefaultDbPath(),
                  "Orchestrator job history database");

            // Persistent sessions + spill data (LocalApplicationData by default)
            var sessionRoot = config["Session:Root"];
            if (string.IsNullOrWhiteSpace(sessionRoot))
                sessionRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ETL-SQL", "Sessions");
            AddDir(sessionRoot, "Persistent sessions and spill data");

            // Dedup by normalized path; case-insensitive on Windows/macOS file systems.
            var comparer = OperatingSystem.IsLinux()
                ? StringComparer.Ordinal
                : StringComparer.OrdinalIgnoreCase;
            var seen = new HashSet<string>(comparer);
            return targets.Where(t => seen.Add(t.Path)).ToList();
        }

        /// <summary>
        /// Conservative guard against deleting a filesystem root, a user-profile root, or a well-known
        /// system directory even if configuration is misconfigured to point at one.
        /// </summary>
        public static bool IsUnsafeTarget(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return true;

            string normalized;
            try { normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullPath)); }
            catch { return true; }

            // Filesystem root (e.g. "C:\" or "/").
            var root = Path.GetPathRoot(normalized);
            if (!string.IsNullOrEmpty(root)
                && string.Equals(Path.TrimEndingDirectorySeparator(root), normalized, PathComparison))
                return true;

            // User profile / home root (deleting it is never intended).
            foreach (var special in new[]
                     {
                         Environment.SpecialFolder.UserProfile,
                         Environment.SpecialFolder.Windows,
                         Environment.SpecialFolder.System,
                         Environment.SpecialFolder.ProgramFiles,
                         Environment.SpecialFolder.ProgramFilesX86,
                     })
            {
                var dir = SafeFolder(special);
                if (dir != null && string.Equals(dir, normalized, PathComparison))
                    return true;
            }

            // Unix system roots.
            foreach (var sys in new[] { "/", "/etc", "/usr", "/bin", "/var", "/lib", "/home", "/root", "/boot", "/sys", "/proc" })
            {
                if (string.Equals(sys, normalized, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static StringComparison PathComparison =>
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        private static string? SafeFolder(Environment.SpecialFolder folder)
        {
            try
            {
                var p = Environment.GetFolderPath(folder);
                return string.IsNullOrEmpty(p) ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(p));
            }
            catch { return null; }
        }

        /// <summary>Computes the on-disk size of a target (0 if it does not exist).</summary>
        public static long MeasureBytes(PurgeTarget target)
        {
            try
            {
                if (target.IsDirectory)
                {
                    if (!Directory.Exists(target.Path)) return 0;
                    long total = 0;
                    foreach (var f in Directory.EnumerateFiles(target.Path, "*", SearchOption.AllDirectories))
                    {
                        try { total += new FileInfo(f).Length; } catch { /* skip unreadable */ }
                    }
                    return total;
                }

                return File.Exists(target.Path) ? new FileInfo(target.Path).Length : 0;
            }
            catch { return 0; }
        }

        /// <summary>
        /// Deletes (or, when <paramref name="dryRun"/> is true, previews) each existing, safe target.
        /// Never throws for per-target failures — locked/missing files are reported, not fatal, so an
        /// uninstall is never blocked by leftover data.
        /// </summary>
        public static IReadOnlyList<PurgeResult> Execute(IEnumerable<PurgeTarget> targets, bool dryRun)
        {
            var results = new List<PurgeResult>();
            foreach (var target in targets)
            {
                if (IsUnsafeTarget(target.Path))
                {
                    results.Add(new PurgeResult(target, false, 0, false, "Refused: unsafe path"));
                    continue;
                }

                bool exists = target.IsDirectory ? Directory.Exists(target.Path) : File.Exists(target.Path);
                long bytes = MeasureBytes(target);

                if (!exists)
                {
                    results.Add(new PurgeResult(target, false, 0, false, null));
                    continue;
                }

                if (dryRun)
                {
                    results.Add(new PurgeResult(target, true, bytes, false, null));
                    continue;
                }

                try
                {
                    if (target.IsDirectory) Directory.Delete(target.Path, recursive: true);
                    else File.Delete(target.Path);
                    results.Add(new PurgeResult(target, true, bytes, true, null));
                }
                catch (Exception ex)
                {
                    results.Add(new PurgeResult(target, true, bytes, false, ex.Message));
                }
            }

            return results;
        }
    }
}
