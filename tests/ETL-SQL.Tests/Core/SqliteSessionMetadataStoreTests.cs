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
}
