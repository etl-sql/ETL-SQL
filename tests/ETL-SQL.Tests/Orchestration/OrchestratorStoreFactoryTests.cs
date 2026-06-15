using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    /// <summary>
    /// Practical HA P1.1: the Orchestrator store is obtained through a config-selected provider seam
    /// instead of constructing the SQLite store directly. SQLite (default) returns a working store;
    /// Postgres fails closed until its hand-written SQL is ported in P1.2.
    /// </summary>
    public class OrchestratorStoreFactoryTests
    {
        private static IConfiguration Config(string? provider, string? connectionString = null) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Orchestrator:Database:Provider"] = provider,
                    ["Orchestrator:Database:ConnectionString"] = connectionString
                })
                .Build();

        [Fact]
        public async Task Sqlite_CreatesUsableStore()
        {
            var factory = new OrchestratorStoreFactory(Config("Sqlite"));
            var dbPath = Path.Combine(Path.GetTempPath(), $"orch_factory_{Guid.NewGuid():N}.db");
            try
            {
                var store = factory.Create(dbPath);
                await store.InitializeAsync();
                await store.SaveJobAsync(new JobDefinition("j1", "RUN SCRIPT 'x.etlsql';", 1, "DAY", "06:00", null, null, true));

                var job = await store.GetJobAsync("j1");
                Assert.NotNull(job);
                Assert.Equal("j1", job!.Name);
            }
            finally
            {
                // SQLite pools connections, keeping the file handle open; clear the pool, then
                // best-effort delete (temp files are non-fatal to leave behind).
                SqliteConnection.ClearAllPools();
                foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                    try { if (File.Exists(p)) File.Delete(p); } catch { /* best-effort */ }
            }
        }

        [Fact]
        public void DefaultProvider_IsSqlite()
        {
            // An unset provider must behave exactly as before (SQLite), so nothing regresses by default.
            var factory = new OrchestratorStoreFactory(Config(null));
            var store = factory.Create(Path.Combine(Path.GetTempPath(), $"orch_default_{Guid.NewGuid():N}.db"));
            Assert.NotNull(store);
        }

        [Fact]
        public void Postgres_WithoutConnectionString_FailsClosed()
        {
            var factory = new OrchestratorStoreFactory(Config("Postgres"));
            var ex = Assert.Throws<InvalidOperationException>(() => factory.Create("ignored"));
            Assert.Contains("ConnectionString", ex.Message);
        }

        [Fact]
        public void Postgres_WithConnectionString_BuildsStore()
        {
            // Construction does not open a connection, so a syntactically valid string suffices here;
            // a live round-trip is covered by OrchestratorPostgresStoreTests (Testcontainers).
            var factory = new OrchestratorStoreFactory(
                Config("Postgres", "Host=localhost;Database=x;Username=u;Password=p"));
            Assert.NotNull(factory.Create());
        }

        [Fact]
        public void UnknownProvider_ThrowsAtConstruction()
        {
            Assert.Throws<ArgumentException>(() => new OrchestratorStoreFactory(Config("mariadb")));
        }
    }
}
