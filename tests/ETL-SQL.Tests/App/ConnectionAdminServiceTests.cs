using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Governance;
using ETL_SQL.Tests.Connectors;
using Xunit;

namespace ETL_SQL.Tests.App;

public class ConnectionAdminServiceTests
{
    [Fact]
    public async Task SetListVerifyDisableDelete_FullLifecycle()
    {
        var catalog = new LocalConnectionCatalogProvider(TempRoot());
        var secrets = new DictionarySecretProvider(("sales_db_password", "resolved"));
        var logger = new CapturingLogger();

        Assert.Equal(0, await Run("set-connection", catalog, secrets, logger, ctx =>
        {
            ctx.ConnectionAlias = "my_sql_server";
            ctx.ConnectionType = "MSSQL";
            ctx.ConnectionOptions = ["SERVER=sql01", "DATABASE=Sales", "PASSWORD=SECRET:sales_db_password"];
        }));

        Assert.Equal(0, await Run("list-connections", catalog, secrets, logger));
        Assert.Contains(logger.Messages, m => m.Contains("my_sql_server") && m.Contains("Active"));

        Assert.Equal(0, await Run("verify-connection", catalog, secrets, logger, ctx => ctx.ConnectionAlias = "my_sql_server"));
        Assert.Contains(logger.Messages, m => m.Contains("1 secret reference(s) resolvable"));
        Assert.DoesNotContain(logger.Messages, m => m.Contains("resolved"));

        Assert.Equal(0, await Run("disable-connection", catalog, secrets, logger, ctx => ctx.ConnectionAlias = "my_sql_server"));
        Assert.Equal(1, await Run("verify-connection", catalog, secrets, logger, ctx => ctx.ConnectionAlias = "my_sql_server"));
        Assert.Contains(logger.Messages, m => m.Contains("disabled"));

        Assert.Equal(0, await Run("delete-connection", catalog, secrets, logger, ctx => ctx.ConnectionAlias = "my_sql_server"));
        Assert.Equal(1, await Run("delete-connection", catalog, secrets, logger, ctx => ctx.ConnectionAlias = "my_sql_server"));
    }

    [Fact]
    public async Task SetConnection_RejectsRawCredentialValues()
    {
        var catalog = new LocalConnectionCatalogProvider(TempRoot());
        var logger = new CapturingLogger();

        Assert.Equal(1, await Run("set-connection", catalog, null, logger, ctx =>
        {
            ctx.ConnectionAlias = "bad";
            ctx.ConnectionType = "MSSQL";
            ctx.ConnectionOptions = ["PASSWORD=hunter2"];
        }));
        Assert.Contains(logger.Messages, m => m.Contains("set-secret"));
        Assert.Equal(SecretLifecycleStatus.NotFound, await catalog.GetStatusAsync("bad"));

        Assert.Equal(1, await Run("set-connection", catalog, null, logger, ctx =>
        {
            ctx.ConnectionAlias = "bad2";
            ctx.ConnectionType = "MSSQL";
            ctx.ConnectionTarget = "Server=db;Password=hunter2";
        }));
        Assert.Equal(SecretLifecycleStatus.NotFound, await catalog.GetStatusAsync("bad2"));

        // References are fine, in options and in the target.
        Assert.Equal(0, await Run("set-connection", catalog, null, logger, ctx =>
        {
            ctx.ConnectionAlias = "good";
            ctx.ConnectionType = "MSSQL";
            ctx.ConnectionTarget = "Server=db;Password=SECRET:pw";
        }));
    }

    [Fact]
    public async Task NoCatalogConfigured_FailsWithGuidance()
    {
        var logger = new CapturingLogger();

        var exit = await ConnectionAdminService.ExecuteAsync(
            "list-connections", new CliContext(), catalog: null, secrets: null, logger, CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains(logger.Messages, m => m.Contains("Governance:ConnectionCatalog:Provider"));
    }

    private static Task<int> Run(
        string action,
        IConnectionCatalogProvider catalog,
        ISecretProvider? secrets,
        CapturingLogger logger,
        Action<CliContext>? configure = null)
    {
        var ctx = new CliContext();
        configure?.Invoke(ctx);
        return ConnectionAdminService.ExecuteAsync(action, ctx, catalog, secrets, logger, CancellationToken.None);
    }

    private static string TempRoot() =>
        Path.Combine(Path.GetTempPath(), $"etl-connection-admin-{Guid.NewGuid():N}");

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = new();

        public string? SessionId { get; set; }
        public bool IsDebugEnabled => false;
        public bool IsVerboseEnabled => false;
        public bool IsVerbose { get; set; }
        public bool SuppressConsole { get; set; }
        public bool IsJsonMode { get; set; }
        public event Action<string, string?, ConsoleColor>? OnMessage { add { } remove { } }

        public void Log(LogLevel level, string message, Exception? ex = null) => Messages.Add(message);
    }
}
