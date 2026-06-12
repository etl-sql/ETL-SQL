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
            _baseDir = Path.Combine(Path.GetTempPath(), "etlsql_purge_test_" + Guid.NewGuid().ToString("N"));
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
            var historyDefault = Path.GetFullPath(Path.Combine(localApp, "ETL-SQL", "etlsql.db"));

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
    }
}
