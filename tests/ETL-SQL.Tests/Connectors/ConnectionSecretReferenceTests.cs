using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
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
    public async Task CreateConnection_ResolvesSecretReferenceInAccessKeyOption()
    {
        var connector = new CapturingConnector();
        var registry = Registry(connector);
        var context = Context();
        var handler = new CreateConnectionStatementHandler(
            registry.Object,
            new Mock<ILogger>().Object,
            secretProvider: new DictionarySecretProvider(("archive_access_key", "resolved-access-key")));
        var statement = new CreateConnectionStatement(
            "archive",
            "CAPTURE",
            options: new Dictionary<string, Expression>
            {
                ["ACCESS_KEY"] = new LiteralExpression("SECRET:archive_access_key", TokenType.STRING_LITERAL)
            });

        await handler.Execute(statement, context);

        Assert.Equal("resolved-access-key", connector.LastOptions?["ACCESS_KEY"]);
    }

    [Fact]
    public async Task CreateConnection_NonCredentialOption_RejectsSecretReference()
    {
        var connector = new CapturingConnector();
        var registry = Registry(connector);
        var context = Context();
        var handler = new CreateConnectionStatementHandler(
            registry.Object,
            new Mock<ILogger>().Object,
            secretProvider: new DictionarySecretProvider(("bucket_name", "prod-bucket")));
        var statement = new CreateConnectionStatement(
            "archive",
            "CAPTURE",
            options: new Dictionary<string, Expression>
            {
                ["BUCKET"] = new LiteralExpression("SECRET:bucket_name", TokenType.STRING_LITERAL)
            });

        var ex = await Assert.ThrowsAsync<ExecutionException>(() => handler.Execute(statement, context));

        Assert.Contains("BUCKET", ex.Message);
        Assert.Contains("credential fields", ex.Message);
        Assert.Null(connector.LastOptions);
    }

    [Fact]
    public async Task CreateConnection_NonCredentialConnectionStringField_RejectsSecretReference()
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
            new LiteralExpression("Server=db;Bucket=SECRET:bucket_name;Password=SECRET:sales_db_password", TokenType.STRING_LITERAL));

        var ex = await Assert.ThrowsAsync<ExecutionException>(() => handler.Execute(statement, context));

        Assert.Contains("Bucket", ex.Message);
        Assert.Null(connector.LastConnectionString);
    }

    private static Mock<IConnectorRegistry> Registry(IConnector connector) =>
        ConnectionTestDoubles.Registry(connector);

    private static IExecutionContext Context(Dictionary<string, IDataSource>? connections = null) =>
        ConnectionTestDoubles.Context(connections);
}
