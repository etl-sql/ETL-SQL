using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ETL_SQL.TUI.UI
{
    /// <summary>
    /// Stored Report Portal connection: the URL and a cached session token (mirrors the VS
    /// Code extension's per-portal token cache). Persisted as plain JSON under the user
    /// profile; the token is short-lived (~55 min), the password is never stored.
    /// </summary>
    public sealed class PortalConfig
    {
        public string? Url { get; set; }
        public string? Token { get; set; }
        public DateTime Expiry { get; set; }

        [JsonIgnore]
        public bool HasValidToken => !string.IsNullOrEmpty(Token) && DateTime.UtcNow < Expiry;

        /// <summary>Overridable for tests; defaults to %APPDATA%/etl-sql/portal.json.</summary>
        public static string ConfigPath { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "etl-sql", "portal.json");

        public static PortalConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                    return JsonSerializer.Deserialize<PortalConfig>(File.ReadAllText(ConfigPath)) ?? new PortalConfig();
            }
            catch { }
            return new PortalConfig();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public static void Clear()
        {
            try { if (File.Exists(ConfigPath)) File.Delete(ConfigPath); }
            catch { }
        }
    }
}
