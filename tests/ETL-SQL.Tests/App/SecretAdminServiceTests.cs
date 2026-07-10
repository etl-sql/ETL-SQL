using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core.Governance;
using Xunit;

namespace ETL_SQL.Tests.App;

public class SecretAdminServiceTests
{
    [Fact]
    public async Task SetVerifyRotateDisableDelete_FullLifecycle()
    {
        var provider = new OsSecretStoreProvider(TempRoot());
        var logger = new CapturingLogger();

        Assert.Equal(0, await Run("set-secret", "db_password", "first-value", provider, logger));
        Assert.Equal(0, await Run("verify-secret", "db_password", null, provider, logger));
        Assert.Equal(0, await Run("rotate-secret", "db_password", "second-value", provider, logger));
        Assert.Equal("second-value", (await provider.ResolveAsync("db_password")).Value);

        Assert.Equal(0, await Run("disable-secret", "db_password", null, provider, logger));
        Assert.Equal(1, await Run("verify-secret", "db_password", null, provider, logger));
        Assert.Contains(logger.Messages, m => m.Contains("disabled"));

        Assert.Equal(0, await Run("set-secret", "db_password", "third-value", provider, logger));
        Assert.Equal("third-value", (await provider.ResolveAsync("db_password")).Value);

        Assert.Equal(0, await Run("delete-secret", "db_password", null, provider, logger));
        Assert.Equal(SecretLifecycleStatus.NotFound, await provider.GetStatusAsync("db_password"));
        Assert.Equal(1, await Run("delete-secret", "db_password", null, provider, logger));

        Assert.DoesNotContain(logger.Messages, m =>
            m.Contains("first-value") || m.Contains("second-value") || m.Contains("third-value"));
    }

    [Fact]
    public async Task RotateSecret_RequiresExistingSecret()
    {
        var provider = new OsSecretStoreProvider(TempRoot());
        var logger = new CapturingLogger();

        Assert.Equal(1, await Run("rotate-secret", "missing", "value", provider, logger));
        Assert.Contains(logger.Messages, m => m.Contains("set-secret"));
    }

    [Fact]
    public async Task Mutations_RequireLifecycleProvider()
    {
        var provider = new EnvironmentSecretProvider(getEnvironmentVariable: _ => "env-value");
        var logger = new CapturingLogger();

        Assert.Equal(1, await Run("set-secret", "db_password", "value", provider, logger));
        Assert.Equal(1, await Run("disable-secret", "db_password", null, provider, logger));
        Assert.Equal(1, await Run("delete-secret", "db_password", null, provider, logger));
        Assert.Equal(0, await Run("verify-secret", "db_password", null, provider, logger));
        Assert.DoesNotContain(logger.Messages, m => m.Contains("env-value"));
    }

    [Fact]
    public async Task MissingName_Fails()
    {
        var provider = new OsSecretStoreProvider(TempRoot());
        var logger = new CapturingLogger();

        Assert.Equal(1, await Run("set-secret", null, "value", provider, logger));
        Assert.Contains(logger.Messages, m => m.Contains("--name"));
    }

    [Fact]
    public async Task OsSecretStoreProvider_DisableAndReenableLifecycle()
    {
        var provider = new OsSecretStoreProvider(TempRoot());

        await provider.StoreAsync("api_token", "token-value");
        Assert.Equal(SecretLifecycleStatus.Active, await provider.GetStatusAsync("api_token"));

        await provider.DisableAsync("api_token");
        Assert.Equal(SecretLifecycleStatus.Disabled, await provider.GetStatusAsync("api_token"));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ResolveAsync("api_token"));
        Assert.Contains("disabled", ex.Message);

        await provider.StoreAsync("api_token", "new-token");
        Assert.Equal(SecretLifecycleStatus.Active, await provider.GetStatusAsync("api_token"));
        Assert.Equal("new-token", (await provider.ResolveAsync("api_token")).Value);

        await provider.DeleteAsync("api_token");
        Assert.Equal(SecretLifecycleStatus.NotFound, await provider.GetStatusAsync("api_token"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => provider.ResolveAsync("api_token"));
    }

    private static Task<int> Run(
        string action,
        string? name,
        string? value,
        ISecretProvider provider,
        CapturingLogger logger) =>
        SecretAdminService.ExecuteAsync(action, name, value, provider, logger, CancellationToken.None);

    private static string TempRoot() =>
        Path.Combine(Path.GetTempPath(), $"etl-secret-admin-{Guid.NewGuid():N}");

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
