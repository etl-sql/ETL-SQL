using System.Net;
using ETL_SQL.Core.Governance;
using Xunit;

namespace ETL_SQL.Tests.Core;

public class SecretProviderTests
{
    [Fact]
    public async Task EnvironmentSecretProvider_ResolvesPrefixedSecret()
    {
        var provider = new EnvironmentSecretProvider(
            "ETLSQL_",
            key => key == "ETLSQL_SALES_DB_PASSWORD" ? "env-secret" : null);

        var result = await provider.ResolveAsync("sales-db-password");

        Assert.Equal("Environment", result.Provider);
        Assert.Equal("sales-db-password", result.Name);
        Assert.Equal("env-secret", result.Value);
    }

    [Fact]
    public async Task OsSecretStoreProvider_StoresAndResolvesProtectedSecret()
    {
        var root = Path.Combine(Path.GetTempPath(), $"etl-secret-store-{Guid.NewGuid():N}");
        var provider = new OsSecretStoreProvider(root);

        await provider.StoreAsync("sales_db_password", "stored-secret");
        var result = await provider.ResolveAsync("sales_db_password");

        Assert.Equal("OsSecretStore", result.Provider);
        Assert.Equal("stored-secret", result.Value);
        Assert.DoesNotContain("stored-secret", File.ReadAllText(Path.Combine(root, "sales_db_password.secret")));
    }

    [Fact]
    public async Task HttpsVaultSecretProvider_ResolvesJsonValue()
    {
        using var http = new HttpClient(new StubHttpHandler(HttpStatusCode.OK, """{"value":"vault-secret"}"""));
        var provider = new HttpsVaultSecretProvider(new Uri("https://vault.example.test/secrets"), http, "token-1");

        var result = await provider.ResolveAsync("sales_db_password");

        Assert.Equal("HttpsVault", result.Provider);
        Assert.Equal("vault-secret", result.Value);
    }

    [Fact]
    public async Task HttpsVaultSecretProvider_RejectsHttpEndpoint()
    {
        using var http = new HttpClient(new StubHttpHandler(HttpStatusCode.OK, "secret"));
        var provider = new HttpsVaultSecretProvider(new Uri("http://vault.example.test/secrets"), http);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ResolveAsync("sales_db_password"));

        Assert.Contains("HTTPS", ex.Message);
    }

    [Fact]
    public void SecretProviderFactory_CreatesConfiguredProvider()
    {
        using var http = new HttpClient(new StubHttpHandler(HttpStatusCode.OK, "secret"));
        var factory = new SecretProviderFactory(http);

        var provider = factory.Create(new SecretProviderOptions
        {
            Provider = "HttpsVault",
            VaultEndpoint = "https://vault.example.test/secrets"
        });

        Assert.Equal("HttpsVault", provider.ProviderName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../secret")]
    [InlineData("secret/name")]
    public async Task Providers_RejectInvalidSecretNames(string name)
    {
        var provider = new EnvironmentSecretProvider(getEnvironmentVariable: _ => "secret");

        await Assert.ThrowsAnyAsync<ArgumentException>(() => provider.ResolveAsync(name));
    }

    private sealed class StubHttpHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content)
            });
    }
}
