using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;

namespace ETL_SQL.Tests.Core;

public sealed class SqliteSessionMetadataStoreTests
{
    [Fact]
    public void Constructor_RejectsSessionIdTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "etlsql-session-root-" + Guid.NewGuid().ToString("N"));

        Assert.Throws<ArgumentException>(() =>
            new SqliteSessionMetadataStore(Path.Combine("..", "escape"), root, "test-entropy"));
    }

    [Fact]
    public async Task InitializeAsync_AllowsSemicolonInSessionRootPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "etlsql-session;root-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new SqliteSessionMetadataStore("session-a", root, "test-entropy");
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
            using var store = new SqliteSessionMetadataStore("session-a", root, "test-entropy");
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
            using var store = new SqliteSessionMetadataStore("session-a", root, "test-entropy");
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
