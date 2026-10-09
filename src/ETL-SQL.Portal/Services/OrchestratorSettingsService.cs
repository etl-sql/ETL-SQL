using System.Text.Json;
using System.Text.Json.Serialization;
using ETL_SQL.Portal.Models;

namespace ETL_SQL.Portal.Services;

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
    private readonly SemaphoreSlim gate = new(1, 1);
    private sealed record Settings(string? ApiUrl, string? ApiKey);
    private Settings current;
    private bool initialized;

    public string? ApiUrl => Volatile.Read(ref current).ApiUrl;
    public string? ApiKey => Volatile.Read(ref current).ApiKey;

    public OrchestratorSettingsService(
        PortalConfig config,
        OrchestratorApiKeyProtector protector)
    {
        this.protector = protector;
        var dbDir = Path.GetDirectoryName(Path.GetFullPath(config.DatabasePath)) ?? ".";
        _settingsPath = Path.Combine(dbDir, "portal-orchestrator.json");

        current = new(config.Orchestrator.ApiUrl, config.Orchestrator.ApiKey);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (initialized) return;
            try
            {
                var persisted = await PortalSettingsFile.ReadAsync<PersistedSettings>(_settingsPath, cancellationToken);
                if (persisted is not null)
                {
                    var apiUrl = persisted.FormatVersion == 1 ? persisted.ApiUrl : persisted.ApiUrl ?? current.ApiUrl;
                    var apiKey = persisted.ProtectedApiKey is not null
                        ? protector.Unprotect(persisted.ProtectedApiKey)
                            ?? throw new InvalidOperationException("The saved Orchestrator key could not be read.")
                        : persisted.FormatVersion == 1 ? null : persisted.ApiKey ?? current.ApiKey;
                    var loaded = new Settings(apiUrl, apiKey);
                    // Upgrade the legacy plaintext sidecar before publishing its values.
                    if (persisted.ApiKey is not null) await PersistAsync(loaded, cancellationToken);
                    Volatile.Write(ref current, loaded);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            {
                throw new InvalidOperationException("Orchestrator settings could not be loaded.");
            }
            initialized = true;
        }
        finally { gate.Release(); }
    }

    public async Task UpdateAsync(string? apiUrl, string? apiKey, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var url = string.IsNullOrWhiteSpace(apiUrl) ? null : apiUrl.Trim();
        if (url is not null && (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0))
            throw new ArgumentException("Orchestrator API URL must be an absolute http/https URL without credentials.");
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Null preserves the existing key; an empty string explicitly clears it.
            var next = new Settings(url, apiKey is null ? current.ApiKey : apiKey.Length == 0 ? null : apiKey);
            await PersistAsync(next, cancellationToken);
            Volatile.Write(ref current, next);
        }
        finally { gate.Release(); }
    }

    public OrchestratorSettingsDto ToDto()
    {
        var snapshot = Volatile.Read(ref current);
        return new(snapshot.ApiUrl, !string.IsNullOrEmpty(snapshot.ApiKey));
    }

    public HttpRequestMessage? CreateRequest(HttpMethod method, string path)
    {
        var snapshot = Volatile.Read(ref current);
        var url = snapshot.ApiUrl?.TrimEnd('/');
        if (string.IsNullOrEmpty(url)) return null;
        var request = new HttpRequestMessage(method, url + '/' + path.TrimStart('/'));
        if (!string.IsNullOrEmpty(snapshot.ApiKey))
            request.Headers.TryAddWithoutValidation("X-Orchestrator-Key", snapshot.ApiKey);
        return request;
    }

    private sealed class PersistedSettings
    {
        public int FormatVersion { get; init; }
        public string? ApiUrl { get; init; }
        public string? ProtectedApiKey { get; init; }
        // Legacy read-only field. Persist() never writes plaintext API keys.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ApiKey { get; init; }
    }

    private async Task PersistAsync(Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            await PortalSettingsFile.WriteAsync(_settingsPath, new PersistedSettings
            {
                FormatVersion = 1,
                ApiUrl = settings.ApiUrl,
                ProtectedApiKey = settings.ApiKey is null ? null : protector.Protect(settings.ApiKey)
            }, cancellationToken);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("Orchestrator settings could not be saved.");
        }
    }
}
