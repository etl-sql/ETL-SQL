using ETL_SQL.Core.Governance;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// Resolves SHARED:alias references from the Portal connection catalog for Portal-hosted script
/// execution, enforcing per-connection use-ACLs against the caller identity. Singleton; reaches
/// the scoped catalog service through a scope per resolution. ACL denials are audited here
/// (with the identity passed explicitly — subscription/scheduled runs have no HTTP context)
/// and surface to the script as a clear error that never contains entry contents.
/// </summary>
public sealed class PortalCatalogConnectionProvider(IServiceScopeFactory scopes) : IConnectionCatalogProvider
{
    public string ProviderName => "PortalCatalog";

    public async Task<SharedConnectionDefinition> ResolveAsync(
        string alias,
        ExecutionIdentity? identity = null,
        CancellationToken cancellationToken = default)
    {
        SecretNameValidator.Validate(alias);
        using var scope = scopes.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<PortalConnectionCatalogService>();
        try
        {
            return await catalog.ResolveDefinitionAsync(alias, identity, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            var audit = scope.ServiceProvider.GetRequiredService<AuditService>();
            await audit.LogAsync(
                identity?.EffectiveUserId,
                "SHARED_CONNECTION_USE_DENIED",
                "PortalSharedConnection",
                alias,
                $"EffectiveUser={identity?.EffectiveUser ?? "(none)"}; RealUser={identity?.RealUser ?? "(none)"}",
                actorType: identity == null ? "System" : "User");
            throw;
        }
    }
}

/// <summary>Placeholder when no catalog is configured; SHARED: references fail with configuration guidance.</summary>
public sealed class UnconfiguredConnectionCatalogProvider : IConnectionCatalogProvider
{
    public static readonly UnconfiguredConnectionCatalogProvider Instance = new();

    public string ProviderName => "Unconfigured";

    public Task<SharedConnectionDefinition> ResolveAsync(
        string alias,
        ExecutionIdentity? identity = null,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "no connection catalog provider is configured (Governance:ConnectionCatalog:Provider).");
}
