using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Extensions.Configuration;

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
            AddDir(config["Logging:AppLog:Directory"] ?? "logs/app", "Application logs");
            AddDir(config["Logging:ScriptLog:Directory"] ?? "logs/scripts", "Script logs");
            AddDir(config["Logging:TestLog:Directory"] ?? "logs/tests", "Test logs");

            // Portal content + data directories
            AddDir(config["Portal:SnapshotDirectory"] ?? "./Snapshots", "Report snapshots");
            AddDir(config["Portal:ScriptRootPath"] ?? "./Reports", "Published reports");
            AddDir(config["Portal:MapRootPath"] ?? "./data/maps", "Portal map files");
            AddDir(config["Portal:DatasetRootPath"] ?? "./data/datasets", "Portal dataset files");

            // Databases
            AddDb(config["Portal:DatabasePath"] ?? "./portal.db", "Portal database");
            AddDb(config["Portal:Orchestrator:DatabasePath"]
                  ?? config["Orchestrator:DatabasePath"]
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

            return Deduplicate(targets);
        }

        /// <summary>
        /// Resolves the runtime data targets for an installed instance (logs, snapshots, reports,
        /// datasets, maps, databases, sessions, Data Protection key ring, and generated secrets/bootstrap files).
        /// </summary>
        public static IReadOnlyList<PurgeTarget> ResolveInstalledDataTargets(IConfiguration config, string installDir)
        {
            if (string.IsNullOrWhiteSpace(installDir)) installDir = AppContext.BaseDirectory;
            string Resolve(string p) =>
                Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(installDir, p));

            var targets = ResolveTargets(config, installDir).ToList();
            var portalDb = Resolve(config["Portal:DatabasePath"] ?? "./portal.db");
            var keyRing = Resolve(
                config["Portal:Storage:KeyRingPath"]
                ?? Path.Combine(Path.GetDirectoryName(portalDb) ?? installDir, ".portal-keys"));

            targets.Add(new PurgeTarget(Resolve("logs"), true, "Application and service logs"));
            targets.Add(new PurgeTarget(Resolve("data"), true, "Portal data directory"));
            targets.Add(new PurgeTarget(keyRing, true, "Data Protection key ring"));
            targets.Add(new PurgeTarget(
                Resolve(ETL_SQL.Common.ConfigurationExtensions.InstalledSecretsFileName),
                false,
                "Installed service secrets"));
            targets.Add(new PurgeTarget(
                Resolve("first-run-password.txt"),
                false,
                "First-run bootstrap password"));

            return Deduplicate(targets);
        }

        private static IReadOnlyList<PurgeTarget> Deduplicate(IEnumerable<PurgeTarget> targets)
        {
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
        public static bool IsUnsafeTarget(string fullPath, string? protectedInstallDir = null)
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

            // The install folder, or a folder that contains it: ScriptRootPath = "." would otherwise
            // delete the application itself.
            var appDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
            if (string.Equals(appDir, normalized, PathComparison) || IsAncestor(normalized, appDir))
                return true;

            if (!string.IsNullOrWhiteSpace(protectedInstallDir))
            {
                var installRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(protectedInstallDir));
                if (string.Equals(installRoot, normalized, PathComparison) || IsAncestor(normalized, installRoot))
                    return true;
            }

            // A git working tree, or a folder inside one. Studio's source-controlled mode keeps
            // reports in a repository; purging runtime data must not delete the team's source.
            // Data under the install folder is the application's own, wherever that folder sits.
            if (IsAncestor(appDir, normalized)) return false;
            for (var dir = new DirectoryInfo(normalized); dir != null; dir = dir.Parent)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git")))
                    return true;
            }

            return false;
        }

        private static bool IsAncestor(string ancestor, string path)
        {
            var prefix = ancestor.EndsWith(Path.DirectorySeparatorChar) ? ancestor : ancestor + Path.DirectorySeparatorChar;
            return path.StartsWith(prefix, PathComparison);
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
        public static IReadOnlyList<PurgeResult> Execute(
            IEnumerable<PurgeTarget> targets,
            bool dryRun,
            string? protectedInstallDir = null)
        {
            var results = new List<PurgeResult>();
            foreach (var target in targets)
            {
                if (IsUnsafeTarget(target.Path, protectedInstallDir))
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

        /// <summary>
        /// Executes <c>etl-sql admin purge-data</c>. Requires <c>--confirm</c> (unless <c>--dry-run</c>
        /// is set) and removes only the resolved installed runtime-data locations.
        /// </summary>
        public static int RunAdminPurgeData(CliContext ctx, ILogger logger, IConfiguration? fallbackConfig = null)
        {
            if (!ctx.AdminPurgeDryRun && !ctx.AdminPurgeConfirm)
            {
                logger.WriteLine(
                    "Refusing to delete installed runtime data without --confirm. Pass --dry-run to preview or --confirm to delete.",
                    ConsoleColor.Red);
                return 1;
            }

            var installDir = string.IsNullOrWhiteSpace(ctx.AdminPurgeInstallDir)
                ? Path.GetFullPath(AppContext.BaseDirectory)
                : Path.GetFullPath(ctx.AdminPurgeInstallDir.Trim('"', '\'', ' '));

            IConfiguration config;
            if (!string.IsNullOrWhiteSpace(ctx.AdminPurgeInstallDir) || fallbackConfig is null)
            {
                config = ETL_SQL.Common.ConfigurationExtensions.BuildInstalledSettings(installDir);
            }
            else
            {
                config = fallbackConfig;
            }

            var targets = ResolveInstalledDataTargets(config, installDir);
            var existing = targets
                .Where(t => t.IsDirectory ? Directory.Exists(t.Path) : File.Exists(t.Path))
                .ToList();

            if (existing.Count == 0)
            {
                logger.WriteLine("No ETL-SQL runtime data found. Nothing to purge.", ConsoleColor.Green);
                return 0;
            }

            if (ctx.AdminPurgeDryRun)
            {
                logger.WriteLine($"Dry run: {existing.Count} runtime data item(s) would be deleted:", ConsoleColor.Yellow);
                foreach (var target in existing)
                    logger.WriteLine($"  - {target.Description}: {target.Path} ({MeasureBytes(target)} bytes)", ConsoleColor.Gray);
                return 0;
            }

            var results = Execute(existing, dryRun: false, protectedInstallDir: installDir);
            var deleted = results.Count(r => r.Deleted);
            var failed = results.Where(r => r.Error != null).ToList();
            logger.WriteLine($"Deleted {deleted} installed runtime data item(s).", ConsoleColor.Green);
            foreach (var failure in failed)
                logger.WriteLine($"Could not delete {failure.Target.Path}: {failure.Error}", ConsoleColor.Yellow);

            return failed.Count > 0 ? 1 : 0;
        }
    }
}
