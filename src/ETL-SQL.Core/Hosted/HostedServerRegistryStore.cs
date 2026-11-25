using System.Text.Json;

namespace ETL_SQL.Core.Hosted;

/// <summary>
/// Persists user-level trusted hosted Portal server aliases (never tokens or credentials).
/// Shared between ETL-SQL TUI (ST57) and Desktop Studio (ST58).
/// </summary>
public sealed class HostedServerRegistryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private sealed record StoredRegistry(IReadOnlyList<HostedServerEntry> Servers);

    public string FilePath { get; }

    public HostedServerRegistryStore(string? filePath = null)
    {
        FilePath = string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "etl-sql",
                "hosted-servers.json")
            : filePath;
    }

    public IReadOnlyList<HostedServerEntry> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];

            var json = File.ReadAllText(FilePath);
            var stored = JsonSerializer.Deserialize<StoredRegistry>(json, JsonOptions);
            if (stored?.Servers is null)
                return [];

            var valid = new List<HostedServerEntry>(stored.Servers.Count);
            var seenAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in stored.Servers)
            {
                if (string.IsNullOrWhiteSpace(entry.Alias) || string.IsNullOrWhiteSpace(entry.Label))
                    continue;
                if (!HostedOriginValidator.TryNormalizeTrustedOrigin(entry.Origin, out var origin, out _, out _))
                    continue;
                var alias = entry.Alias.Trim();
                if (!seenAliases.Add(alias))
                    continue;
                valid.Add(new HostedServerEntry(alias, entry.Label.Trim(), origin, entry.Environment?.Trim()));
            }

            return valid;
        }
        catch
        {
            return [];
        }
    }

    public HostedServerEntry AddOrUpdate(string alias, string label, string rawOrigin, string? environment = null)
    {
        if (string.IsNullOrWhiteSpace(alias))
            throw new ArgumentException("Server alias is required.", nameof(alias));
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("Server label is required.", nameof(label));
        if (!HostedOriginValidator.TryNormalizeTrustedOrigin(rawOrigin, out var origin, out _, out var error))
            throw new ArgumentException(error ?? "Invalid Portal origin.", nameof(rawOrigin));

        var normalized = new HostedServerEntry(alias.Trim(), label.Trim(), origin, string.IsNullOrWhiteSpace(environment) ? null : environment.Trim());
        var servers = Load()
            .Where(s => !string.Equals(s.Alias, normalized.Alias, StringComparison.OrdinalIgnoreCase))
            .Append(normalized)
            .ToList();
        Save(servers);
        return normalized;
    }

    public bool Remove(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
            return false;

        var existing = Load();
        var filtered = existing
            .Where(s => !string.Equals(s.Alias, alias.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (filtered.Count == existing.Count)
            return false;

        Save(filtered);
        return true;
    }

    public HostedServerEntry? FindByAlias(string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
            return null;

        return Load().FirstOrDefault(s => string.Equals(s.Alias, alias.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public void Save(IReadOnlyList<HostedServerEntry> servers)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        // Only serialize the minimal trusted-server fields; never tokens.
        var sanitized = servers.Select(s => new HostedServerEntry(s.Alias, s.Label, s.Origin, s.Environment)).ToArray();
        var payload = JsonSerializer.Serialize(new StoredRegistry(sanitized), JsonOptions);
        File.WriteAllText(FilePath, payload);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
