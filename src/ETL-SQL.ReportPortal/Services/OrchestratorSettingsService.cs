using System.Text.Json;
using System.Text.Json.Serialization;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// Holds the active Orchestrator API URL and key at runtime.
/// Bootstraps from appsettings/env-vars (PortalConfig), then overrides from a persisted
/// sidecar JSON file so an admin can reconfigure it through the UI without a restart.
/// </summary>
public class OrchestratorSettingsService
{
    private readonly string _settingsPath;
    private readonly object _lock = new();

    public string? ApiUrl { get; private set; }
    public string? ApiKey { get; private set; }

    public OrchestratorSettingsService(PortalConfig config)
    {
        var dbDir = Path.GetDirectoryName(Path.GetFullPath(config.DatabasePath)) ?? ".";
        _settingsPath = Path.Combine(dbDir, "portal-orchestrator.json");

        ApiUrl = config.Orchestrator.ApiUrl;
        ApiKey = config.Orchestrator.ApiKey;

        if (File.Exists(_settingsPath))
        {
            try
            {
                var persisted = JsonSerializer.Deserialize<PersistedSettings>(File.ReadAllText(_settingsPath));
                if (persisted?.ApiUrl is not null) ApiUrl = persisted.ApiUrl;
                if (persisted?.ApiKey is not null) ApiKey = persisted.ApiKey;
            }
            catch { }
        }
    }

    public void Update(string? apiUrl, string? apiKey)
    {
        lock (_lock)
        {
            ApiUrl = string.IsNullOrWhiteSpace(apiUrl) ? null : apiUrl.Trim();
            ApiKey = string.IsNullOrEmpty(apiKey) ? null : apiKey;

            try
            {
                File.WriteAllText(_settingsPath,
                    JsonSerializer.Serialize(
                        new PersistedSettings { ApiUrl = ApiUrl, ApiKey = ApiKey },
                        new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }

    public string? BuildUrl(string path)
    {
        var b = ApiUrl?.TrimEnd('/');
        return string.IsNullOrEmpty(b) ? null : b + '/' + path.TrimStart('/');
    }

    private sealed class PersistedSettings
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ApiUrl { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ApiKey { get; init; }
    }
}
