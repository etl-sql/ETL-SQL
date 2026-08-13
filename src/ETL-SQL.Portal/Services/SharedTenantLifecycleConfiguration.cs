namespace ETL_SQL.Portal.Services;

public static class SharedTenantLifecycleConfiguration
{
    public static void Validate(PortalConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var shared = config.SharedTenancy;
        if (string.IsNullOrWhiteSpace(shared.LifecycleManagementKey)) return;
        if (!shared.Enabled)
            throw new InvalidOperationException(
                "Portal:SharedTenancy:LifecycleManagementKey requires Shared tenancy mode.");
        if (shared.LifecycleManagementKey.Length < 32)
            throw new InvalidOperationException(
                "Portal:SharedTenancy:LifecycleManagementKey must contain at least 32 characters.");
        if (string.IsNullOrWhiteSpace(shared.DefaultRelease)
            || shared.DefaultRelease.Length > 256
            || shared.DefaultRelease.IndexOfAny(['\r', '\n']) >= 0)
            throw new InvalidOperationException(
                "Portal:SharedTenancy:DefaultRelease must name one release or immutable image digest.");
        if (shared.DefaultMaxConcurrentJobs < 1
            || shared.DefaultMaxStorageMb < 128
            || shared.DefaultMaxReportSessions < 1)
            throw new InvalidOperationException(
                "Shared lifecycle defaults require positive jobs/sessions and at least 128 MiB storage.");
        if (!Uri.TryCreate(config.Orchestrator.ApiUrl, UriKind.Absolute, out var orchestratorUri)
            || orchestratorUri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException(
                "Shared lifecycle requires Portal:Orchestrator:ApiUrl.");
        if (string.IsNullOrWhiteSpace(config.Orchestrator.ApiKey))
            throw new InvalidOperationException(
                "Shared lifecycle requires a distinct Portal:Orchestrator:ApiKey.");
        if (string.IsNullOrWhiteSpace(config.Orchestrator.IdentitySigningSecret)
            || System.Text.Encoding.UTF8.GetByteCount(config.Orchestrator.IdentitySigningSecret) < 32)
            throw new InvalidOperationException(
                "Shared lifecycle requires a 32-byte Portal:Orchestrator:IdentitySigningSecret.");
    }
}
