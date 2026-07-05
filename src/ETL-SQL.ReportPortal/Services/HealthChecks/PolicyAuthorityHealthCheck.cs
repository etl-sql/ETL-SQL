using ETL_SQL.Core.Governance;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ETL_SQL.ReportPortal.Services.HealthChecks;

/// <summary>
/// Reports policy-authority availability. Unconfigured signing is a valid standalone state and
/// stays healthy; a configured signer whose key material is no longer accessible degrades the node,
/// because policy publishing (and therefore staged rollout and emergency rollback) would fail.
/// </summary>
public class PolicyAuthorityHealthCheck(IPolicyEnvelopeSigner signer) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        if (signer is DisabledPolicyEnvelopeSigner)
            return Task.FromResult(HealthCheckResult.Healthy(
                "Policy authority signing is not configured; enrolled-machine policy distribution is inactive."));
        try
        {
            _ = signer.PublicKeyPem;
            return Task.FromResult(HealthCheckResult.Healthy(
                "Policy authority signing key is accessible."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "Policy authority signing key is not accessible; policy publishing will fail.", ex));
        }
    }
}
