using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

[Trait("CompatBreak", "0.23")]
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
    public async Task SharedReference_ScriptOptionsMergeSafeOptionsButLockDestinationsAndCredentials()
    {
        var connector = new CapturingConnector();
        var catalog = Catalog(new SharedConnectionDefinition(
            "my_sql_server", "CAPTURE", null,
            Options(("SERVER", "sql01"), ("PASSWORD", "SECRET:sales_db_password")),
            Disabled: false));
        var secrets = new DictionarySecretProvider(("sales_db_password", "resolved-password"));

        // Safe non-destination, non-credential script option merges alongside catalog options.
        var handler = Handler(connector, catalog, secrets);
        var statement = new CreateConnectionStatement(
            "m", "CAPTURE",
            new LiteralExpression("SHARED:my_sql_server", TokenType.STRING_LITERAL),
            new Dictionary<string, Expression>
            {
                ["TIMEOUT_SECONDS"] = new LiteralExpression("60", TokenType.STRING_LITERAL)
            });
        await handler.Execute(statement, ConnectionTestDoubles.Context());
        Assert.Equal("60", connector.LastOptions?["TIMEOUT_SECONDS"]);
        Assert.Equal("sql01", connector.LastOptions?["SERVER"]);

        // Credential and destination options cannot be set or overridden on SHARED: connections.
        foreach (var blockedKey in new[] { "PASSWORD", "HOST", "SERVER", "PORT", "DATABASE", "URL", "BASE_URL" })
        {
            var overriding = new CreateConnectionStatement(
                $"m_{blockedKey}", "CAPTURE",
                new LiteralExpression("SHARED:my_sql_server", TokenType.STRING_LITERAL),
                new Dictionary<string, Expression>
                {
                    [blockedKey] = new LiteralExpression("attacker-value", TokenType.STRING_LITERAL)
                });
            var ex = await Assert.ThrowsAsync<ExecutionException>(
                () => Handler(new CapturingConnector(), catalog, secrets).Execute(overriding, ConnectionTestDoubles.Context()));
            Assert.Contains(blockedKey, ex.Message);
            Assert.Contains("cannot", ex.Message);
        }

        // Script cannot inject SECRET: values through an otherwise benign option on SHARED:.
        var secretInjection = new CreateConnectionStatement(
            "m_inject", "CAPTURE",
            new LiteralExpression("SHARED:my_sql_server", TokenType.STRING_LITERAL),
            new Dictionary<string, Expression>
            {
                ["CUSTOM_HEADER"] = new LiteralExpression("SECRET:sales_db_password", TokenType.STRING_LITERAL)
            });
        await Assert.ThrowsAsync<ExecutionException>(
            () => Handler(new CapturingConnector(), catalog, secrets).Execute(secretInjection, ConnectionTestDoubles.Context()));
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
        var sink = new RecordingSecurityEventSink();
        using var eventScope = SecurityEventRuntime.UseSinkForScope(sink);

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

        AssertDenied(sink, "missing", "AliasNotFound");
        AssertDenied(sink, "old", "Disabled");
        AssertDenied(sink, "x", "CatalogProviderMissing");
    }

    [Fact]
    public async Task SharedReference_UnauthorizedProvider_EmitsUseDeniedWithoutSecrets()
    {
        var connector = new CapturingConnector();
        var sink = new RecordingSecurityEventSink();
        using var eventScope = SecurityEventRuntime.UseSinkForScope(sink);

        var statement = new CreateConnectionStatement(
            "m", "CAPTURE",
            new LiteralExpression("SHARED:restricted", TokenType.STRING_LITERAL),
            new Dictionary<string, Expression>
            {
                ["PASSWORD"] = new LiteralExpression("do-not-log", TokenType.STRING_LITERAL)
            });

        var ex = await Assert.ThrowsAsync<ExecutionException>(
            () => Handler(connector, new DenyingCatalogProvider()).Execute(statement, ConnectionTestDoubles.Context()));

        Assert.Contains("not authorized", ex.Message, StringComparison.OrdinalIgnoreCase);
        AssertDenied(sink, "restricted", "Unauthorized");
        Assert.DoesNotContain(sink.Events, e =>
            e.Reason.Contains("do-not-log", StringComparison.OrdinalIgnoreCase)
            || e.SanitizedTarget.Contains("do-not-log", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SharedReference_RecordsResolvedAliasesOnContextEvenAfterDrop()
    {
        var connector = new CapturingConnector();
        var registry = ConnectionTestDoubles.Registry(connector);
        var catalog = Catalog(
            new SharedConnectionDefinition("corp_db", "CAPTURE", null, Options(("SERVER", "sql01")), Disabled: false),
            new SharedConnectionDefinition("archive_db", "CAPTURE", null, Options(("SERVER", "sql02")), Disabled: false));
        var context = ConnectionTestDoubles.Context();

        var createHandler = new CreateConnectionStatementHandler(
            registry.Object, new Mock<ILogger>().Object, connectionCatalog: catalog);
        await createHandler.Execute(SharedCreate("corp", "SHARED:corp_db"), context);
        await createHandler.Execute(SharedCreate("arch", "SHARED:archive_db"), context);

        var dropHandler = new DropConnectionStatementHandler(new Mock<ILogger>().Object);
        await dropHandler.Execute(new DropConnectionStatement("corp", ifExists: false), context);

        Assert.False(context.CatalogBackedConnections.ContainsKey("corp"));
        Assert.True(context.SharedConnectionDependencies.SetEquals(["corp_db", "archive_db"]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Evaluator_RecordsParameterizedAndNestedRunScriptSharedConnectionDependencies(bool parallel)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "etlsql-shared-dep-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempRoot);
        try
        {
            var storePath = Path.Combine(tempRoot, "catalog");
            var sp = DependencyInjectionSetup.BuildServiceProvider(new Dictionary<string, string?>
            {
                ["Governance:ConnectionCatalog:Provider"] = "Local",
                ["Governance:ConnectionCatalog:LocalRoot"] = storePath,
                ["Governance:ConnectionCatalog:ProtectionScope"] = "CurrentUser"
            });

            var catalog = sp.GetRequiredService<IConnectionCatalogProvider>();
            await ((LocalConnectionCatalogProvider)catalog).StoreAsync(
                new SharedConnectionDefinition("dyn_db", "MOCKDB", null, new Dictionary<string, string>(), false));
            await ((LocalConnectionCatalogProvider)catalog).StoreAsync(
                new SharedConnectionDefinition("nested_db", "MOCKDB", null, new Dictionary<string, string>(), false));

            var nestedPath = Path.Combine(tempRoot, "nested.etlsql");
            await File.WriteAllTextAsync(nestedPath, "CREATE CONNECTION sub_conn AS MOCKDB('SHARED:nested_db');");

            var mainPath = Path.Combine(tempRoot, "main.etlsql");
            var mainScript = """
                DECLARE @env VARCHAR = 'dyn_db';
                CREATE CONNECTION dyn_conn AS MOCKDB('SHARED:' + @env);
                RUN SCRIPT 'nested.etlsql';
                DROP CONNECTION dyn_conn;
                """;
            if (parallel)
                mainScript = mainScript.Replace("RUN SCRIPT 'nested.etlsql';",
                    "PARALLEL BEGIN PARALLEL BEGIN RUN SCRIPT 'nested.etlsql'; END; END;");
            await File.WriteAllTextAsync(mainPath, mainScript);

            await using var evaluator = sp.GetRequiredService<Evaluator>();
            evaluator.SecurityService.ApprovedSafeZones.Add(tempRoot);
            evaluator.CurrentScriptPath = mainPath;
            var ast = new Parser(new Lexer(mainScript).Tokenize(), mainScript).Parse();
            await evaluator.Evaluate(ast);

            Assert.True(evaluator.SharedConnectionDependencies.SetEquals(["dyn_db", "nested_db"]));
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ForkedDependencies_SurviveWithoutMergingBranchResults()
    {
        var sp = DependencyInjectionSetup.BuildServiceProvider();
        await using var evaluator = sp.GetRequiredService<Evaluator>();
        var catalog = Catalog(Enumerable.Range(0, 32).Select(i =>
            new SharedConnectionDefinition($"source_{i}", "CAPTURE", null, Options(), false)).ToArray());

        // A failed PARALLEL block never merges result sets. Connections already used by its
        // branches must still protect any snapshot produced after an enclosing TRY/CATCH.
        await Task.WhenAll(Enumerable.Range(0, 32).Select(i => Task.Run(async () =>
        {
            var fork = evaluator.Fork().Fork();
            await Handler(new CapturingConnector(), catalog)
                .Execute(SharedCreate($"local_{i}", $"SHARED:source_{i}"), fork);
        })));

        Assert.True(evaluator.SharedConnectionDependencies.SetEquals(
            Enumerable.Range(0, 32).Select(i => $"source_{i}")));
    }

    [Fact]
    public async Task SharedReference_AlterAndCreateOrAlter_AreRejectedOnCatalogBackedConnections()
    {
        var connector = new CapturingConnector();
        var registry = ConnectionTestDoubles.Registry(connector);
        var catalog = Catalog(new SharedConnectionDefinition(
            "corp_db", "CAPTURE", null,
            Options(("SERVER", "sql01"), ("PASSWORD", "SECRET:sales_db_password")),
            Disabled: false));
        var secrets = new DictionarySecretProvider(("sales_db_password", "resolved-password"));
        var context = ConnectionTestDoubles.Context();

        var createHandler = new CreateConnectionStatementHandler(
            registry.Object, new Mock<ILogger>().Object, secretProvider: secrets, connectionCatalog: catalog);
        await createHandler.Execute(SharedCreate("corp", "SHARED:corp_db"), context);
        Assert.True(context.CatalogBackedConnections.ContainsKey("corp"));

        var alterHandler = new AlterConnectionStatementHandler(
            registry.Object, new Mock<ILogger>().Object, secretProvider: secrets, connectionCatalog: catalog);
        var alterStmt = new AlterConnectionStatement(
            "corp", null, null,
            new Dictionary<string, Expression> { ["SERVER"] = new LiteralExpression("evil.host", TokenType.STRING_LITERAL) });
        var alterEx = await Assert.ThrowsAsync<ExecutionException>(() => alterHandler.Execute(alterStmt, context));
        Assert.Contains("SHARED:", alterEx.Message);
        Assert.Equal("sql01", connector.LastOptions?["SERVER"]);

        var upsertStmt = new CreateConnectionStatement(
            "corp", "CAPTURE",
            options: new Dictionary<string, Expression> { ["HOST"] = new LiteralExpression("evil.host", TokenType.STRING_LITERAL) },
            mode: ObjectCreationMode.CreateOrAlter);
        var upsertEx = await Assert.ThrowsAsync<ExecutionException>(() => createHandler.Execute(upsertStmt, context));
        Assert.Contains("SHARED:", upsertEx.Message);
        Assert.Equal("sql01", connector.LastOptions?["SERVER"]);
    }

    [Fact]
    public async Task DirectSecretReference_WithActiveCatalog_RequiresAdminAuthorization()
    {
        var connector = new CapturingConnector();
        var registry = ConnectionTestDoubles.Registry(connector);
        var catalog = Catalog(new SharedConnectionDefinition(
            "corp_db", "CAPTURE", null,
            Options(("SERVER", "sql01"), ("PASSWORD", "SECRET:sales_db_password")),
            Disabled: false));
        var secrets = new DictionarySecretProvider(("sales_db_password", "resolved-password"));

        var createHandler = new CreateConnectionStatementHandler(
            registry.Object, new Mock<ILogger>().Object, secretProvider: secrets, connectionCatalog: catalog);

        var directCreate = new CreateConnectionStatement(
            "bypass", "CAPTURE",
            options: new Dictionary<string, Expression>
            {
                ["SERVER"] = new LiteralExpression("evil.host", TokenType.STRING_LITERAL),
                ["PASSWORD"] = new LiteralExpression("SECRET:sales_db_password", TokenType.STRING_LITERAL)
            });

        // Non-admin identity cannot reference SECRET: directly to bypass SHARED: ACLs.
        var nonAdminContext = ContextWithIdentity(new ExecutionIdentity
        {
            EffectiveUser = "alice",
            RealUser = "alice",
            IsAdmin = false
        });
        await Assert.ThrowsAsync<ExecutionException>(() => createHandler.Execute(directCreate, nonAdminContext));
        Assert.Null(connector.LastOptions);

        // Anonymous context with governed catalog active cannot reference SECRET: directly either.
        await Assert.ThrowsAsync<ExecutionException>(() => createHandler.Execute(directCreate, ConnectionTestDoubles.Context()));

        // Authorized SHARED: expansion still succeeds for non-admin users who have catalog access.
        await createHandler.Execute(SharedCreate("corp_ok", "SHARED:corp_db"), nonAdminContext);
        Assert.Equal("resolved-password", connector.LastOptions?["PASSWORD"]);

        // Admin can still resolve direct SECRET: references.
        var adminContext = ContextWithIdentity(new ExecutionIdentity
        {
            EffectiveUser = "root",
            RealUser = "root",
            IsAdmin = true
        });
        await createHandler.Execute(directCreate, adminContext);
        Assert.Equal("resolved-password", connector.LastOptions?["PASSWORD"]);
    }

    private static IExecutionContext ContextWithIdentity(ExecutionIdentity identity)
    {
        var baseContext = ConnectionTestDoubles.Context();
        var mock = Mock.Get(baseContext);
        mock.SetupGet(c => c.ExecutionIdentity).Returns(identity);
        return mock.Object;
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

    private static void AssertDenied(RecordingSecurityEventSink sink, string alias, string reason)
    {
        Assert.Contains(sink.Events, e =>
            e.Type == SecurityEventType.OperationDenied
            && e.Decision == SecurityEventDecision.Denied
            && e.SanitizedTarget == $"SHARED_CONNECTION:{alias}"
            && e.Reason.Contains("SHARED_CONNECTION_USE_DENIED", StringComparison.Ordinal)
            && e.Reason.Contains(reason, StringComparison.Ordinal));
    }

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

    private sealed class DenyingCatalogProvider : IConnectionCatalogProvider
    {
        public string ProviderName => "DenyingCatalog";

        public Task<SharedConnectionDefinition> ResolveAsync(
            string alias,
            ExecutionIdentity? identity = null,
            CancellationToken cancellationToken = default) =>
            throw new UnauthorizedAccessException(
                $"Identity '{identity?.EffectiveUser ?? "(none)"}' is not authorized to use shared connection '{alias}'.");
    }

    private sealed class RecordingSecurityEventSink : ISecurityEventSink
    {
        public List<SecurityEvent> Events { get; } = [];

        public void Emit(SecurityEvent securityEvent) => Events.Add(securityEvent);
    }
}
