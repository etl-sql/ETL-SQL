using ETL_SQL.Data;

namespace ETL_SQL.Tests.Core;

public sealed class DataSourceConfigRedactionTests
{
    [Fact]
    public void GetConfig_MasksSensitiveConnectorOptions()
    {
        var source = new ConfigOnlyDataSource(new Dictionary<string, string>
        {
            ["SERVER"] = "sql01",
            ["PWD"] = "cleartext",
            ["CLIENT_SECRET"] = "client-secret",
            ["ACCOUNT_KEY"] = "account-key",
            ["MESSAGE"] = "Authorization=Bearer bearer-token"
        });

        var config = ((IDataSource)source).GetConfig();

        Assert.Equal("sql01", config["SERVER"]);
        Assert.Equal("********", config["PWD"]);
        Assert.Equal("********", config["CLIENT_SECRET"]);
        Assert.Equal("********", config["ACCOUNT_KEY"]);
        Assert.DoesNotContain("bearer-token", config["MESSAGE"]);
    }

    private sealed class ConfigOnlyDataSource(Dictionary<string, string> options) : IDataSource
    {
        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => AsyncEnumerable.Empty<DataTable>();
        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => Task.CompletedTask;
        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName) => this;
        public string Path => "";
        public Dictionary<string, string>? Options { get; } = options;
        public string ConnectorType => "TEST";
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
