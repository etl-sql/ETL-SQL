using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Security;

/// <summary>
/// The unmounted machine key is this installation's own random key, not something computable from
/// the host and account name; and GENERATE JWT_SECRET hands the secret back as a result, not a log line.
/// </summary>
public sealed class MachineKeyAndJwtSecretTests
{
    [Fact]
    public void TheMachineKeyIsNotDerivedFromTheHostAndAccountName()
    {
        var key = SecurityService.GetMachineKey();
        var predictable = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{Environment.MachineName}:{Environment.UserName}")));

        Assert.NotEqual(predictable, key);
        Assert.Equal(key, SecurityService.GetMachineKey());
    }

    [Fact]
    public void AValueEncryptedUnderThePredictableKeyIsNotReadable()
    {
        var predictable = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{Environment.MachineName}:{Environment.UserName}")));
        var forged = CryptoUtils.Encrypt("forged", predictable);

        Assert.ThrowsAny<Exception>(() => CryptoUtils.Decrypt(forged, SecurityService.GetMachineKey()));
    }

    [Fact]
    public async Task GenerateJwtSecretReturnsTheSecretAsARowAndLogsNoneOfIt()
    {
        var provider = DependencyInjectionSetup.BuildServiceProvider();
        await using var providerLifetime = provider as IAsyncDisposable;
        var logger = provider.GetRequiredService<ILogger>();
        logger.SuppressConsole = true;
        var logged = new List<string>();
        logger.OnMessage += (message, _, _) => { lock (logged) logged.Add(message); };
        var evaluator = provider.GetRequiredService<Evaluator>();
        evaluator.RedirectOutput = true;
        var configPath = Path.Combine(Path.GetTempPath(), $"jwt-appsettings-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(configPath, "{}");
        try
        {
            var handler = new GenerateJwtSecretStatementHandler(logger) { ConfigPath = configPath };
            await handler.Execute(new GenerateJwtSecretStatement(), evaluator);

            var row = Assert.Single(evaluator.LastResult!.Rows);
            var secret = Assert.IsType<string>(row["Secret"]);
            Assert.Equal(32, Convert.FromBase64String(secret).Length);
            Assert.Equal(secret, CryptoUtils.Decrypt((string)row["EncryptedConfigValue"]!, SecurityService.GetMachineKey()));

            Assert.DoesNotContain(evaluator.Messages, entry => entry.Message.Contains(secret, StringComparison.Ordinal));
            lock (logged) Assert.DoesNotContain(logged, message => message.Contains(secret, StringComparison.Ordinal));
            Assert.DoesNotContain(secret, await File.ReadAllTextAsync(configPath), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(configPath);
        }
    }
}
