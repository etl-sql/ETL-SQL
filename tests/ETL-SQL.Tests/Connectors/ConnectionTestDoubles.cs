using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;
using Moq;

namespace ETL_SQL.Tests.Connectors;

/// <summary>Shared doubles for CREATE/ALTER CONNECTION handler tests.</summary>
internal static class ConnectionTestDoubles
{
    public static Mock<IConnectorRegistry> Registry(IConnector connector, string type = "CAPTURE")
    {
        var registry = new Mock<IConnectorRegistry>();
        registry.Setup(r => r.GetConnector(type)).Returns(connector);
        return registry;
    }

    public static IExecutionContext Context(Dictionary<string, IDataSource>? connections = null)
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
}

internal sealed class DictionarySecretProvider(params (string Name, string Value)[] secrets) : ISecretProvider
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

internal sealed class CapturingConnector : IConnector
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

internal sealed class CapturingDataSource(
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
