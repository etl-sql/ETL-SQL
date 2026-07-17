using ETL_SQL.Core.Governance;
using Xunit;

namespace ETL_SQL.Tests.Core;

public class ConnectionCatalogTests
{
    [Fact]
    public async Task LocalProvider_StoreAndResolve_RoundTripsMachineEncrypted()
    {
        var root = TempRoot();
        var provider = new LocalConnectionCatalogProvider(root);
        var definition = new SharedConnectionDefinition(
            "my_sql_server",
            "MSSQL",
            Target: null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SERVER"] = "sql01",
                ["DATABASE"] = "Sales",
                ["PASSWORD"] = "SECRET:sales_db_password"
            },
            Disabled: false);

        await provider.StoreAsync(definition);

        var raw = File.ReadAllText(Path.Combine(root, "my_sql_server.connection"));
        var expectedPrefix = OperatingSystem.IsWindows() ? "DPAPI-M:" : "MACHINE:";
        Assert.StartsWith(expectedPrefix, raw);
        Assert.DoesNotContain("sql01", raw);

        var resolved = await provider.ResolveAsync("my_sql_server");
        Assert.Equal("MSSQL", resolved.ConnectorType);
        Assert.Equal("sql01", resolved.Options["SERVER"]);
        Assert.Equal("SECRET:sales_db_password", resolved.Options["PASSWORD"]);
        Assert.False(resolved.Disabled);
    }

    [Fact]
    public async Task LocalProvider_DisableReenableDeleteLifecycle()
    {
        var provider = new LocalConnectionCatalogProvider(TempRoot());
        var definition = new SharedConnectionDefinition(
            "warehouse", "POSTGRES", null,
            new Dictionary<string, string> { ["HOST"] = "pg01" }, false);

        await provider.StoreAsync(definition);
        Assert.Equal(SecretLifecycleStatus.Active, await provider.GetStatusAsync("warehouse"));
        Assert.Equal(["warehouse"], await provider.ListAsync());

        await provider.DisableAsync("warehouse");
        Assert.Equal(SecretLifecycleStatus.Disabled, await provider.GetStatusAsync("warehouse"));
        var disabledError = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ResolveAsync("warehouse"));
        Assert.Contains("disabled", disabledError.Message);

        await provider.StoreAsync(definition);
        Assert.Equal(SecretLifecycleStatus.Active, await provider.GetStatusAsync("warehouse"));

        await provider.DeleteAsync("warehouse");
        Assert.Equal(SecretLifecycleStatus.NotFound, await provider.GetStatusAsync("warehouse"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => provider.ResolveAsync("warehouse"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => provider.DeleteAsync("warehouse"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../evil")]
    [InlineData("name/with/slash")]
    public async Task LocalProvider_RejectsInvalidAliases(string alias)
    {
        var provider = new LocalConnectionCatalogProvider(TempRoot());

        await Assert.ThrowsAnyAsync<ArgumentException>(() => provider.ResolveAsync(alias));
    }

    [Fact]
    public void Factory_ReturnsNullWhenUnconfigured_CreatesLocal_RejectsPortal()
    {
        Assert.Null(ConnectionCatalogProviderFactory.Create(new ConnectionCatalogOptions()));

        var local = ConnectionCatalogProviderFactory.Create(new ConnectionCatalogOptions
        {
            Provider = "Local",
            LocalRoot = TempRoot()
        });
        Assert.Equal("LocalCatalog", local!.ProviderName);

        var portal = Assert.Throws<InvalidOperationException>(() =>
            ConnectionCatalogProviderFactory.Create(new ConnectionCatalogOptions { Provider = "Portal" }));
        Assert.Contains("Portal", portal.Message);
    }

    private static string TempRoot() =>
        Path.Combine(Path.GetTempPath(), $"etl-connection-catalog-{Guid.NewGuid():N}");
}
