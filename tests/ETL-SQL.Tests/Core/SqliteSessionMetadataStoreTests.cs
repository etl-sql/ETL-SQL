using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Security;
using System.Security.Cryptography;

namespace ETL_SQL.Tests.Core;

public sealed class SqliteSessionMetadataStoreTests
{
    private static readonly SqliteSessionMetadataStoreFactory StoreFactory = new();

    [Fact]
    public async Task SharedFactoryRequiresExplicitScopeAndSeparatesCheckpointKeys()
    {
        var root = Path.Combine(Path.GetTempPath(), "etlsql-shared-checkpoint-" + Guid.NewGuid().ToString("N"));
        var provider = new ResolvedKeyMaterialProvider("vault",
        [
            (new KeyMaterialDescriptor("vault", "alpha-checkpoint", "tenant-alpha", KeyPurpose.Checkpoint, "v1"),
                Enumerable.Repeat((byte)31, 32).ToArray()),
            (new KeyMaterialDescriptor("vault", "beta-checkpoint", "tenant-beta", KeyPurpose.Checkpoint, "v1"),
                Enumerable.Repeat((byte)73, 32).ToArray())
        ]);
        var factory = new SqliteSessionMetadataStoreFactory(
            provider, new KeyMaterialHostScope("portal-host", RequireExplicitScope: true));
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() =>
                factory.Create("session-a", root, "legacy-entropy"));

            using (var alpha = factory.Create("session-a", root, "legacy-entropy", "tenant-alpha"))
            {
                await alpha.InitializeAsync();
                await alpha.SaveVariablesAsync(
                    new Dictionary<string, object?> { ["secret"] = "tenant-alpha-only" },
                    new Dictionary<string, VariableMetadata>());
            }

            using var beta = factory.Create("session-a", root, "legacy-entropy", "tenant-beta");
            await beta.InitializeAsync();
            await Assert.ThrowsAnyAsync<CryptographicException>(async () =>
                await beta.LoadVariablesAsync());
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ProviderBackedCheckpointEncryptsAllPersistedPayloadsAndRoundTrips()
    {
        var root = Path.Combine(Path.GetTempPath(), "etlsql-session-keyed-" + Guid.NewGuid().ToString("N"));
        var descriptor = new KeyMaterialDescriptor(
            "vault", "checkpoint-alpha", "tenant-alpha", KeyPurpose.Checkpoint, "v1");
        var provider = new ResolvedKeyMaterialProvider("vault",
            [(descriptor, Enumerable.Repeat((byte)91, 32).ToArray())]);
        try
        {
            using (var store = new SqliteSessionMetadataStore(
                       "session-a", root, "legacy-entropy", provider, "tenant-alpha"))
            {
                await store.InitializeAsync();
                await store.SaveVariablesAsync(
                    new Dictionary<string, object?> { ["customer"] = "checkpoint-secret-value" },
                    new Dictionary<string, VariableMetadata>
                    {
                        ["customer"] = new() { IsSensitive = true }
                    });
                await store.SaveTempTablesAsync(
                    [new SavedTempTable("#stage", [new("secret_column", "TEXT", false)], ["secret-chunk.arrow"])]);
                await store.SaveConnectionsAsync(
                [
                    new ETL_SQL.Core.Data.ConnectionInfo
                    {
                        Name = "warehouse",
                        Type = "MSSQL",
                        ConnectionString = "Password=checkpoint-db-secret"
                    }
                ]);
                await store.SaveDockerStateAsync("Password=docker-secret",
                    new Dictionary<string, string> { ["warehouse"] = "Password=docker-secret" });

                var (variables, _) = await store.LoadVariablesAsync();
                Assert.Equal("checkpoint-secret-value", variables["customer"]);
                Assert.Equal("secret-chunk.arrow", Assert.Single(await store.LoadAllTempTablesAsync()).ChunkNames[0]);
                Assert.Contains("checkpoint-db-secret",
                    Assert.Single(await store.LoadConnectionsAsync()).ConnectionString);
                Assert.Contains("docker-secret", (await store.LoadDockerStateAsync()).LastConn);
            }

            var databaseBytes = await File.ReadAllBytesAsync(Path.Combine(root, "session-a", "metadata.db"));
            var databaseText = System.Text.Encoding.UTF8.GetString(databaseBytes);
            Assert.DoesNotContain("checkpoint-secret-value", databaseText, StringComparison.Ordinal);
            Assert.DoesNotContain("secret_column", databaseText, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-chunk.arrow", databaseText, StringComparison.Ordinal);
            Assert.DoesNotContain("checkpoint-db-secret", databaseText, StringComparison.Ordinal);
            Assert.DoesNotContain("docker-secret", databaseText, StringComparison.Ordinal);
            Assert.Contains("km1:Checkpoint:v1:", databaseText, StringComparison.Ordinal);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Constructor_RejectsSessionIdTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "etlsql-session-root-" + Guid.NewGuid().ToString("N"));

        Assert.Throws<ArgumentException>(() =>
            StoreFactory.Create(Path.Combine("..", "escape"), root, "test-entropy"));
    }

    [Fact]
    public async Task InitializeAsync_AllowsSemicolonInSessionRootPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "etlsql-session;root-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = StoreFactory.Create("session-a", root, "test-entropy");
            await store.InitializeAsync();
            await store.SaveVariablesAsync(
                new Dictionary<string, object?> { ["answer"] = 42L },
                new Dictionary<string, VariableMetadata>());

            var (variables, _) = await store.LoadVariablesAsync();

            Assert.Equal(42L, Convert.ToInt64(variables["answer"]));
            Assert.True(File.Exists(Path.Combine(root, "session-a", "metadata.db")));
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task LoadAllTempTablesAsync_RoundTripsTablesAndChunksInSinglePassShape()
    {
        var root = Path.Combine(Path.GetTempPath(), "etlsql-session-temp-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = StoreFactory.Create("session-a", root, "test-entropy");
            await store.InitializeAsync();

            var schema = new List<ColumnDefinition>
            {
                new("id", "INT", false),
                new("name", "TEXT", false)
            };
            await store.SaveTempTablesAsync([
                new SavedTempTable("#empty", schema, []),
                new SavedTempTable("#orders", schema, ["orders-1.arrow", "orders-2.arrow"]),
                new SavedTempTable("#customers", schema, ["customers-1.arrow"])
            ]);

            var tables = (await store.LoadAllTempTablesAsync())
                .OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Assert.Equal(["#customers", "#empty", "#orders"], tables.Select(t => t.TableName).ToArray());
            Assert.Equal(["customers-1.arrow"], tables[0].ChunkNames);
            Assert.Empty(tables[1].ChunkNames);
            Assert.Equal(["orders-1.arrow", "orders-2.arrow"], tables[2].ChunkNames);
            Assert.All(tables, table => Assert.Equal(["id", "name"], table.Schema.Select(c => c.ColumnName).ToArray()));
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task SaveVariablesAndConnections_BatchesLargeStateRoundTrip()
    {
        var root = Path.Combine(Path.GetTempPath(), "etlsql-session-large-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = StoreFactory.Create("session-a", root, "test-entropy");
            await store.InitializeAsync();

            var variables = Enumerable.Range(0, 750)
                .ToDictionary(i => $"v{i:000}", i => (object?)i);
            var metadata = variables.Keys.ToDictionary(
                name => name,
                name => new VariableMetadata { IsSensitive = name.EndsWith("0", StringComparison.Ordinal) });
            var connections = Enumerable.Range(0, 475)
                .Select(i => new ETL_SQL.Core.Data.ConnectionInfo
                {
                    Name = $"conn{i:000}",
                    Type = "SQLITE",
                    ConnectionString = $"Data Source=db{i:000}.sqlite",
                    Options = new Dictionary<string, string> { ["mode"] = "readonly" }
                })
                .ToList();

            await store.SaveVariablesAsync(variables, metadata);
            await store.SaveConnectionsAsync(connections);

            var (loadedVariables, loadedMetadata) = await store.LoadVariablesAsync();
            var loadedConnections = (await store.LoadConnectionsAsync())
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Assert.Equal(750, loadedVariables.Count);
            Assert.Equal(749L, Convert.ToInt64(loadedVariables["v749"]));
            Assert.True(loadedMetadata["v000"].IsSensitive);
            Assert.False(loadedMetadata["v001"].IsSensitive);
            Assert.Equal(475, loadedConnections.Count);
            Assert.Equal("conn000", loadedConnections[0].Name);
            Assert.Equal("Data Source=db474.sqlite", loadedConnections[^1].ConnectionString);
            Assert.Equal("readonly", loadedConnections[^1].Options["mode"]);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
