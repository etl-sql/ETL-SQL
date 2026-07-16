using System.Security.Claims;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;
using ETL_SQL.ReportPortal.Services;

namespace ETL_SQL.ReportPortal.Tests;

public sealed class PortalDesignerSchemaServiceTests
{
    [Fact]
    public async Task RegisterResolvedConnectionAsync_AppliesSchemaObjectLimits()
    {
        var dataSource = new FakeDataSource(
            ["Z_ignored", "A_orders", "B_customers"],
            ["Id", "Name"]);
        var metadata = new RecordingMetadataManager();
        var service = new PortalDesignerSchemaService(
            new FakeCatalogProvider(),
            new FakeSecretProvider(),
            new FakeConnectorRegistry(new FakeConnector(dataSource)),
            metadata,
            new PortalConfig
            {
                DesignerLimits = new PortalDesignerLimitsConfig
                {
                    MaxSchemaTables = 2,
                    MaxSchemaColumnsPerTable = 1,
                    MaxSchemaColumnConcurrency = 1
                }
            });

        var response = await service.RegisterResolvedConnectionAsync(
            "sales",
            new SharedConnectionDefinition("sales", "FAKE", "target", new Dictionary<string, string>(), false),
            "portal-designer://u/1/c/sales/default");

        Assert.Equal(["A_orders", "B_customers"], response.Tables.Select(t => t.Name));
        Assert.All(response.Tables, table => Assert.Single(table.Columns));
        Assert.Equal(["A_orders", "B_customers"], metadata.Tables);
        Assert.All(metadata.Columns.Values, columns => Assert.Single(columns));
    }

    [Fact]
    public async Task GetSchemaAsync_CoalescesConcurrentDiscoveryForSameDocumentConnection()
    {
        var dataSource = new FakeDataSource(["Orders"], ["Id"]);
        var service = new PortalDesignerSchemaService(
            new FakeCatalogProvider(),
            new FakeSecretProvider(),
            new FakeConnectorRegistry(new FakeConnector(dataSource)),
            new RecordingMetadataManager(),
            new PortalConfig());
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1")]));

        var first = service.GetSchemaAsync("sales", user, "report.rptsql");
        var second = service.GetSchemaAsync("sales", user, "report.rptsql");
        await Task.WhenAll(first, second);

        Assert.Equal(1, dataSource.GetTablesCalls);
    }

    private sealed class FakeCatalogProvider : IConnectionCatalogProvider
    {
        public string ProviderName => "Fake";

        public Task<SharedConnectionDefinition> ResolveAsync(
            string alias,
            ExecutionIdentity? identity = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SharedConnectionDefinition(alias, "FAKE", "target", new Dictionary<string, string>(), false));
    }

    private sealed class FakeSecretProvider : ISecretProvider
    {
        public string ProviderName => "Fake";
        public Task<SecretResolutionResult> ResolveAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SecretResolutionResult(name, $"resolved-{name}", ProviderName));
    }

    private sealed class FakeConnectorRegistry(IConnector connector) : IConnectorRegistry
    {
        public void Register(IConnector connector) { }
        public IConnector? GetConnector(string name) => connector;
        public IEnumerable<string> GetRegisteredNames() => [connector.Name];
        public HashSet<string> GetAllConnectorKeywords() => [];
        public HashSet<string> GetAllConnectorFunctions() => [];
        public Dictionary<string, string[]> GetAllConnectorOptionValues() => [];
    }

    private sealed class FakeConnector(FakeDataSource dataSource) : IConnector
    {
        public string Name => "FAKE";
        public IReadOnlyList<string> Aliases => [];
        public Task<string> GetVersionAsync(IExecutionContext context, string connectionString) => Task.FromResult("1");
        public HashSet<string> GetSupportedFunctions() => [];
        public HashSet<string> GetSupportedKeywords() => [];
        public Dictionary<string, string[]> GetSupportedOptions() => [];
        public Dictionary<string, string[]> GetOptionValues() => [];
        public string GetHelp() => "";
        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null) => dataSource;
        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => dataSource.GetTablesAsync();
        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => dataSource.GetColumnsAsync(tableName);
        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        public string BuildConnectionString(Dictionary<string, string> properties) => "built";
    }

    private sealed class FakeDataSource(IReadOnlyList<string> tables, IReadOnlyList<string> columns) : IDatabaseSource
    {
        private int _getTablesCalls;
        public int GetTablesCalls => _getTablesCalls;
        public string Path => "";
        public Dictionary<string, string>? Options => null;
        public string ConnectorType => "FAKE";
        public string ConnectionString => "target";
        public string Dialect => "fake";
        public bool SupportsSqlPushdown => false;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => AsyncEnumerable.Empty<DataTable>();
        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => Task.CompletedTask;
        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult<IEnumerable<string>>(columns);
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName) => this;
        public async Task<IEnumerable<string>> GetTablesAsync()
        {
            Interlocked.Increment(ref _getTablesCalls);
            await Task.Delay(50);
            return tables;
        }
        public Task<string> GetVersionAsync() => Task.FromResult("1");
        public HashSet<string> GetSupportedFunctions() => [];
        public IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null) => AsyncEnumerable.Empty<DataTable>();
        public Task<IEnumerable<string>> GetViewsAsync() => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(string tableName) => Task.FromResult<IEnumerable<string>>(columns);
    }

    private sealed class RecordingMetadataManager : IMetadataManager
    {
        public bool DebugMode { get; set; }
        public IReadOnlyList<string> Tables { get; private set; } = [];
        public Dictionary<string, List<ColumnMetadata>> Columns { get; } = new(StringComparer.OrdinalIgnoreCase);
        public void RegisterConnection(string name, string type, string connectionString) { }
        public void RegisterDocumentConnection(string uri, string name, string type, string connectionString) { }
        public void RegisterDocumentMetadata(
            string uri,
            string name,
            string type,
            IEnumerable<string> tables,
            IReadOnlyDictionary<string, IEnumerable<ColumnMetadata>> columns,
            IEnumerable<string>? views = null)
        {
            Tables = tables.ToList();
            foreach (var (table, tableColumns) in columns)
                Columns[table] = tableColumns.ToList();
        }
        public void ClearDocumentConnections(string uri) { }
        public List<ConnectionInfo> GetConnections(string? uri = null) => [];
        public Task<IEnumerable<string>> GetTablesAsync(string connectionName, string? uri = null) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetViewsAsync(string connectionName, string? uri = null) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetTempTablesAsync(string? uri = null) => Task.FromResult(Enumerable.Empty<string>());
        public void RegisterTempTable(string uri, string name, List<string> columns) { }
        public void ClearTempTables(string uri) { }
        public Task<IEnumerable<string>> GetColumnsAsync(string connectionName, string tableName, string? uri = null) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<ColumnMetadata>> GetColumnDetailsAsync(string connectionName, string tableName, string? uri = null) => Task.FromResult(Enumerable.Empty<ColumnMetadata>());
        public IEnumerable<string> GetRegisteredNames() => [];
        public IConnector? GetConnector(string name) => null;
        public string? GetConnectionType(string connectionName, string? uri = null) => null;
        public void ClearCache() { }
        public void ClearCacheForUri(string uri) { }
        public void CleanUpDocumentConnectionsAndTempTables(string uri, IEnumerable<string> activeConnectionNames, IEnumerable<string> activeTempTableNames) { }
    }
}
