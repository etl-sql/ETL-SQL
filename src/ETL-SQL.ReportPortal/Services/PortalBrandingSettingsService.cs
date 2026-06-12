using System.Text.Json;
using System.Text.Json.Serialization;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// Stores lightweight portal branding configured by administrators.
/// Persisted beside the portal database so Docker volume deployments keep it across rebuilds.
/// </summary>
public class PortalBrandingSettingsService
{
    private readonly string _settingsPath;
    private readonly object _lock = new();
    private readonly ILogger<PortalBrandingSettingsService> _logger;

    public string? DisplayName { get; private set; }
    public string? FooterText { get; private set; }
    public string? LogoUrl { get; private set; } = "/img/logo.png";

    public PortalBrandingSettingsService(PortalConfig config, ILogger<PortalBrandingSettingsService> logger)
    {
        _logger = logger;
        var dbDir = Path.GetDirectoryName(Path.GetFullPath(config.DatabasePath)) ?? ".";
        _settingsPath = Path.Combine(dbDir, "portal-branding.json");

        if (!File.Exists(_settingsPath)) return;

        try
        {
            var persisted = JsonSerializer.Deserialize<PersistedSettings>(File.ReadAllText(_settingsPath));
            DisplayName = Clean(persisted?.DisplayName);
            FooterText = Clean(persisted?.FooterText);
            LogoUrl = Clean(persisted?.LogoUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Portal branding settings could not be loaded from {Path}. Defaults will be used.", _settingsPath);
        }
    }

    public void Update(string? displayName, string? footerText, string? logoUrl)
    {
        lock (_lock)
        {
            DisplayName = Clean(displayName, 80);
            FooterText = Clean(footerText, 120);
            LogoUrl = ValidateLogoUrl(logoUrl);

            try
            {
                var directory = Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

                File.WriteAllText(_settingsPath,
                    JsonSerializer.Serialize(
                        new PersistedSettings { DisplayName = DisplayName, FooterText = FooterText, LogoUrl = LogoUrl },
                        new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Portal branding settings could not be saved.", ex);
            }
        }
    }

    public object ToDto() => new
    {
        DisplayName,
        FooterText,
        LogoUrl
    };

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
