using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;
using ETL_SQL.Engine.Handlers;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

public class ConnectionSecretReferenceTests
{
    [Fact]
    public async Task CreateConnection_ResolvesSecretReferenceInSensitiveOption()
    {
        var connector = new CapturingConnector();
        var registry = Registry(connector);
        var connections = new Dictionary<string, IDataSource>(StringComparer.OrdinalIgnoreCase);
        var context = Context(connections);
        var handler = new CreateConnectionStatementHandler(
            registry.Object,
            new Mock<ILogger>().Object,
            secretProvider: new DictionarySecretProvider(("sales_db_password", "resolved-password")));
        var statement = new CreateConnectionStatement(
            "sales",
            "CAPTURE",
            options: new Dictionary<string, Expression>
            {
                ["PASSWORD"] = new LiteralExpression("SECRET:sales_db_password", TokenType.STRING_LITERAL),
                ["DATABASE"] = new LiteralExpression("Sales", TokenType.STRING_LITERAL)
            });

        await handler.Execute(statement, context);

        Assert.Equal("resolved-password", connector.LastOptions?["PASSWORD"]);
        Assert.Equal("Sales", connector.LastOptions?["DATABASE"]);
        Assert.True(connections.ContainsKey("sales"));
    }

    [Fact]
    public async Task CreateConnection_ResolvesSecretReferenceInConnectionStringField()
    {
        var connector = new CapturingConnector();
        var registry = Registry(connector);
        var context = Context();
        var handler = new CreateConnectionStatementHandler(
            registry.Object,
            new Mock<ILogger>().Object,
            secretProvider: new DictionarySecretProvider(("sales_db_password", "resolved-password")));
        var statement = new CreateConnectionStatement(
            "sales",
            "CAPTURE",
            new LiteralExpression("Server=db;Password=SECRET:sales_db_password;Database=Sales", TokenType.STRING_LITERAL));

        await handler.Execute(statement, context);

        Assert.Equal("Server=db;Password=resolved-password;Database=Sales", connector.LastConnectionString);
    }

    [Fact]
    public async Task AlterConnection_ResolvesSecretReferenceInSensitiveOption()
    {
        var connector = new CapturingConnector();
        var registry = Registry(connector);
        var connections = new Dictionary<string, IDataSource>(StringComparer.OrdinalIgnoreCase);
        connections["sales"] = new CapturingDataSource("CAPTURE", "", new Dictionary<string, string>
        {
            ["DATABASE"] = "Sales"
        });
        var context = Context(connections);
        var handler = new AlterConnectionStatementHandler(
            registry.Object,
            new Mock<ILogger>().Object,
            new DictionarySecretProvider(("sales_db_password", "resolved-password")));
        var statement = new AlterConnectionStatement(
            "sales",
            null,
            null,
            new Dictionary<string, Expression>
            {
                ["PASSWORD"] = new LiteralExpression("SECRET:sales_db_password", TokenType.STRING_LITERAL)
            });

        await handler.Execute(statement, context);

        Assert.Equal("resolved-password", connector.LastOptions?["PASSWORD"]);
        Assert.Equal("Sales", connector.LastOptions?["DATABASE"]);
    }

    [Fact]
    public async Task CreateConnection_NonSensitiveOption_DoesNotResolveSecretReference()
    {
        var connector = new CapturingConnector();
        var registry = Registry(connector);
        var context = Context();
        var handler = new CreateConnectionStatementHandler(
            registry.Object,
            new Mock<ILogger>().Object,
            secretProvider: new DictionarySecretProvider(("not_used", "resolved")));
        var statement = new CreateConnectionStatement(
            "sales",
            "CAPTURE",
            options: new Dictionary<string, Expression>
            {
                ["LABEL"] = new LiteralExpression("SECRET:not_used", TokenType.STRING_LITERAL)
            });

        await handler.Execute(statement, context);

        Assert.Equal("SECRET:not_used", connector.LastOptions?["LABEL"]);
    }

    private static Mock<IConnectorRegistry> Registry(IConnector connector)
    {
        var registry = new Mock<IConnectorRegistry>();
        registry.Setup(r => r.GetConnector("CAPTURE")).Returns(connector);
        return registry;
    }

    private static IExecutionContext Context(Dictionary<string, IDataSource>? connections = null)
    {
        var context = new Mock<IExecutionContext>();
        context.SetupGet(c => c.Connections).Returns(connections ?? new Dictionary<string, IDataSource>(StringComparer.OrdinalIgnoreCase));
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        context.SetupGet(c => c.InteractiveMode).Returns(false);
        context.SetupGet(c => c.IsWhatIf).Returns(false);
        context.SetupProperty(c => c.LastResult);
        context.Setup(c => c.EvaluateValue(It.IsAny<Expression?>(), It.IsAny<Row>(), It.IsAny<bool>()))
            .Returns<Expression?, Row, bool>((expr, _, _) => new ValueTask<object?>(expr is LiteralExpression literal ? literal.Value : null));
        return context.Object;
    }

    private sealed class DictionarySecretProvider(params (string Name, string Value)[] secrets) : ISecretProvider
    {
        private readonly Dictionary<string, string> _secrets = secrets.ToDictionary(
            secret => secret.Name,
            secret => secret.Value,
            StringComparer.OrdinalIgnoreCase);

        public string ProviderName => "Test";

        public Task<SecretResolutionResult> ResolveAsync(string name, CancellationToken cancellationToken = default)
        {
            if (!_secrets.TryGetValue(name, out var value))
                throw new KeyNotFoundException(name);

            return Task.FromResult(new SecretResolutionResult(name, value, ProviderName));
        }
    }

    private sealed class CapturingConnector : IConnector
    {
        public string? LastConnectionString { get; private set; }
        public Dictionary<string, string>? LastOptions { get; private set; }

        public string Name => "CAPTURE";
        public IReadOnlyList<string> Aliases => Array.Empty<string>();
        public Task<string> GetVersionAsync(IExecutionContext context, string connectionString) => Task.FromResult("1.0");
        public HashSet<string> GetSupportedFunctions() => new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> GetSupportedKeywords() => new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string[]> GetOptionValues() => new(StringComparer.OrdinalIgnoreCase);
        public string GetHelp() => "Capturing connector.";

        public string BuildConnectionString(Dictionary<string, string> properties)
        {
            LastOptions = new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase);
            return string.Join(";", properties.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        }

        public IDataSource CreateDataSource(
            IExecutionContext context,
            string connectionString,
            Dictionary<string, string>? options = null)
        {
            LastConnectionString = connectionString;
            LastOptions = options == null ? null : new Dictionary<string, string>(options, StringComparer.OrdinalIgnoreCase);
            return new CapturingDataSource(Name, connectionString, LastOptions);
        }

        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
    }

    private sealed class CapturingDataSource(
        string connectorType,
        string path,
        Dictionary<string, string>? options) : IDataSource
    {
        public string Path => path;
        public Dictionary<string, string>? Options => options;
        public string ConnectorType => connectorType;
        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => AsyncEnumerable.Empty<DataTable>();
        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => Task.CompletedTask;
        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName) => this;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
