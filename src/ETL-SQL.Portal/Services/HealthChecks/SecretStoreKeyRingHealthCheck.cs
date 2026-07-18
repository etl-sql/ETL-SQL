using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ETL_SQL.Portal.Services.HealthChecks;

/// <summary>
/// Proves this node's Data Protection key ring can decrypt every stored Portal secret. On an HA
/// node with a wrong or missing shared key ring this fails fast instead of failing the first
/// script that resolves a SECRET: reference.
/// </summary>
public class SecretStoreKeyRingHealthCheck(IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<PortalSecretStoreService>();
            var result = await store.CheckKeyRingAsync(ct);

            if (result.SecretCount == 0)
                return HealthCheckResult.Healthy("No portal-store secrets to verify.");

            if (result.FailedCount == 0)
                return HealthCheckResult.Healthy($"All {result.SecretCount} portal-store secrets are decryptable.");

            return HealthCheckResult.Unhealthy(
                $"{result.FailedCount} of {result.SecretCount} portal-store secrets cannot be decrypted with this " +
                $"node's key ring (first: '{result.FirstFailedName}'). Check Portal:Storage:KeyRingPath points at the " +
                "cluster-shared key ring.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Portal secret store key-ring check failed.", ex);
        }
    }
}
