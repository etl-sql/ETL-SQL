using System.Text.Json;
using ETL_SQL.Common;

namespace ETL_SQL.Core.Governance;

/// <summary>
/// A shared connection catalog entry: connector type, non-secret options, and SECRET: references.
/// Credential fields hold references, never resolved values, so rotating a secret never touches
/// catalog entries.
///
/// <para>An entry resolves to exactly one of two bindings. A <b>direct</b> binding names a target the
/// execution plane may reach itself. A <b>Gateway</b> binding sets <see cref="Gateway"/> and carries
/// no target and no credential, because those are Gateway-local facts
/// (<c>docs/architecture/SaaSTenantIsolation.md</c> §11.2). Which one an alias resolves to is an
/// administrative decision: promoting a script between environments changes the binding, never the
/// script, and there is no script syntax that can request or bypass Gateway routing.</para>
/// </summary>
public sealed record SharedConnectionDefinition(
    string Alias,
    string ConnectorType,
    string? Target,
    IReadOnlyDictionary<string, string> Options,
    bool Disabled,
    IReadOnlyCollection<string>? SensitiveFields = null,
    GatewayResourceBinding? Gateway = null,
    string? OwnerPrincipal = null,
    IReadOnlyCollection<string>? AllowedPrincipals = null,
    IReadOnlyCollection<string>? AllowedGroups = null,
    string? TenantId = null,
    bool RequireAcl = false);

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
    Task<SharedConnectionDefinition> GetDefinitionAsync(string alias, CancellationToken cancellationToken = default);
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
public sealed class LocalConnectionCatalogProvider(
    string rootDirectory,
    LocalVaultProtectionScope protectionScope = LocalVaultProtectionScope.LocalMachine,
    string? serviceIdentity = null,
    ILocalStoreProtector? protector = null) : IWritableConnectionCatalogProvider
{
    private readonly ILocalStoreProtector _protector = protector ?? new OsLocalStoreProtector();

    public LocalVaultProtectionScope ProtectionScope => protectionScope;
    public string ProviderName => "LocalCatalog";

    private sealed record EntryPayload(
        string ConnectorType, string? Target, Dictionary<string, string> Options, List<string>? SensitiveFields = null,
        GatewayResourceBinding? Gateway = null,
        string? OwnerPrincipal = null,
        List<string>? AllowedPrincipals = null,
        List<string>? AllowedGroups = null,
        string? TenantId = null,
        bool RequireAcl = false);

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
        EnforceAccess(alias, payload, identity);
        return ToDefinition(alias, payload, disabled: false);
    }

    public async Task StoreAsync(SharedConnectionDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.ConnectorType))
            throw new ArgumentException("A connector type is required.", nameof(definition));
        // A Gateway-bound entry must not be storable with an endpoint or credential, whatever the
        // caller passed — the store is the last place that can refuse.
        GatewayBindingValidator.EnsureValid(definition.Gateway, definition.Target, definition.Options);

        var path = GetEntryPath(definition.Alias);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var payload = new EntryPayload(
            definition.ConnectorType.Trim(),
            definition.Target,
            new Dictionary<string, string>(definition.Options, StringComparer.OrdinalIgnoreCase),
            definition.SensitiveFields?.ToList(),
            definition.Gateway,
            NormalizePrincipal(definition.OwnerPrincipal),
            NormalizeDistinctList(definition.AllowedPrincipals),
            NormalizeDistinctList(definition.AllowedGroups),
            string.IsNullOrWhiteSpace(definition.TenantId) ? null : definition.TenantId.Trim(),
            definition.RequireAcl);
        var protectedValue = LocalStoreProtection.ProtectValue(
            JsonSerializer.Serialize(payload), definition.Alias, protectionScope);
        await File.WriteAllTextAsync(path, protectedValue, cancellationToken).ConfigureAwait(false);
        _protector.ProtectFile(path, protectionScope, serviceIdentity);
        _protector.ProtectDirectory(directory, protectionScope, serviceIdentity);

        // Storing an entry re-enables a previously disabled alias.
        var disabledPath = GetDisabledPath(definition.Alias);
        if (File.Exists(disabledPath))
            File.Delete(disabledPath);
    }

    public async Task<SharedConnectionDefinition> GetDefinitionAsync(
        string alias,
        CancellationToken cancellationToken = default)
    {
        var path = GetEntryPath(alias);
        var disabled = false;
        if (!File.Exists(path))
        {
            path = GetDisabledPath(alias);
            disabled = true;
        }

        if (!File.Exists(path))
            throw new KeyNotFoundException($"Shared connection '{alias}' was not found in the connection catalog.");

        var protectedValue = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var payload = Deserialize(alias, CryptoUtils.Unprotect(protectedValue, alias));
        return ToDefinition(alias, payload, disabled);
    }

    private static SharedConnectionDefinition ToDefinition(string alias, EntryPayload payload, bool disabled) =>
        new(
            alias,
            payload.ConnectorType,
            payload.Target,
            payload.Options,
            disabled,
            payload.SensitiveFields,
            payload.Gateway,
            payload.OwnerPrincipal,
            payload.AllowedPrincipals,
            payload.AllowedGroups,
            payload.TenantId,
            payload.RequireAcl);

    private static void EnforceAccess(string alias, EntryPayload payload, ExecutionIdentity? identity)
    {
        bool isAclProtected = payload.RequireAcl
            || !string.IsNullOrWhiteSpace(payload.OwnerPrincipal)
            || payload.AllowedPrincipals is { Count: > 0 }
            || payload.AllowedGroups is { Count: > 0 }
            || !string.IsNullOrWhiteSpace(payload.TenantId);

        if (!isAclProtected)
            return;

        if (identity is null)
        {
            throw new UnauthorizedAccessException(
                $"Identity '(none)' is not authorized to use shared connection '{alias}'.");
        }

        if (!string.IsNullOrWhiteSpace(payload.TenantId) &&
            !string.Equals(identity.TenantId, payload.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                $"Identity '{identity.EffectiveUser}' is not authorized to use shared connection '{alias}'.");
        }

        if (identity.IsAdmin)
            return;

        if (!string.IsNullOrWhiteSpace(payload.OwnerPrincipal) &&
            string.Equals(identity.EffectiveUser, payload.OwnerPrincipal, StringComparison.OrdinalIgnoreCase))
            return;

        if (payload.AllowedPrincipals is { Count: > 0 } &&
            payload.AllowedPrincipals.Contains(identity.EffectiveUser, StringComparer.OrdinalIgnoreCase))
            return;

        if (payload.AllowedGroups is { Count: > 0 } &&
            payload.AllowedGroups.Any(identity.HasGroup))
            return;

        throw new UnauthorizedAccessException(
            $"Identity '{identity.EffectiveUser}' is not authorized to use shared connection '{alias}'.");
    }

    private static string? NormalizePrincipal(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<string>? NormalizeDistinctList(IReadOnlyCollection<string>? values)
    {
        if (values is null || values.Count == 0)
            return null;

        var list = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return list.Count == 0 ? null : list;
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

        var disabledPath = GetDisabledPath(alias);
        File.Move(path, disabledPath, overwrite: true);
        _protector.ProtectFile(disabledPath, protectionScope, serviceIdentity);
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
        _protector.ProtectFile(path, protectionScope, serviceIdentity);
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
        if (string.IsNullOrWhiteSpace(alias) || alias.Contains("..") || alias.Contains('/') || alias.Contains('\\'))
            throw new ArgumentException($"Invalid alias path: '{alias}'", nameof(alias));

        SecretNameValidator.Validate(alias);
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("Connection catalog root directory is required.", nameof(rootDirectory));
        if (!Path.IsPathFullyQualified(rootDirectory))
            throw new InvalidOperationException("Connection catalog root directory must be fully qualified.");

        var root = GetRoot();
        var safeFileName = Path.GetFileName(alias) + ".connection";
        var fullPath = Path.GetFullPath(Path.Combine(root, safeFileName));
        var rootWithSep = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Invalid alias path: '{alias}'", nameof(alias));

        return fullPath;
    }

    private string GetDisabledPath(string alias)
    {
        var activePath = GetEntryPath(alias);
        var disabledPath = activePath + ".disabled";
        var root = GetRoot();
        var rootWithSep = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!disabledPath.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Invalid alias path: '{alias}'", nameof(alias));

        return disabledPath;
    }

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
    public LocalVaultProtectionScope ProtectionScope { get; set; } = LocalVaultProtectionScope.LocalMachine;
    public string? ServiceIdentity { get; set; }

    public static ConnectionCatalogOptions FromConfiguration(Func<string, string?> getSetting)
    {
        ArgumentNullException.ThrowIfNull(getSetting);
        return new()
        {
            Provider = getSetting("Governance:ConnectionCatalog:Provider"),
            LocalRoot = getSetting("Governance:ConnectionCatalog:LocalRoot"),
            ProtectionScope = LocalStoreProtection.ParseProtectionScope(
                getSetting("Governance:ConnectionCatalog:ProtectionScope")),
            ServiceIdentity = getSetting("Governance:ConnectionCatalog:ServiceIdentity")
        };
    }
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
                options.LocalRoot ?? throw new InvalidOperationException("Connection catalog local root is required."),
                options.ProtectionScope,
                options.ServiceIdentity),
            "PORTAL" => throw new InvalidOperationException(
                "The Portal connection catalog provider is only available inside the Portal host."),
            _ => throw new InvalidOperationException($"Connection catalog provider '{options.Provider}' is not supported.")
        };
    }
}
