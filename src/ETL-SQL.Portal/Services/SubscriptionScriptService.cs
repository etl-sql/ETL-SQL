using ETL_SQL.Portal.Data;

namespace ETL_SQL.Portal.Services;

public sealed class SubscriptionScriptService(PortalConfig config)
{
    public string WriteTriggerScript(Subscription subscription, Report report, string? tenantId = null)
    {
        var scriptName = SubscriptionOrchestration.ScriptFileName(subscription.Id, report.Name);
        if (!PortalPathGuard.TryResolveScript(
                config,
                RequireTenant(tenantId),
                Path.Combine("subscriptions", scriptName),
                out var scriptPath))
        {
            throw new InvalidOperationException("Subscription script path must be within the configured script root.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        SubscriptionTriggerScript.Write(scriptPath, subscription.Id);
        return scriptPath;
    }

    public bool TryResolve(string? scriptPath, out string resolved, string? tenantId = null)
    {
        resolved = string.Empty;
        return !string.IsNullOrWhiteSpace(scriptPath)
            && PortalPathGuard.TryResolveScript(config, RequireTenant(tenantId), scriptPath, out resolved);
    }

    private string RequireTenant(string? tenantId)
    {
        if (!config.SharedTenancy.Enabled)
            return string.IsNullOrWhiteSpace(config.TenantId) ? "portal-host" : config.TenantId;
        return ETL_SQL.Core.Multitenancy.TenantId.FromTrustedSource(tenantId).Value;
    }
}
