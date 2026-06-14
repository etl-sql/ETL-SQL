using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ETL_SQL.Tests.CliCommands
{
    /// <summary>
    /// Operator Tooling Phase 2 (Backup & DR): split-custody backup, restore, and fail-closed
    /// validation. Backs up a fabricated workspace, then proves the data/keys split, the round-trip
    /// restore (secrets re-injected), and that validation rejects a mismatched archive pair.
    /// </summary>
    public class BackupRestoreServiceTests : IDisposable
    {
        private readonly string _root;
        private readonly string _source;

        public BackupRestoreServiceTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "etlsql_backup_test_" + Guid.NewGuid().ToString("N"));
            _source = Path.Combine(_root, "source");
            Directory.CreateDirectory(_source);
            SeedWorkspace();
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }

        private const string AtRestKey = "ATREST-KEY-VALUE";
        private const string JwtSecret = "JWT-SECRET-VALUE";

        private void SeedWorkspace()
        {
            File.WriteAllText(Path.Combine(_source, "appsettings.json"), $$"""
            {
              "Portal": {
                "DatabasePath": "./portal.db",
                "SnapshotDirectory": "./Snapshots",
                "ScriptRootPath": "./Reports",
                "DatasetRootPath": "./data/datasets",
                "MapRootPath": "./data/maps",
                "Jwt": { "Secret": "{{JwtSecret}}", "ExpiryMinutes": 60 },
                "Dataset": {
                  "AtRestKey": "{{AtRestKey}}",
                  "AtRestKeyVersion": "v1",
                  "PreviousAtRestKeys": { "v0": "OLD-KEY" }
                }
              },
              "Orchestrator": { "HistoryDbPath": "./etlsql.db" }
            }
            """);

            File.WriteAllText(Path.Combine(_source, "portal.db"), "PORTAL-DB-BYTES");
            File.WriteAllText(Path.Combine(_source, "portal.db-wal"), "WAL");
            File.WriteAllText(Path.Combine(_source, "etlsql.db"), "ORCH-DB-BYTES");
            WriteUnder("Snapshots", "snap.json", "{}");
            WriteUnder("Reports", "rev.rptsql", "SELECT 1;");
            WriteUnder(Path.Combine("data", "datasets"), "sales.parquet", "ENCRYPTED-PARQUET");
            WriteUnder(Path.Combine("data", "maps"), "world.json", "{}");
            WriteUnder(".portal-keys", "key-abc.xml", "<key/>");
        }

        private void WriteUnder(string dir, string file, string content)
        {
            var full = Path.Combine(_source, dir);
            Directory.CreateDirectory(full);
            File.WriteAllText(Path.Combine(full, file), content);
        }

        private IConfiguration Config() =>
            new ConfigurationBuilder().AddJsonFile(Path.Combine(_source, "appsettings.json")).Build();

        private (string dataZip, string keysZip) FindArchives(string dir)
        {
            var data = Directory.GetFiles(dir, "etl-sql-backup-*.zip");
            var keys = Directory.GetFiles(dir, "etl-sql-keys-*.zip");
            Assert.Single(data);
            Assert.Single(keys);
            return (data[0], keys[0]);
        }

        [Fact]
        public async Task Backup_SplitsSecretsOutOfDataArchiveIntoKeysArchive()
        {
            var outDir = Path.Combine(_root, "out");
            var exit = await BackupRestoreService.BackupCoreAsync(Config(), _source, outDir, NullLogger.Instance);
            Assert.Equal(0, exit);

            var (dataZip, keysZip) = FindArchives(outDir);

            var dataExtract = Path.Combine(_root, "data-x");
            var keysExtract = Path.Combine(_root, "keys-x");
            ZipFile.ExtractToDirectory(dataZip, dataExtract);
            ZipFile.ExtractToDirectory(keysZip, keysExtract);

            // Data archive config carries NO secret values.
            var dataConfigText = File.ReadAllText(Path.Combine(dataExtract, "appsettings.json"));
            Assert.DoesNotContain(AtRestKey, dataConfigText);
            Assert.DoesNotContain(JwtSecret, dataConfigText);
            Assert.DoesNotContain("OLD-KEY", dataConfigText);

            // Keys archive carries the secrets and the Data Protection key ring.
            var secretsText = File.ReadAllText(Path.Combine(keysExtract, "secrets.json"));
            Assert.Contains(AtRestKey, secretsText);
            Assert.Contains(JwtSecret, secretsText);
            Assert.True(File.Exists(Path.Combine(keysExtract, ".portal-keys", "key-abc.xml")));

            // Encrypted dataset cache + databases are in the DATA archive (useless without the keys).
            Assert.True(File.Exists(Path.Combine(dataExtract, "db", "portal.db")));
            Assert.True(File.Exists(Path.Combine(dataExtract, "db", "portal.db-wal")));
            Assert.True(File.Exists(Path.Combine(dataExtract, "content", "datasets", "sales.parquet")));
        }

        [Fact]
        public async Task RoundTrip_RestoreReinjectsSecretsAndMaterializesLayout()
        {
            var outDir = Path.Combine(_root, "out");
            await BackupRestoreService.BackupCoreAsync(Config(), _source, outDir, NullLogger.Instance);
            var (dataZip, keysZip) = FindArchives(outDir);

            // Validate-only writes nothing and passes.
            var validateCtx = new CliContext
            {
                Command = "admin-restore",
                RestoreFrom = dataZip,
                RestoreKeys = keysZip,
                RestoreValidateOnly = true
            };
            Assert.Equal(0, await BackupRestoreService.RestoreAsync(validateCtx, NullLogger.Instance));

            // Restore into a clean directory.
            var restoreDir = Path.Combine(_root, "restored");
            var restoreCtx = new CliContext
            {
                Command = "admin-restore",
                RestoreFrom = dataZip,
                RestoreKeys = keysZip,
                RestoreTo = restoreDir
            };
            Assert.Equal(0, await BackupRestoreService.RestoreAsync(restoreCtx, NullLogger.Instance));

            // Layout materialized.
            Assert.True(File.Exists(Path.Combine(restoreDir, "portal.db")));
            Assert.True(File.Exists(Path.Combine(restoreDir, "etlsql.db")));
            Assert.True(File.Exists(Path.Combine(restoreDir, "Reports", "rev.rptsql")));
            Assert.True(File.Exists(Path.Combine(restoreDir, "data", "datasets", "sales.parquet")));
            Assert.True(File.Exists(Path.Combine(restoreDir, ".portal-keys", "key-abc.xml")));

            // Secrets re-injected into the restored config.
            var restored = JsonNode.Parse(File.ReadAllText(Path.Combine(restoreDir, "appsettings.json")))!.AsObject();
            Assert.Equal(AtRestKey, (string?)restored["Portal"]!["Dataset"]!["AtRestKey"]);
            Assert.Equal(JwtSecret, (string?)restored["Portal"]!["Jwt"]!["Secret"]);
            Assert.Equal("OLD-KEY", (string?)restored["Portal"]!["Dataset"]!["PreviousAtRestKeys"]!["v0"]);
        }

        [Fact]
        public async Task Restore_FailsClosed_OnMismatchedArchivePair()
        {
            // Two independent backups → two different backup ids.
            var out1 = Path.Combine(_root, "out1");
            var out2 = Path.Combine(_root, "out2");
            await BackupRestoreService.BackupCoreAsync(Config(), _source, out1, NullLogger.Instance);
            await BackupRestoreService.BackupCoreAsync(Config(), _source, out2, NullLogger.Instance);
            var (dataZip1, _) = FindArchives(out1);
            var (_, keysZip2) = FindArchives(out2);

            var ctx = new CliContext
            {
                Command = "admin-restore",
                RestoreFrom = dataZip1,
                RestoreKeys = keysZip2,     // keys from a DIFFERENT backup
                RestoreValidateOnly = true
            };

            // Fail closed: mismatched pair must not validate.
            Assert.Equal(1, await BackupRestoreService.RestoreAsync(ctx, NullLogger.Instance));
        }

        [Fact]
        public void SplitConfigSecrets_BlanksSecretsAndCapturesByDottedPath()
        {
            var path = Path.Combine(_source, "appsettings.json");
            var (stripped, secrets) = BackupRestoreService.SplitConfigSecrets(path);

            Assert.DoesNotContain(AtRestKey, stripped);
            Assert.DoesNotContain(JwtSecret, stripped);
            Assert.True(secrets.ContainsKey("Portal.Dataset.AtRestKey"));
            Assert.True(secrets.ContainsKey("Portal.Jwt.Secret"));
            Assert.Equal(AtRestKey, (string?)(JsonNode?)secrets["Portal.Dataset.AtRestKey"]);
        }
    }
}
