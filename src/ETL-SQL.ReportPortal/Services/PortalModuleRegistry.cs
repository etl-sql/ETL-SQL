namespace ETL_SQL.ReportPortal.Services;

public sealed record PortalModuleStatus(string Name, bool Enabled, string Description);

public sealed class PortalModuleRegistry(PortalConfig config)
{
    private readonly PortalModuleConfig modules = config.Modules ?? new PortalModuleConfig();

    public IReadOnlyList<PortalModuleStatus> All { get; } =
    [
        new("Reporting", config.Modules?.Reporting ?? true, "Report catalog, report player, datasets, and subscriptions."),
        new("Designer", config.Modules?.Designer ?? true, "Browser report designer and design-time APIs."),
        new("ConnectionCatalog", config.Modules?.ConnectionCatalog ?? true, "Shared connection catalog APIs and diagnostics."),
        new("SecretStore", config.Modules?.SecretStore ?? true, "Portal-managed secret vault APIs and secret resolution."),
        new("Scheduling", config.Modules?.Scheduling ?? true, "Refresh scheduling, orchestrator polling, and scheduled work."),
        new("Operations", config.Modules?.Operations ?? true, "Operational health, fleet status, audit, and administrative telemetry."),
        new("Documentation", config.Modules?.Documentation ?? true, "Portal-hosted documentation surfaces."),
    ];

    public bool IsEnabled(string moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
            return false;

        return Normalize(moduleName) switch
        {
            "reports" or "reporting" or "player" or "datasets" or "subscriptions" => modules.Reporting,
            "designer" or "reportdesigner" => modules.Designer,
            "connectioncatalog" or "connections" or "diagnostics" => modules.ConnectionCatalog,
            "secretstore" or "secrets" => modules.SecretStore,
            "scheduling" or "scheduler" or "refresh" => modules.Scheduling,
            "operations" or "admin" or "audit" or "fleet" or "health" => modules.Operations,
            "documentation" or "docs" => modules.Documentation,
            _ => false,
        };
    }

    private static string Normalize(string value) =>
        value.Trim().Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
}
