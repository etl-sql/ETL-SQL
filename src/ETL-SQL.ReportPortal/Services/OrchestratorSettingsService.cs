using System.Text.Json;
using System.Text.Json.Serialization;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// Holds the active Orchestrator API URL and key at runtime.
/// Bootstraps from appsettings/env-vars (PortalConfig), then overrides from a persisted
/// sidecar JSON file so an admin can reconfigure it through the UI without a restart.
/// Sidecar API keys are protected with ASP.NET Data Protection.
/// </summary>
public class OrchestratorSettingsService
{
    private readonly string _settingsPath;
    private readonly OrchestratorApiKeyProtector protector;
    private readonly object _lock = new();

    public string? ApiUrl { get; private set; }
    public string? ApiKey { get; private set; }

    public OrchestratorSettingsService(
        PortalConfig config,
        OrchestratorApiKeyProtector protector)
    {
        this.protector = protector;
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
                if (persisted?.ProtectedApiKey is not null)
                    ApiKey = protector.Unprotect(persisted.ProtectedApiKey) ?? ApiKey;
                else if (persisted?.ApiKey is not null)
                {
                    // One-time migration from the pre-v0.11 plaintext sidecar format.
                    ApiKey = persisted.ApiKey;
                    Persist();
                }
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

            Persist();
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
        public string? ProtectedApiKey { get; init; }
        // Legacy read-only field. Persist() never writes plaintext API keys.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ApiKey { get; init; }
    }

    private void Persist()
    {
        try
        {
            File.WriteAllText(_settingsPath,
                JsonSerializer.Serialize(
                    new PersistedSettings
                    {
                        ApiUrl = ApiUrl,
                        ProtectedApiKey = string.IsNullOrEmpty(ApiKey)
                            ? null
                            : protector.Protect(ApiKey)
                    },
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
