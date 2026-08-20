
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Tests.Governance;

/// <summary>
/// Slice D1 of the Secure Outbound Data Gateway: the binding model.
///
/// <para>The property under test is the one §11.2 of the SaaS isolation architecture turns on — a
/// Gateway-bound catalog entry carries the connector type plus immutable Gateway/resource IDs and
/// <b>nothing else</b>. If a physical endpoint or a credential could survive a round trip through
/// the cloud-side catalog, a compromised catalog would hand over both the private address and the
/// key to reach it, and the Gateway would be decoration. These tests exist because "the binding does
/// not store an endpoint" is exactly the kind of claim that reads as obviously true and is never
/// actually asserted.</para>
/// </summary>
public sealed class GatewayBindingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "etlsql-gateway-binding-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    // ----------------------------------------------------- the binding carries nothing dialable

    [Theory]
    [InlineData("HOST")]
    [InlineData("host")]
    [InlineData("SERVER")]
    [InlineData("ADDRESS")]
    [InlineData("ENDPOINT")]
    [InlineData("URL")]
    [InlineData("URI")]
    [InlineData("PORT")]
    [InlineData("DSN")]
    [InlineData("DATA SOURCE")]
    [InlineData("BASEURL")]
    public void Binding_RejectsAnyOptionNamingAPhysicalDestination(string key)
    {
        var violation = GatewayBindingValidator.FindViolation(
            new GatewayResourceBinding("hq-gateway", "corp-sql-sales"),
            target: null,
            options: new Dictionary<string, string> { [key] = "myserver.corp.internal" });

        Assert.NotNull(violation);
        Assert.Contains("resolved on the Gateway", violation, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("PASSWORD")]
    [InlineData("password")]
    public void Binding_RejectsCredentialOptions(string key)
    {
        var violation = GatewayBindingValidator.FindViolation(
            new GatewayResourceBinding("hq-gateway", "corp-sql-sales"),
            target: null,
            options: new Dictionary<string, string> { [key] = "SECRET:sales-etl-credential" });

        Assert.NotNull(violation);
        Assert.Contains("held only on the Gateway", violation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Binding_RejectsATargetEvenWhenItLooksHarmless()
    {
        // Presence is the test, not shape: anything in the target position is a destination.
        var violation = GatewayBindingValidator.FindViolation(
            new GatewayResourceBinding("hq-gateway", "corp-sql-sales"),
            target: "Database=Sales",
            options: new Dictionary<string, string>());

        Assert.NotNull(violation);
        Assert.Contains("cannot carry a target", violation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Binding_AcceptsIdsAndNonDestinationOptions()
    {
        // A Gateway binding may still carry ordinary non-secret metadata, e.g. a schema hint.
        var violation = GatewayBindingValidator.FindViolation(
            new GatewayResourceBinding("hq-gateway", "corp-sql-sales"),
            target: null,
            options: new Dictionary<string, string> { ["SCHEMA"] = "dbo", ["TIMEOUT"] = "30" });

        Assert.Null(violation);
    }

    [Theory]
    [InlineData("", "corp-sql-sales")]
    [InlineData("   ", "corp-sql-sales")]
    [InlineData("hq-gateway", "")]
    [InlineData("hq gateway", "corp-sql-sales")]      // whitespace
    [InlineData("hq/gateway", "corp-sql-sales")]      // path separator
    [InlineData("hq-gateway", "corp:sql")]            // scheme-ish
    [InlineData("hq-gateway", "../other-tenant")]     // traversal shape
    public void Binding_RejectsMalformedIds(string gatewayId, string resourceId)
    {
        Assert.NotNull(GatewayBindingValidator.FindViolation(
            new GatewayResourceBinding(gatewayId, resourceId), target: null, options: null));
    }

    [Fact]
    public void Validator_IsANoOpForDirectBindings()
    {
        // A direct entry is unaffected: it may carry a host and a credential reference as before.
        Assert.Null(GatewayBindingValidator.FindViolation(
            binding: null,
            target: "Host=db.corp.internal;Database=Sales",
            options: new Dictionary<string, string> { ["PASSWORD"] = "SECRET:sales" }));
    }

    // ----------------------------------------------------- the store is the last line of refusal

    [Fact]
    public async Task Catalog_RefusesToStoreAGatewayEntryCarryingAnEndpoint()
    {
        var catalog = new LocalConnectionCatalogProvider(_root);
        var definition = new SharedConnectionDefinition(
            "sales_prod", "POSTGRES", Target: null,
            new Dictionary<string, string> { ["HOST"] = "db.corp.internal" },
            Disabled: false, SensitiveFields: null,
            Gateway: new GatewayResourceBinding("hq-gateway", "corp-sql-sales"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => catalog.StoreAsync(definition));
        Assert.Contains("resolved on the Gateway", error.Message, StringComparison.OrdinalIgnoreCase);

        // Nothing was written, so a rejected entry cannot be resolved afterwards.
        await Assert.ThrowsAsync<KeyNotFoundException>(() => catalog.ResolveAsync("sales_prod"));
    }

    [Fact]
    public async Task Catalog_RoundTripsAGatewayBindingWithoutGainingAnEndpoint()
    {
        var catalog = new LocalConnectionCatalogProvider(_root);
        await catalog.StoreAsync(new SharedConnectionDefinition(
            "sales_prod", "POSTGRES", Target: null,
            new Dictionary<string, string> { ["SCHEMA"] = "dbo" },
            Disabled: false, SensitiveFields: null,
            Gateway: new GatewayResourceBinding("hq-gateway", "corp-sql-sales")));

        var resolved = await catalog.ResolveAsync("sales_prod");

        Assert.NotNull(resolved.Gateway);
        Assert.Equal("hq-gateway", resolved.Gateway!.GatewayId);
        Assert.Equal("corp-sql-sales", resolved.Gateway.ResourceId);
        // The round trip must not have invented a destination or a credential.
        Assert.True(string.IsNullOrEmpty(resolved.Target));
        Assert.DoesNotContain(resolved.Options, pair => SecretResolvableFields.IsCredential(pair.Key));
        Assert.DoesNotContain(resolved.Options,
            pair => pair.Key.Equals("HOST", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Catalog_PersistedGatewayEntryLeaksNoAddressToDisk()
    {
        // Whatever the storage format, the bytes on disk must not contain a routable destination.
        var catalog = new LocalConnectionCatalogProvider(_root);
        await catalog.StoreAsync(new SharedConnectionDefinition(
            "sales_prod", "POSTGRES", Target: null,
            new Dictionary<string, string> { ["SCHEMA"] = "dbo" },
            Disabled: false, SensitiveFields: null,
            Gateway: new GatewayResourceBinding("hq-gateway", "corp-sql-sales")));

        var written = string.Concat(Directory.EnumerateFiles(_root, "*.connection")
            .Select(File.ReadAllText));

        Assert.NotEmpty(written);
        foreach (var forbidden in new[] { "db.corp.internal", "myserver", "1433", "Password" })
            Assert.DoesNotContain(forbidden, written, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Catalog_DirectAndGatewayEntriesShareTheSameAliasShape()
    {
        // Promotion changes the binding, not the script: the same alias is direct in one environment
        // and Gateway-bound in another, and a caller resolves it identically either way.
        var catalog = new LocalConnectionCatalogProvider(_root);

        await catalog.StoreAsync(new SharedConnectionDefinition(
            "sales_prod", "POSTGRES", "Host=db.dev.internal;Database=Sales",
            new Dictionary<string, string>(), Disabled: false));
        var direct = await catalog.ResolveAsync("sales_prod");
        Assert.Null(direct.Gateway);
        Assert.False(string.IsNullOrEmpty(direct.Target));

        await catalog.StoreAsync(new SharedConnectionDefinition(
            "sales_prod", "POSTGRES", Target: null, new Dictionary<string, string>(),
            Disabled: false, SensitiveFields: null,
            Gateway: new GatewayResourceBinding("hq-gateway", "corp-sql-sales")));
        var viaGateway = await catalog.ResolveAsync("sales_prod");

        Assert.NotNull(viaGateway.Gateway);
        Assert.True(string.IsNullOrEmpty(viaGateway.Target));
        Assert.Equal(direct.Alias, viaGateway.Alias);
    }

    // ----------------------------------------------------- no script may ask for or dodge routing

    // Note on naming: `CREATE BINDING x AS GATEWAY (...)` exists and is unrelated. It is a
    // validation-only stub for governed EXECUTE TOOL metadata whose own reference page says it is
    // not an authorization or resource boundary. It cannot bind a connection to the outbound Data
    // Gateway, and these tests assert the property behaviourally rather than by banning the word.

    [Fact]
    public async Task ScriptOptions_CannotIntroduceAGatewayBindingOnADirectAlias()
    {
        // Routing is an administrative fact in the catalog. A script that names gateway-shaped
        // options is simply passing options; it must not gain a Gateway binding for the connection.
        var catalog = new LocalConnectionCatalogProvider(_root);
        await catalog.StoreAsync(new SharedConnectionDefinition(
            "sales_prod", "POSTGRES", "Host=db.dev.internal;Database=Sales",
            new Dictionary<string, string>(), Disabled: false));

        var expander = new ETL_SQL.Engine.Services.SharedConnectionExpander(catalog);
        var expanded = await expander.ExpandAsync(
            "POSTGRES", "SHARED:sales_prod",
            new Dictionary<string, string>
            {
                ["GATEWAY"] = "hq-gateway",
                ["GATEWAY_ID"] = "hq-gateway",
                ["RESOURCE"] = "corp-sql-sales"
            },
            identity: null, CancellationToken.None);

        // The catalog entry is still the direct one, and the script's options stayed options.
        Assert.Equal("Host=db.dev.internal;Database=Sales", expanded.Target);
        Assert.Null((await catalog.ResolveAsync("sales_prod")).Gateway);
    }

    [Fact]
    public async Task GatewayBoundAlias_FailsClosedAndCannotBeLocallyBypassed()
    {
        // The local-bypass case: the entry deliberately carries no endpoint, and the script supplies
        // one. Falling back to a direct connection would route tenant data to whatever the script
        // named, so resolution must refuse outright while no Gateway data plane exists.
        var catalog = new LocalConnectionCatalogProvider(_root);
        await catalog.StoreAsync(new SharedConnectionDefinition(
            "sales_prod", "POSTGRES", Target: null, new Dictionary<string, string>(),
            Disabled: false, SensitiveFields: null,
            Gateway: new GatewayResourceBinding("hq-gateway", "corp-sql-sales")));

        var expander = new ETL_SQL.Engine.Services.SharedConnectionExpander(catalog);

        var error = await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(
            () => expander.ExpandAsync(
                "POSTGRES", "SHARED:sales_prod",
                new Dictionary<string, string> { ["HOST"] = "attacker.example.com" },
                identity: null, CancellationToken.None));

        Assert.Contains("does not accept script-supplied", error.Message, StringComparison.OrdinalIgnoreCase);

        var routed = await expander.ExpandAsync(
            "POSTGRES", "SHARED:sales_prod", new Dictionary<string, string>(),
            identity: null, CancellationToken.None);
        Assert.Equal(new GatewayResourceBinding("hq-gateway", "corp-sql-sales"), routed.Gateway);
        Assert.Empty(routed.Target);
        // The refusal must not have leaked the script's proposed destination back either.
        Assert.DoesNotContain("attacker.example.com", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
