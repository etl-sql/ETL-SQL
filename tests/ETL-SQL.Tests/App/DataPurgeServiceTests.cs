using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ETL_SQL.App;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ETL_SQL.Tests.CliCommands
{
    public class DataPurgeServiceTests : IDisposable
    {
        private readonly string _baseDir;

        public DataPurgeServiceTests()
        {
            // Runtime-owned data is permitted even when the checkout or user's temp folder is in a Git tree.
            _baseDir = Path.Combine(AppContext.BaseDirectory, "etlsql_purge_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_baseDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, true); } catch { }
        }

        private static IConfiguration Config(Dictionary<string, string?> values) =>
            new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        [Fact]
        public void ResolveTargets_ResolvesRelativeConfigPathsAgainstBaseDir()
        {
            var config = Config(new() { ["Portal:DatabasePath"] = "./portal.db" });

            var targets = DataPurgeService.ResolveTargets(config, _baseDir);

            var expected = Path.GetFullPath(Path.Combine(_baseDir, "portal.db"));
            Assert.Contains(targets, t => t.Path == expected && !t.IsDirectory);
        }

        [Fact]
        public void ResolveTargets_HonorsAbsoluteConfigOverrides()
        {
            var customReports = Path.Combine(_baseDir, "custom-reports");
            var config = Config(new() { ["Portal:ScriptRootPath"] = customReports });

            var targets = DataPurgeService.ResolveTargets(config, _baseDir);

            Assert.Contains(targets, t => t.Path == Path.GetFullPath(customReports) && t.IsDirectory);
        }

        [Fact]
        public void ResolveTargets_IncludesSqliteSidecarsForDatabases()
        {
            var config = Config(new() { ["Portal:DatabasePath"] = "./portal.db" });

            var paths = DataPurgeService.ResolveTargets(config, _baseDir).Select(t => t.Path).ToList();

            var db = Path.GetFullPath(Path.Combine(_baseDir, "portal.db"));
            Assert.Contains(db, paths);
            Assert.Contains(db + "-wal", paths);
            Assert.Contains(db + "-shm", paths);
        }

        [Fact]
        public void ResolveTargets_IncludesLocalAppDataDefaultsWhenUnconfigured()
        {
            var targets = DataPurgeService.ResolveTargets(Config(new()), _baseDir);

            var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var sessionDefault = Path.GetFullPath(Path.Combine(localApp, "ETL-SQL", "Sessions"));
            var historyDefault = Path.GetFullPath(ETL_SQL.Orchestrator.Storage.SQLiteJobHistoryStore.DefaultDbPath());

            Assert.Contains(targets, t => t.Path == sessionDefault && t.IsDirectory);
            Assert.Contains(targets, t => t.Path == historyDefault && !t.IsDirectory);
        }

        [Fact]
        public void ResolveTargets_PrefersPortalOrchestratorDbPath()
        {
            var orchDb = Path.Combine(_baseDir, "orch", "history.db");
            var config = Config(new() { ["Portal:Orchestrator:DatabasePath"] = orchDb });

            var targets = DataPurgeService.ResolveTargets(config, _baseDir);

            Assert.Contains(targets, t => t.Path == Path.GetFullPath(orchDb));
        }

        [Fact]
        public void ResolveTargets_ProducesNoDuplicatePaths()
        {
            var targets = DataPurgeService.ResolveTargets(Config(new()), _baseDir);
            var paths = targets.Select(t => t.Path).ToList();

            Assert.Equal(paths.Count, paths.Distinct().Count());
        }

        [Fact]
        public void IsUnsafeTarget_RejectsFilesystemRootAndEmpty()
        {
            Assert.True(DataPurgeService.IsUnsafeTarget(""));
            var root = Path.GetPathRoot(Path.GetFullPath(_baseDir))!;
            Assert.True(DataPurgeService.IsUnsafeTarget(root));
        }

        [Fact]
        public void IsUnsafeTarget_AllowsNormalDataDir()
        {
            Assert.False(DataPurgeService.IsUnsafeTarget(Path.Combine(_baseDir, "Snapshots")));
        }

        [Fact]
        public void IsUnsafeTarget_RejectsTheInstallFolderAndItsParents()
        {
            var appDir = Path.GetFullPath(AppContext.BaseDirectory);
            Assert.True(DataPurgeService.IsUnsafeTarget(appDir));
            Assert.True(DataPurgeService.IsUnsafeTarget(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(appDir))!));
            Assert.False(DataPurgeService.IsUnsafeTarget(Path.Combine(appDir, "logs", "app")));
        }

        [Fact]
        public void IsUnsafeTarget_RejectsAGitWorkingTree()
        {
            // Source repositories live outside runtime-owned data and must remain protected.
            var repo = Path.Combine(Path.GetTempPath(), "purge-reports-repo-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(repo, ".git"));
            Directory.CreateDirectory(Path.Combine(repo, "Reports"));
            try
            {
                Assert.True(DataPurgeService.IsUnsafeTarget(repo));
                Assert.True(DataPurgeService.IsUnsafeTarget(Path.Combine(repo, "Reports")));

                var results = DataPurgeService.Execute(new[] { new PurgeTarget(Path.Combine(repo, "Reports"), true, "Published reports") }, dryRun: false);
                Assert.Equal("Refused: unsafe path", Assert.Single(results).Error);
                Assert.True(Directory.Exists(Path.Combine(repo, "Reports")));
            }
            finally
            {
                Directory.Delete(repo, recursive: true);
            }
        }

        [Fact]
        public void Execute_DryRun_ReportsButDeletesNothing()
        {
            var dir = Path.Combine(_baseDir, "Reports");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "r.rptsql"), "SELECT 1;");
            var target = new PurgeTarget(dir, true, "Published reports");

            var results = DataPurgeService.Execute(new[] { target }, dryRun: true);

            Assert.True(Directory.Exists(dir));
            var r = Assert.Single(results);
            Assert.True(r.Existed);
            Assert.False(r.Deleted);
            Assert.True(r.Bytes > 0);
        }

        [Fact]
        public void Execute_DeletesExistingFilesAndDirectories()
        {
            var dir = Path.Combine(_baseDir, "Snapshots");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "s.json"), "{}");
            var file = Path.Combine(_baseDir, "portal.db");
            File.WriteAllText(file, "x");

            var results = DataPurgeService.Execute(new[]
            {
                new PurgeTarget(dir, true, "Report snapshots"),
                new PurgeTarget(file, false, "Portal database"),
            }, dryRun: false);

            Assert.False(Directory.Exists(dir));
            Assert.False(File.Exists(file));
            Assert.All(results, r => Assert.True(r.Deleted));
        }

        [Fact]
        public void Execute_MissingTarget_IsNotFatal()
        {
            var target = new PurgeTarget(Path.Combine(_baseDir, "does-not-exist"), false, "Portal database");

            var r = Assert.Single(DataPurgeService.Execute(new[] { target }, dryRun: false));

            Assert.False(r.Existed);
            Assert.False(r.Deleted);
            Assert.Null(r.Error);
        }

        [Fact]
        public void Execute_RefusesUnsafeTargetWithoutDeleting()
        {
            var root = Path.GetPathRoot(Path.GetFullPath(_baseDir))!;
            var target = new PurgeTarget(root, true, "bogus");

            var r = Assert.Single(DataPurgeService.Execute(new[] { target }, dryRun: false));

            Assert.False(r.Deleted);
            Assert.NotNull(r.Error);
            Assert.True(Directory.Exists(root));
        }

        [Fact]
        public void RunAdminPurgeData_RefusesWithoutConfirmation()
        {
            var installDir = SeedInstalledInstance(out var systemDb, out var sessionDir);
            try
            {
                var exit = DataPurgeService.RunAdminPurgeData(new ETL_SQL.Core.CliContext
                {
                    Command = "admin-purge-data",
                    AdminPurgeInstallDir = installDir,
                    AdminPurgeConfirm = false,
                    AdminPurgeDryRun = false
                }, ETL_SQL.Common.NullLogger.Instance);

                Assert.Equal(1, exit);
                Assert.True(Directory.Exists(Path.Combine(installDir, "Reports")));
                Assert.True(Directory.Exists(Path.Combine(installDir, "Snapshots")));
                Assert.True(File.Exists(Path.Combine(installDir, "portal.db")));
                Assert.True(File.Exists(systemDb));
                Assert.True(Directory.Exists(sessionDir));
            }
            finally
            {
                TryDeleteDirectory(Path.GetDirectoryName(installDir)!);
            }
        }

        [Fact]
        public void RunAdminPurgeData_DryRun_PreviewsWithoutDeleting()
        {
            var installDir = SeedInstalledInstance(out var systemDb, out var sessionDir);
            try
            {
                var exit = DataPurgeService.RunAdminPurgeData(new ETL_SQL.Core.CliContext
                {
                    Command = "admin-purge-data",
                    AdminPurgeInstallDir = installDir,
                    AdminPurgeConfirm = false,
                    AdminPurgeDryRun = true
                }, ETL_SQL.Common.NullLogger.Instance);

                Assert.Equal(0, exit);
                Assert.True(Directory.Exists(Path.Combine(installDir, "Reports")));
                Assert.True(File.Exists(Path.Combine(installDir, "appsettings.secrets.json")));
                Assert.True(File.Exists(systemDb));
                Assert.True(Directory.Exists(sessionDir));
            }
            finally
            {
                TryDeleteDirectory(Path.GetDirectoryName(installDir)!);
            }
        }

        [Fact]
        public void RunAdminPurgeData_WithConfirm_DeletesOnlyIntendedRuntimeData()
        {
            var installDir = SeedInstalledInstance(out var systemDb, out var sessionDir);
            try
            {
                var exit = DataPurgeService.RunAdminPurgeData(new ETL_SQL.Core.CliContext
                {
                    Command = "admin-purge-data",
                    AdminPurgeInstallDir = installDir,
                    AdminPurgeConfirm = true
                }, ETL_SQL.Common.NullLogger.Instance);

                Assert.Equal(0, exit);
                Assert.True(Directory.Exists(installDir));
                Assert.True(File.Exists(Path.Combine(installDir, "ETL-SQL.exe")));
                Assert.True(File.Exists(Path.Combine(installDir, "appsettings.json")));
                Assert.False(Directory.Exists(Path.Combine(installDir, "logs")));
                Assert.False(Directory.Exists(Path.Combine(installDir, "Snapshots")));
                Assert.False(Directory.Exists(Path.Combine(installDir, "Reports")));
                Assert.False(Directory.Exists(Path.Combine(installDir, "data")));
                Assert.False(Directory.Exists(Path.Combine(installDir, ".portal-keys")));
                Assert.False(File.Exists(Path.Combine(installDir, "portal.db")));
                Assert.False(File.Exists(Path.Combine(installDir, "portal.db-wal")));
                Assert.False(File.Exists(Path.Combine(installDir, "appsettings.secrets.json")));
                Assert.False(File.Exists(Path.Combine(installDir, "first-run-password.txt")));
                Assert.False(File.Exists(systemDb));
                Assert.False(File.Exists(systemDb + "-shm"));
                Assert.False(Directory.Exists(sessionDir));
            }
            finally
            {
                TryDeleteDirectory(Path.GetDirectoryName(installDir)!);
            }
        }

        [Fact]
        public void RunAdminPurgeData_WithConfirm_IsSafeWhenDirectoriesDoNotExist()
        {
            var root = Path.Combine(_baseDir, "etlsql_purge_empty_" + Guid.NewGuid().ToString("N"));
            var installDir = Path.Combine(root, "install");
            Directory.CreateDirectory(installDir);
            File.WriteAllText(Path.Combine(installDir, "appsettings.json"), $$"""
            {
              "Portal": {
                "Orchestrator": { "DatabasePath": "{{Path.Combine(root, "missing", "etlsql.db").Replace("\\", "\\\\")}}" }
              },
              "Session": { "Root": "{{Path.Combine(root, "missing-sessions").Replace("\\", "\\\\")}}" }
            }
            """);

            try
            {
                var exit = DataPurgeService.RunAdminPurgeData(new ETL_SQL.Core.CliContext
                {
                    Command = "admin-purge-data",
                    AdminPurgeInstallDir = installDir,
                    AdminPurgeConfirm = true
                }, ETL_SQL.Common.NullLogger.Instance);

                Assert.Equal(0, exit);
                Assert.True(Directory.Exists(installDir));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Fact]
        public void RunAdminPurgeData_RefusesInstallDirItselfWhenMisconfigured()
        {
            var root = Path.Combine(_baseDir, "etlsql_purge_guard_" + Guid.NewGuid().ToString("N"));
            var installDir = Path.Combine(root, "install");
            Directory.CreateDirectory(installDir);
            var exePath = Path.Combine(installDir, "ETL-SQL.exe");
            File.WriteAllText(exePath, "BINARY");
            File.WriteAllText(Path.Combine(installDir, "appsettings.json"), $$"""
            {
              "Portal": {
                "ScriptRootPath": ".",
                "Orchestrator": { "DatabasePath": "{{Path.Combine(root, "missing", "etlsql.db").Replace("\\", "\\\\")}}" }
              },
              "Session": { "Root": "{{Path.Combine(root, "missing-sessions").Replace("\\", "\\\\")}}" }
            }
            """);

            try
            {
                var exit = DataPurgeService.RunAdminPurgeData(new ETL_SQL.Core.CliContext
                {
                    Command = "admin-purge-data",
                    AdminPurgeInstallDir = installDir,
                    AdminPurgeConfirm = true
                }, ETL_SQL.Common.NullLogger.Instance);

                Assert.Equal(1, exit);
                Assert.True(Directory.Exists(installDir));
                Assert.True(File.Exists(exePath));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        private string SeedInstalledInstance(out string systemDb, out string sessionDir)
        {
            var root = Path.Combine(_baseDir, "etlsql_admin_purge_" + Guid.NewGuid().ToString("N"));
            var installDir = Path.Combine(root, "install");
            var systemDir = Path.Combine(root, "systemdata");
            sessionDir = Path.Combine(root, "sessions");
            systemDb = Path.Combine(systemDir, "etlsql.db");
            Directory.CreateDirectory(installDir);
            Directory.CreateDirectory(systemDir);
            Directory.CreateDirectory(sessionDir);

            File.WriteAllText(Path.Combine(installDir, "ETL-SQL.exe"), "BINARY");
            File.WriteAllText(Path.Combine(installDir, "appsettings.json"), $$"""
            {
              "Portal": {
                "DatabasePath": "./portal.db",
                "SnapshotDirectory": "./Snapshots",
                "ScriptRootPath": "./Reports",
                "DatasetRootPath": "./data/datasets",
                "MapRootPath": "./data/maps",
                "Orchestrator": { "DatabasePath": "{{systemDb.Replace("\\", "\\\\")}}" }
              },
              "Session": { "Root": "{{sessionDir.Replace("\\", "\\\\")}}" }
            }
            """);

            Directory.CreateDirectory(Path.Combine(installDir, "logs", "orchestrator"));
            File.WriteAllText(Path.Combine(installDir, "logs", "orchestrator", "orch.log"), "log");
            Directory.CreateDirectory(Path.Combine(installDir, "Snapshots"));
            File.WriteAllText(Path.Combine(installDir, "Snapshots", "s.json"), "{}");
            Directory.CreateDirectory(Path.Combine(installDir, "Reports"));
            File.WriteAllText(Path.Combine(installDir, "Reports", "r.rptsql"), "SELECT 1;");
            Directory.CreateDirectory(Path.Combine(installDir, "data", "datasets"));
            File.WriteAllText(Path.Combine(installDir, "data", "datasets", "d.parquet"), "data");
            Directory.CreateDirectory(Path.Combine(installDir, ".portal-keys"));
            File.WriteAllText(Path.Combine(installDir, ".portal-keys", "k.xml"), "<k/>");
            File.WriteAllText(Path.Combine(installDir, "portal.db"), "db");
            File.WriteAllText(Path.Combine(installDir, "portal.db-wal"), "wal");
            File.WriteAllText(Path.Combine(installDir, "appsettings.secrets.json"), "{}");
            File.WriteAllText(Path.Combine(installDir, "first-run-password.txt"), "pw");
            File.WriteAllText(systemDb, "orch");
            File.WriteAllText(systemDb + "-shm", "shm");
            File.WriteAllText(Path.Combine(sessionDir, "s.db"), "session");
            return installDir;
        }

        private static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
        }
    }
}
