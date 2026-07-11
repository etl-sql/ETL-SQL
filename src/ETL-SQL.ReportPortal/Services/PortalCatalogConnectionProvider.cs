using ETL_SQL.Core.Governance;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// Resolves SHARED:alias references from the Portal connection catalog for Portal-hosted script
/// execution. Singleton; reaches the scoped catalog service through a scope per resolution.
/// </summary>
public sealed class PortalCatalogConnectionProvider(IServiceScopeFactory scopes) : IConnectionCatalogProvider
{
    public string ProviderName => "PortalCatalog";

    public async Task<SharedConnectionDefinition> ResolveAsync(string alias, CancellationToken cancellationToken = default)
    {
        SecretNameValidator.Validate(alias);
        using var scope = scopes.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<PortalConnectionCatalogService>();
        return await catalog.ResolveDefinitionAsync(alias, cancellationToken);
    }
}

/// <summary>Placeholder when no catalog is configured; SHARED: references fail with configuration guidance.</summary>
public sealed class UnconfiguredConnectionCatalogProvider : IConnectionCatalogProvider
{
    public static readonly UnconfiguredConnectionCatalogProvider Instance = new();

    public string ProviderName => "Unconfigured";

    public Task<SharedConnectionDefinition> ResolveAsync(string alias, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "no connection catalog provider is configured (Governance:ConnectionCatalog:Provider).");
}
