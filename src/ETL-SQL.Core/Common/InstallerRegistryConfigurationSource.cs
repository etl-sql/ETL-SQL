using System;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Common;

/// <summary>
/// Configuration from the installer's registry values (see <see cref="InstallerRegistry"/>), present
/// only when the recorded install folder is this host's folder. It sits below appsettings.json, so an
/// operator's explicit setting wins.
/// </summary>
public sealed class InstallerRegistryConfigurationSource(string baseDirectory, IInstallerRegistry registry) : IConfigurationSource
{
    /// <summary>
    /// The install folder joins Security:ApprovedSafeZones at an index no hand-written list reaches, so
    /// configuration binding appends it to the operator's zones instead of replacing one.
    /// </summary>
    public const string SafeZoneKey = "Security:ApprovedSafeZones:65535";

    public IConfigurationProvider Build(IConfigurationBuilder builder) => new Provider(baseDirectory, registry);

    private sealed class Provider(string baseDirectory, IInstallerRegistry registry) : ConfigurationProvider
    {
        public override void Load()
        {
            var values = registry.ReadInstallValues();
            var folder = InstallerRegistry.MatchingInstallFolder(values, baseDirectory);
            if (values is null || folder is null) return;

            Data[SafeZoneKey] = folder;
            if (values.TryGetValue(InstallerRegistry.OrchestratorDatabasePath, out var dbPath) && !string.IsNullOrWhiteSpace(dbPath))
                Data["Portal:Orchestrator:DatabasePath"] = dbPath;
            foreach (var module in new[] { InstallerRegistry.Reporting, InstallerRegistry.Designer, InstallerRegistry.Scheduling, InstallerRegistry.Operations })
            {
                if (values.TryGetValue(module, out var on))
                    Data[$"Portal:Modules:{module}"] = on.Trim() == "1" ? "true" : "false";
            }

            var hasOrch = values.TryGetValue(InstallerRegistry.OrchSurface, out var orch);
            var hasSteward = values.TryGetValue(InstallerRegistry.StewardSurface, out var steward);
            var hasReport = values.TryGetValue(InstallerRegistry.ReportPortal, out var report);
            if (hasOrch || hasSteward || hasReport)
                Data["Portal:Surface"] = Surface(orch?.Trim() == "1", steward?.Trim() == "1", report?.Trim() == "1");
        }

        // The installer's feature selections as the Portal's surface name (formerly in configure-portal-jwt.ps1).
        private static string Surface(bool orch, bool steward, bool report) => (orch, steward, report) switch
        {
            (true, true, true) => "All",
            (true, true, false) => "OrchestratorAndSteward",
            (true, false, true) => "OrchestratorAndReports",
            (false, true, true) => "StewardAndReports",
            (true, false, false) => "OrchestratorOnly",
            (false, true, false) => "DataStewardOnly",
            (false, false, true) => "ReportOnly",
            _ => "All",
        };
    }
}
