using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;
using ETL_SQL.Engine.Handlers;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

public class SharedConnectionTests
{
    [Fact]
    public async Task SharedReference_ExpandsCatalogEntryAndResolvesSecrets()
    {
        var connector = new CapturingConnector();
        var handler = Handler(connector,
            catalog: Catalog(new SharedConnectionDefinition(
                "my_sql_server", "CAPTURE", null,
                Options(("SERVER", "sql01"), ("DATABASE", "Sales"), ("PASSWORD", "SECRET:sales_db_password")),
                Disabled: false)),
            secrets: new DictionarySecretProvider(("sales_db_password", "resolved-password")));

        await handler.Execute(SharedCreate("m", "SHARED:my_sql_server"), ConnectionTestDoubles.Context());

        Assert.Equal("sql01", connector.LastOptions?["SERVER"]);
        Assert.Equal("Sales", connector.LastOptions?["DATABASE"]);
        Assert.Equal("resolved-password", connector.LastOptions?["PASSWORD"]);
    }

    [Fact]
    public async Task SharedReference_ScriptOptionsMergeButCannotOverrideCatalogCredentials()
    {
        var connector = new CapturingConnector();
        var catalog = Catalog(new SharedConnectionDefinition(
            "my_sql_server", "CAPTURE", null,
            Options(("SERVER", "sql01"), ("PASSWORD", "SECRET:sales_db_password")),
            Disabled: false));
        var secrets = new DictionarySecretProvider(("sales_db_password", "resolved-password"));

        // Non-credential override wins.
        var handler = Handler(connector, catalog, secrets);
        var statement = new CreateConnectionStatement(
            "m", "CAPTURE",
            new LiteralExpression("SHARED:my_sql_server", TokenType.STRING_LITERAL),
            new Dictionary<string, Expression>
            {
                ["DATABASE"] = new LiteralExpression("Analytics", TokenType.STRING_LITERAL)
            });
        await handler.Execute(statement, ConnectionTestDoubles.Context());
        Assert.Equal("Analytics", connector.LastOptions?["DATABASE"]);
        Assert.Equal("sql01", connector.LastOptions?["SERVER"]);

        // Credential override is rejected.
        var overriding = new CreateConnectionStatement(
            "m2", "CAPTURE",
            new LiteralExpression("SHARED:my_sql_server", TokenType.STRING_LITERAL),
            new Dictionary<string, Expression>
            {
                ["PASSWORD"] = new LiteralExpression("attacker-value", TokenType.STRING_LITERAL)
            });
        var ex = await Assert.ThrowsAsync<ExecutionException>(
            () => Handler(new CapturingConnector(), catalog, secrets).Execute(overriding, ConnectionTestDoubles.Context()));
        Assert.Contains("PASSWORD", ex.Message);
        Assert.Contains("cannot", ex.Message);
    }

    [Fact]
    public async Task SharedReference_EntrySensitiveFieldsResolveAndCannotBeOverridden()
    {
        var catalog = Catalog(new SharedConnectionDefinition(
            "archive", "CAPTURE", null,
            Options(("HOST", "SECRET:archive_host"), ("DATABASE", "dw")),
            Disabled: false,
            SensitiveFields: ["HOST"]));
        var secrets = new DictionarySecretProvider(("archive_host", "sftp01.internal"));

        var connector = new CapturingConnector();
        await Handler(connector, catalog, secrets)
            .Execute(SharedCreate("archive", "SHARED:archive"), ConnectionTestDoubles.Context());

        Assert.Equal("sftp01.internal", connector.LastOptions?["HOST"]);

        var overriding = new CreateConnectionStatement(
            "archive2", "CAPTURE",
            new LiteralExpression("SHARED:archive", TokenType.STRING_LITERAL),
            new Dictionary<string, Expression>
            {
                ["HOST"] = new LiteralExpression("attacker-host", TokenType.STRING_LITERAL)
            });
        var ex = await Assert.ThrowsAsync<ExecutionException>(
            () => Handler(new CapturingConnector(), catalog, secrets).Execute(overriding, ConnectionTestDoubles.Context()));
        Assert.Contains("HOST", ex.Message);
        Assert.Contains("cannot", ex.Message);
    }

    [Fact]
    public async Task SharedReference_ConnectorTypeMismatch_Fails()
    {
        var handler = Handler(new CapturingConnector(),
            Catalog(new SharedConnectionDefinition("archive", "S3", null, Options(), false)));

        var ex = await Assert.ThrowsAsync<ExecutionException>(
            () => handler.Execute(SharedCreate("m", "SHARED:archive"), ConnectionTestDoubles.Context()));

        Assert.Contains("S3", ex.Message);
        Assert.Contains("CAPTURE", ex.Message);
    }

    [Fact]
    public async Task SharedReference_UnknownAliasDisabledEntryAndNoProvider_FailClearly()
    {
        var context = ConnectionTestDoubles.Context();

        var unknown = await Assert.ThrowsAsync<ExecutionException>(
            () => Handler(new CapturingConnector(), Catalog()).Execute(SharedCreate("a", "SHARED:missing"), context));
        Assert.Contains("missing", unknown.Message);
        Assert.Contains("not found", unknown.Message);

        var disabled = await Assert.ThrowsAsync<ExecutionException>(
            () => Handler(new CapturingConnector(),
                    Catalog(new SharedConnectionDefinition("old", "CAPTURE", null, Options(), Disabled: true)))
                .Execute(SharedCreate("b", "SHARED:old"), context));
        Assert.Contains("disabled", disabled.Message);

        var noProvider = await Assert.ThrowsAsync<ExecutionException>(
            () => Handler(new CapturingConnector(), catalog: null).Execute(SharedCreate("c", "SHARED:x"), context));
        Assert.Contains("Governance:ConnectionCatalog:Provider", noProvider.Message);
    }

    private static CreateConnectionStatement SharedCreate(string name, string target) =>
        new(name, "CAPTURE", new LiteralExpression(target, TokenType.STRING_LITERAL));

    private static CreateConnectionStatementHandler Handler(
        CapturingConnector connector,
        IConnectionCatalogProvider? catalog,
        ISecretProvider? secrets = null) =>
        new(ConnectionTestDoubles.Registry(connector).Object,
            new Mock<ILogger>().Object,
            secretProvider: secrets,
            connectionCatalog: catalog);

    private static Dictionary<string, string> Options(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

    private static FakeCatalogProvider Catalog(params SharedConnectionDefinition[] definitions) =>
        new(definitions);

    private sealed class FakeCatalogProvider(SharedConnectionDefinition[] definitions) : IConnectionCatalogProvider
    {
        private readonly Dictionary<string, SharedConnectionDefinition> _entries =
            definitions.ToDictionary(d => d.Alias, d => d, StringComparer.OrdinalIgnoreCase);

        public string ProviderName => "TestCatalog";

        public Task<SharedConnectionDefinition> ResolveAsync(
            string alias,
            ExecutionIdentity? identity = null,
            CancellationToken cancellationToken = default)
        {
            if (!_entries.TryGetValue(alias, out var definition))
                throw new KeyNotFoundException(alias);
            return Task.FromResult(definition);
        }
    }
}
