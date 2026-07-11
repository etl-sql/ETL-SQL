using System.Text.Json;
using ETL_SQL.Common;

namespace ETL_SQL.Core.Governance;

/// <summary>
/// A shared connection catalog entry: connector type, non-secret options, and SECRET: references.
/// Credential fields hold references, never resolved values, so rotating a secret never touches
/// catalog entries.
/// </summary>
public sealed record SharedConnectionDefinition(
    string Alias,
    string ConnectorType,
    string? Target,
    IReadOnlyDictionary<string, string> Options,
    bool Disabled,
    IReadOnlyCollection<string>? SensitiveFields = null);

/// <summary>Resolves SHARED:alias references to cataloged connection definitions.</summary>
public interface IConnectionCatalogProvider
{
    string ProviderName { get; }

    /// <summary>
    /// Resolves an alias for the given caller. Providers that support per-connection use ACLs
    /// must fail closed for restricted entries when the identity is null or not authorized.
    /// </summary>
    Task<SharedConnectionDefinition> ResolveAsync(
        string alias,
        ExecutionIdentity? identity = null,
        CancellationToken cancellationToken = default);
}

public interface IWritableConnectionCatalogProvider : IConnectionCatalogProvider
{
    Task StoreAsync(SharedConnectionDefinition definition, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);
    Task<SecretLifecycleStatus> GetStatusAsync(string alias, CancellationToken cancellationToken = default);
    Task DisableAsync(string alias, CancellationToken cancellationToken = default);
    /// <summary>Re-enables a disabled entry without re-supplying its definition. No-op when already active.</summary>
    Task EnableAsync(string alias, CancellationToken cancellationToken = default);
    Task DeleteAsync(string alias, CancellationToken cancellationToken = default);
}

/// <summary>
/// Machine-scoped connection catalog for single-node/SME deployments without a Portal: one
/// machine-encrypted JSON entry per alias under a local directory (beside the OS secret store),
/// written by the admin CLI and read by script execution. Same trust boundary as
/// <see cref="OsSecretStoreProvider"/>: machine-scope crypto plus filesystem ACLs.
/// </summary>
public sealed class LocalConnectionCatalogProvider(string rootDirectory) : IWritableConnectionCatalogProvider
{
    public string ProviderName => "LocalCatalog";

    private sealed record EntryPayload(
        string ConnectorType, string? Target, Dictionary<string, string> Options, List<string>? SensitiveFields = null);

    // The local catalog has no user model: its trust boundary is filesystem access on the single
    // node, so the caller identity is not evaluated here.
    public async Task<SharedConnectionDefinition> ResolveAsync(
        string alias,
        ExecutionIdentity? identity = null,
        CancellationToken cancellationToken = default)
    {
        var path = GetEntryPath(alias);
        if (!File.Exists(path))
        {
            if (File.Exists(GetDisabledPath(alias)))
                throw new InvalidOperationException(
                    $"Shared connection '{alias}' is disabled. Re-enable it by storing it again with set-connection.");

            throw new KeyNotFoundException($"Shared connection '{alias}' was not found in the connection catalog.");
        }

        var protectedValue = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var payload = Deserialize(alias, CryptoUtils.Unprotect(protectedValue, alias));
        return new SharedConnectionDefinition(
            alias, payload.ConnectorType, payload.Target, payload.Options, Disabled: false, payload.SensitiveFields);
    }

    public async Task StoreAsync(SharedConnectionDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.ConnectorType))
            throw new ArgumentException("A connector type is required.", nameof(definition));

        var path = GetEntryPath(definition.Alias);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var payload = new EntryPayload(
            definition.ConnectorType.Trim(),
            definition.Target,
            new Dictionary<string, string>(definition.Options, StringComparer.OrdinalIgnoreCase),
            definition.SensitiveFields?.ToList());
        var protectedValue = CryptoUtils.ProtectMachine(JsonSerializer.Serialize(payload), definition.Alias);
        await File.WriteAllTextAsync(path, protectedValue, cancellationToken).ConfigureAwait(false);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        // Storing an entry re-enables a previously disabled alias.
        var disabledPath = GetDisabledPath(definition.Alias);
        if (File.Exists(disabledPath))
            File.Delete(disabledPath);
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(GetRoot()))
            return Task.FromResult<IReadOnlyList<string>>([]);

        var aliases = Directory.EnumerateFiles(GetRoot(), "*.connection")
            .Concat(Directory.EnumerateFiles(GetRoot(), "*.connection.disabled")
                .Select(path => path[..^".disabled".Length]))
            .Select(Path.GetFileNameWithoutExtension)
            .Where(alias => !string.IsNullOrEmpty(alias))
            .Select(alias => alias!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(aliases);
    }

    public Task<SecretLifecycleStatus> GetStatusAsync(string alias, CancellationToken cancellationToken = default)
    {
        if (File.Exists(GetEntryPath(alias)))
            return Task.FromResult(SecretLifecycleStatus.Active);
        if (File.Exists(GetDisabledPath(alias)))
            return Task.FromResult(SecretLifecycleStatus.Disabled);
        return Task.FromResult(SecretLifecycleStatus.NotFound);
    }

    public Task DisableAsync(string alias, CancellationToken cancellationToken = default)
    {
        var path = GetEntryPath(alias);
        if (!File.Exists(path))
            throw new KeyNotFoundException($"Shared connection '{alias}' was not found in the connection catalog.");

        File.Move(path, GetDisabledPath(alias), overwrite: true);
        return Task.CompletedTask;
    }

    public Task EnableAsync(string alias, CancellationToken cancellationToken = default)
    {
        var path = GetEntryPath(alias);
        if (File.Exists(path))
            return Task.CompletedTask;

        var disabledPath = GetDisabledPath(alias);
        if (!File.Exists(disabledPath))
            throw new KeyNotFoundException($"Shared connection '{alias}' was not found in the connection catalog.");

        File.Move(disabledPath, path);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string alias, CancellationToken cancellationToken = default)
    {
        var path = GetEntryPath(alias);
        var disabledPath = GetDisabledPath(alias);
        if (!File.Exists(path) && !File.Exists(disabledPath))
            throw new KeyNotFoundException($"Shared connection '{alias}' was not found in the connection catalog.");

        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(disabledPath)) File.Delete(disabledPath);
        return Task.CompletedTask;
    }

    private static EntryPayload Deserialize(string alias, string json)
    {
        var payload = JsonSerializer.Deserialize<EntryPayload>(json);
        if (payload == null || string.IsNullOrWhiteSpace(payload.ConnectorType))
            throw new InvalidOperationException($"Shared connection '{alias}' has an invalid catalog entry payload.");
        return payload;
    }

    private string GetEntryPath(string alias)
    {
        SecretNameValidator.Validate(alias);
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("Connection catalog root directory is required.", nameof(rootDirectory));
        if (!Path.IsPathFullyQualified(rootDirectory))
            throw new InvalidOperationException("Connection catalog root directory must be fully qualified.");

        return Path.Combine(GetRoot(), alias + ".connection");
    }

    private string GetDisabledPath(string alias) => GetEntryPath(alias) + ".disabled";

    private string GetRoot() => Path.GetFullPath(rootDirectory);
}

/// <summary>
/// Shared write-side validation for catalog entries: the catalog stores SECRET:/ENC: references,
/// never credential values. Used by the admin CLI and the Portal catalog API so both reject the
/// same shapes.
/// </summary>
public static class SharedConnectionValidator
{
    /// <summary>
    /// Returns the first credential field carrying a raw value instead of a SECRET:/ENC: reference,
    /// or null. Deliberately checks the strict credential set only: organization-designated
    /// sensitive metadata (HOST, PATH, ...) may still be stored as plain values — designation
    /// controls resolution and masking, not storage.
    /// </summary>
    public static string? FindRawCredential(IReadOnlyDictionary<string, string> options, string? target)
    {
        foreach (var (key, value) in options)
        {
            if (SecretResolvableFields.IsCredential(key) && !IsReference(value))
                return key;
        }

        if (!string.IsNullOrEmpty(target))
        {
            foreach (var segment in target.Split(';'))
            {
                var parts = segment.Split('=', 2);
                if (parts.Length == 2 && SecretResolvableFields.IsCredential(parts[0].Trim()) && !IsReference(parts[1]))
                    return parts[0].Trim();
            }
        }

        return null;
    }

    private static bool IsReference(string value)
    {
        var trimmed = value.Trim().Trim('\'', '"');
        return trimmed.StartsWith("SECRET:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("ENC:", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class ConnectionCatalogOptions
{
    public string? Provider { get; set; }
    public string? LocalRoot { get; set; }
}

public static class ConnectionCatalogProviderFactory
{
    /// <summary>Returns null when no catalog provider is configured — SHARED: references then fail with a clear error.</summary>
    public static IConnectionCatalogProvider? Create(ConnectionCatalogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Provider))
            return null;

        return options.Provider.Trim().ToUpperInvariant() switch
        {
            "LOCAL" => new LocalConnectionCatalogProvider(
                options.LocalRoot ?? throw new InvalidOperationException("Connection catalog local root is required.")),
            "PORTAL" => throw new InvalidOperationException(
                "The Portal connection catalog provider is only available inside the Report Portal host."),
            _ => throw new InvalidOperationException($"Connection catalog provider '{options.Provider}' is not supported.")
        };
    }
}
