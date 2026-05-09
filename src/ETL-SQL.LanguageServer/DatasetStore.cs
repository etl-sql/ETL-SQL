using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.LSP
{
    /// <summary>
    /// Thread-safe in-process cache of portal dataset metadata.
    /// Populated by the VS Code extension via etlsql/setPortalDbPath.
    /// When no portal.db path is configured or the file is absent, all lookups return empty.
    /// </summary>
    public sealed class DatasetStore
    {
        public record DatasetEntry(
            string   Name,
            string   FolderPath,
            string   AccessLevel,
            long     RowCount,
            DateTime? LastRefresh,
            string?  Ttl,
            bool     IsStale);

        private readonly ILogger<DatasetStore> _log;
        private volatile List<DatasetEntry>    _entries = [];
        private string?                        _dbPath;

        public DatasetStore(ILogger<DatasetStore> log) => _log = log;

        public void SetPortalDbPath(string? path)
        {
            _dbPath = path;
            Refresh();
        }

        public void Refresh()
        {
            if (string.IsNullOrWhiteSpace(_dbPath) || !File.Exists(_dbPath))
            {
                _entries = [];
                return;
            }

            try
            {
                var entries = new List<DatasetEntry>();
                var cs = new SqliteConnectionStringBuilder
                {
                    DataSource = _dbPath,
                    Mode       = SqliteOpenMode.ReadOnly
                }.ToString();

                using var conn = new SqliteConnection(cs);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT Name, FolderPath, AccessLevel, RowCount, LastRefresh, Ttl
                    FROM   Datasets
                    ORDER  BY FolderPath, Name
                    """;

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var name        = reader.GetString(0);
                    var folder      = reader.GetString(1);
                    var accessLevel = reader.IsDBNull(2) ? "Private" : MapAccessLevel(reader.GetInt32(2));
                    var rowCount    = reader.IsDBNull(3) ? 0L : reader.GetInt64(3);
                    DateTime? lastRefresh = null;
                    if (!reader.IsDBNull(4))
                        lastRefresh = DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind);
                    var ttl         = reader.IsDBNull(5) ? null : reader.GetString(5);
                    var isStale     = ComputeIsStale(lastRefresh, ttl);

                    entries.Add(new DatasetEntry(name, folder, accessLevel, rowCount, lastRefresh, ttl, isStale));
                }

                _entries = entries;
                _log.LogDebug("DatasetStore refreshed: {Count} datasets from {Path}", entries.Count, _dbPath);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "DatasetStore: failed to read portal.db at {Path}", _dbPath);
                _entries = [];
            }
        }

        public List<DatasetEntry> GetAll() => _entries;

        public DatasetEntry? Find(string rawName)
        {
            var key = rawName.TrimStart('&', '#');
            var all = _entries;
            return all.Find(e => e.Name.TrimStart('&', '#').Equals(key, StringComparison.OrdinalIgnoreCase));
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static string MapAccessLevel(int value) => value switch { 1 => "Public", _ => "Private" };

        private static bool ComputeIsStale(DateTime? lastRefresh, string? ttl)
        {
            if (!lastRefresh.HasValue) return true;
            if (string.IsNullOrWhiteSpace(ttl)) return false;
            var span = ParseDuration(ttl);
            return span.HasValue && lastRefresh.Value + span.Value < DateTime.UtcNow;
        }

        private static TimeSpan? ParseDuration(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var m = Regex.Match(s.Trim(), @"^(\d+)([smhd])$", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            int v = int.Parse(m.Groups[1].Value);
            return m.Groups[2].Value.ToUpperInvariant() switch
            {
                "S" => TimeSpan.FromSeconds(v),
                "M" => TimeSpan.FromMinutes(v),
                "H" => TimeSpan.FromHours(v),
                "D" => TimeSpan.FromDays(v),
                _   => null
            };
        }
    }
}
