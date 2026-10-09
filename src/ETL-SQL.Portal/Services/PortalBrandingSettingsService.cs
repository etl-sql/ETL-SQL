using System.Text.Json;
using System.Text.Json.Serialization;
using ETL_SQL.Portal.Models;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Stores lightweight portal branding configured by administrators.
/// Persisted beside the portal database so Docker volume deployments keep it across rebuilds.
/// </summary>
public class PortalBrandingSettingsService
{
    private readonly string _settingsPath;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ILogger<PortalBrandingSettingsService> _logger;

    private PortalBrandingDto current = new(null, null, "/img/logo.png");
    private bool initialized;
    public string? DisplayName => Volatile.Read(ref current).DisplayName;
    public string? FooterText => Volatile.Read(ref current).FooterText;
    public string? LogoUrl => Volatile.Read(ref current).LogoUrl;

    public PortalBrandingSettingsService(PortalConfig config, ILogger<PortalBrandingSettingsService> logger)
    {
        _logger = logger;
        var dbDir = Path.GetDirectoryName(Path.GetFullPath(config.DatabasePath)) ?? ".";
        _settingsPath = Path.Combine(dbDir, "portal-branding.json");

    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (initialized) return;
            var persisted = await PortalSettingsFile.ReadAsync<PersistedSettings>(_settingsPath, cancellationToken);
            if (persisted is not null)
                Volatile.Write(ref current, new(Clean(persisted.DisplayName, 80), Clean(persisted.FooterText), ValidateLogoUrl(persisted.LogoUrl)));
            initialized = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            _logger.LogWarning("Portal branding settings could not be loaded ({ErrorType}).", error.GetType().Name);
            throw new InvalidOperationException("Portal branding settings could not be loaded.");
        }
        finally { gate.Release(); }
    }

    public async Task UpdateAsync(string? displayName, string? footerText, string? logoUrl, CancellationToken cancellationToken = default)
    {
        var next = new PortalBrandingDto(Clean(displayName, 80), Clean(footerText), ValidateLogoUrl(logoUrl));
        await InitializeAsync(cancellationToken);
        await gate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                await PortalSettingsFile.WriteAsync(_settingsPath,
                    new PersistedSettings { DisplayName = next.DisplayName, FooterText = next.FooterText, LogoUrl = next.LogoUrl }, cancellationToken);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException("Portal branding settings could not be saved.");
            }
            Volatile.Write(ref current, next);
        }
        finally { gate.Release(); }
    }

    public PortalBrandingDto ToDto() => Volatile.Read(ref current);

    private static string? Clean(string? value, int maxLength = 120)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string? ValidateLogoUrl(string? value)
    {
        var cleaned = Clean(value, 500);
        if (cleaned is null) return null;

        if (cleaned.StartsWith("/", StringComparison.Ordinal) && !cleaned.StartsWith("//", StringComparison.Ordinal) && !cleaned.Contains('\\'))
        {
            return cleaned;
        }

        if (Uri.TryCreate(cleaned, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            return cleaned;
        }

        throw new ArgumentException("Logo URL must be an absolute http/https URL or a site-relative path starting with '/'.", nameof(value));
    }

    private sealed class PersistedSettings
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? DisplayName { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FooterText { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LogoUrl { get; init; }
    }
}
