using ETL_SQL.Core.Governance;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Resolves SECRET:name references from the Portal-managed encrypted secret store, so scripts
/// executed by the Portal (reports, subscriptions, datasets) share one cluster-wide store.
/// Registered when Governance:Secrets:Provider is "PortalStore"; singleton, so the scoped
/// store/DbContext is reached through a scope per resolution.
/// </summary>
public sealed class PortalStoreSecretProvider(IServiceScopeFactory scopes) : ISecretProvider
{
    public string ProviderName => "PortalStore";

    public async Task<SecretResolutionResult> ResolveAsync(string name, CancellationToken cancellationToken = default)
    {
        SecretNameValidator.Validate(name);
        using var scope = scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<PortalSecretStoreService>();

        if (await store.GetStatusAsync(name, cancellationToken) == SecretLifecycleStatus.NotFound)
            throw new KeyNotFoundException($"Secret '{name}' was not found in the Portal secret store.");

        var value = await store.ResolveAsync(name, cancellationToken);
        return new SecretResolutionResult(name, value, ProviderName);
    }
}
