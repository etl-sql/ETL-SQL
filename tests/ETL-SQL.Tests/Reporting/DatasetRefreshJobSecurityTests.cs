using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Reporting
{
    /// <summary>
    /// Scheduled refresh jobs must remain parseable and must never persist source credentials or the
    /// portal at-rest key. ENCRYPT = PORTAL remains covered as the runtime secret-resolution mechanism
    /// for any persisted connector definition that needs it.
    /// </summary>
    public class DatasetRefreshJobSecurityTests
    {
        private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize()).Parse();

        [Fact]
        public void EncryptPortal_ResolvesEnvKey_RoundTripsWithPassword()
        {
            const string key = "cG9ydGFsLWF0LXJlc3Qta2V5LXBvcnRhbA==";
            var prev = Environment.GetEnvironmentVariable(EncryptionOptions.PortalAtRestKeyEnvVar);
            var dir = Path.Combine(Path.GetTempPath(), "etlsql_portalenc_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            try
            {
                Environment.SetEnvironmentVariable(EncryptionOptions.PortalAtRestKeyEnvVar, key);

                var plain = Path.Combine(dir, "plain.bin");
                File.WriteAllText(plain, "hello dataset at-rest");
                var encrypted = Path.Combine(dir, "enc.bin");
                var decrypted = Path.Combine(dir, "dec.bin");

                // Encrypt with ENCRYPT=PORTAL (key from env), decrypt with ENCRYPT=PASSWORD + the same key.
                new EncryptionOptions(new Dictionary<string, string> { ["ENCRYPT"] = "PORTAL" })
                    .EncryptFile(plain, encrypted);
                new EncryptionOptions(new Dictionary<string, string> { ["ENCRYPT"] = "PASSWORD", ["PASSWORD"] = key })
                    .DecryptFile(encrypted, decrypted);

                Assert.Equal(File.ReadAllText(plain), File.ReadAllText(decrypted));
            }
            finally
            {
                Environment.SetEnvironmentVariable(EncryptionOptions.PortalAtRestKeyEnvVar, prev);
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        [Fact]
        public void EncryptPortal_EnvUnset_Throws()
        {
            var prev = Environment.GetEnvironmentVariable(EncryptionOptions.PortalAtRestKeyEnvVar);
            try
            {
                Environment.SetEnvironmentVariable(EncryptionOptions.PortalAtRestKeyEnvVar, null);
                Assert.Throws<ExecutionException>(() =>
                    new EncryptionOptions(new Dictionary<string, string> { ["ENCRYPT"] = "PORTAL" }));
            }
            finally
            {
                Environment.SetEnvironmentVariable(EncryptionOptions.PortalAtRestKeyEnvVar, prev);
            }
        }

        [Fact]
        public void RefreshEvery_IsRejectedWithTheNormalizedReplacement()
        {
            var script = Parse(
                "CREATE DATASET &sales TTL = '1h' REFRESH EVERY '1m' AS (SELECT 1 AS v);");

            var diagnostic = Assert.Single(script.Diagnostics);
            Assert.Contains("CREATE SCHEDULE", diagnostic.Message, StringComparison.Ordinal);
            Assert.Contains("FOR REPORT", diagnostic.Message, StringComparison.Ordinal);
        }

        // Minimal in-memory registry that writes parquet under a temp root.
        private sealed class TempRegistry(string root) : IDatasetRegistry
        {
            private readonly Dictionary<string, DatasetMetadata> _items = new();
            private int _nextId = 1;
            public (int ReportId, string JobName, string Interval)? RefreshJob { get; private set; }

            public Task<int> RegisterOrUpdate(DatasetMetadata metadata)
            {
                if (metadata.Id == 0) metadata.Id = _nextId++;
                _items[metadata.Name] = metadata;
                return Task.FromResult(metadata.Id);
            }
            public Task<DatasetMetadata?> Lookup(string name, string callerPermissions = "")
                => Task.FromResult(_items.TryGetValue(name, out var m) ? m : null);
            public Task<bool> Exists(string name) => Task.FromResult(_items.ContainsKey(name));
            public Task<bool> CanEditAsync(string name, string callerPermissions) => Task.FromResult(true);
            public Task SetStale(string name) => Task.CompletedTask;
            public Task<IEnumerable<DatasetMetadata>> ListAll(string callerPermissions)
                => Task.FromResult<IEnumerable<DatasetMetadata>>(_items.Values.ToList());
            public Task Delete(string name) => Task.CompletedTask;
            public Task RegisterRefreshJobAsync(int reportId, string orchestratorJobName, string refreshInterval)
            {
                RefreshJob = (reportId, orchestratorJobName, refreshInterval);
                return Task.CompletedTask;
            }
            public string BuildDatasetFilePath(int datasetId, string name)
                => Path.Combine(root, $"{name.TrimStart('&', '#')}_{datasetId}.parquet");
        }
    }
}
