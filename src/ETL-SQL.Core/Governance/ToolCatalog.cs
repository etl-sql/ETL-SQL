using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;

namespace ETL_SQL.Core.Governance;

public sealed record ToolDefinition(
    string Name,
    string ToolType,
    IReadOnlyDictionary<string, string> Options,
    bool Disabled);

public interface IToolCatalogProvider
{
    string ProviderName { get; }
    Task<ToolDefinition> ResolveAsync(
        string name,
        ExecutionIdentity? identity = null,
        CancellationToken cancellationToken = default);
}

public interface IWritableToolCatalogProvider : IToolCatalogProvider
{
    Task StoreAsync(ToolDefinition definition, CancellationToken cancellationToken = default);
    Task<ToolDefinition> GetDefinitionAsync(string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);
    Task DisableAsync(string name, CancellationToken cancellationToken = default);
    Task EnableAsync(string name, CancellationToken cancellationToken = default);
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}

public sealed class LocalToolCatalogProvider(string rootDirectory) : IWritableToolCatalogProvider
{
    public string ProviderName => "LocalToolCatalog";

    private sealed record EntryPayload(
        string ToolType, Dictionary<string, string> Options);

    public async Task<ToolDefinition> ResolveAsync(
        string name,
        ExecutionIdentity? identity = null,
        CancellationToken cancellationToken = default)
    {
        var path = GetEntryPath(name);
        if (!File.Exists(path))
        {
            if (File.Exists(GetDisabledPath(name)))
                throw new InvalidOperationException(
                    $"Tool '{name}' is disabled. Re-enable it by storing it again.");

            throw new KeyNotFoundException($"Tool '{name}' was not found in the tool catalog.");
        }

        var protectedValue = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var payload = Deserialize(name, CryptoUtils.Unprotect(protectedValue, name));
        return new ToolDefinition(
            name, payload.ToolType, payload.Options, Disabled: false);
    }

    public async Task StoreAsync(ToolDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.ToolType))
            throw new ArgumentException("A tool type is required.", nameof(definition));

        var path = GetEntryPath(definition.Name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var payload = new EntryPayload(
            definition.ToolType.Trim(),
            new Dictionary<string, string>(definition.Options, StringComparer.OrdinalIgnoreCase));
        var protectedValue = CryptoUtils.ProtectMachine(JsonSerializer.Serialize(payload), definition.Name);
        await File.WriteAllTextAsync(path, protectedValue, cancellationToken).ConfigureAwait(false);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        var disabledPath = GetDisabledPath(definition.Name);
        if (File.Exists(disabledPath)) File.Delete(disabledPath);
    }

    public Task<ToolDefinition> GetDefinitionAsync(string name, CancellationToken cancellationToken = default) => ResolveAsync(name, null, cancellationToken);

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(rootDirectory)) return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        var entries = Directory.GetFiles(rootDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !n!.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            .Cast<string>()
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(entries);
    }

    public Task DisableAsync(string name, CancellationToken cancellationToken = default)
    {
        var active = GetEntryPath(name);
        if (File.Exists(active)) File.Move(active, GetDisabledPath(name), overwrite: true);
        return Task.CompletedTask;
    }

    public Task EnableAsync(string name, CancellationToken cancellationToken = default)
    {
        var disabled = GetDisabledPath(name);
        if (File.Exists(disabled)) File.Move(disabled, GetEntryPath(name), overwrite: true);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        var active = GetEntryPath(name);
        var disabled = GetDisabledPath(name);
        if (File.Exists(active)) File.Delete(active);
        if (File.Exists(disabled)) File.Delete(disabled);
        return Task.CompletedTask;
    }

    private string GetEntryPath(string name) => Path.Combine(rootDirectory, name.ToLowerInvariant() + ".json");
    private string GetDisabledPath(string name) => Path.Combine(rootDirectory, name.ToLowerInvariant() + ".disabled.json");

    private static EntryPayload Deserialize(string name, string json)
    {
        try
        {
            return JsonSerializer.Deserialize<EntryPayload>(json) ?? throw new InvalidOperationException("Null payload.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"The catalog entry for tool '{name}' is corrupt or unreadable.", ex);
        }
    }
}

public sealed class ToolCatalogOptions
{
    public string? Provider { get; set; }
    public string? LocalRoot { get; set; }
}

public static class ToolCatalogProviderFactory
{
    public static IToolCatalogProvider? Create(ToolCatalogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Provider))
            return null;

        return options.Provider.Trim().ToUpperInvariant() switch
        {
            "LOCAL" => new LocalToolCatalogProvider(
                options.LocalRoot ?? throw new InvalidOperationException("Tool catalog local root is required.")),
            "PORTAL" => throw new InvalidOperationException(
                "The Portal tool catalog provider is only available inside the Portal host."),
            _ => throw new InvalidOperationException($"Tool catalog provider '{options.Provider}' is not supported.")
        };
    }
}
