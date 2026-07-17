using ETL_SQL.ReportPortal.Data;

namespace ETL_SQL.ReportPortal.Services;

public sealed class SubscriptionScriptService(PortalConfig config)
{
    public string WriteTriggerScript(Subscription subscription, Report report)
    {
        var scriptName = SubscriptionOrchestration.ScriptFileName(subscription.Id, report.Name);
        if (!PortalPathGuard.TryResolveScript(
                config,
                Path.Combine("subscriptions", scriptName),
                out var scriptPath))
        {
            throw new InvalidOperationException("Subscription script path must be within the configured script root.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        SubscriptionTriggerScript.Write(scriptPath, subscription.Id);
        return scriptPath;
    }

    public bool TryResolve(string? scriptPath, out string resolved)
    {
        resolved = string.Empty;
        return !string.IsNullOrWhiteSpace(scriptPath)
            && PortalPathGuard.TryResolveScript(config, scriptPath, out resolved);
    }
}
