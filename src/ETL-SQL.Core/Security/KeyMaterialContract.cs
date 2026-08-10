using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace ETL_SQL.Core.Security;

/// <summary>The cryptographic domain a key is authorized to protect.</summary>
public enum KeyPurpose
{
    Dataset,
    Credential,
    Artifact,
    Checkpoint
}

/// <summary>
/// Server-derived request for key material. Tenant and purpose are part of authority, not labels a
/// caller may rewrite after resolution.
/// </summary>
public sealed record KeyMaterialRequest(string Scope, KeyPurpose Purpose, string? Version = null)
{
    public KeyMaterialRequest Normalize()
    {
        if (string.IsNullOrWhiteSpace(Scope))
            throw new ArgumentException("A server-derived key scope is required.", nameof(Scope));
        if (Version is not null && string.IsNullOrWhiteSpace(Version))
            throw new ArgumentException("A key version cannot be empty.", nameof(Version));
        return this with { Scope = Scope.Trim(), Version = Version?.Trim() };
    }
}

/// <summary>Non-secret key metadata safe for diagnostics, manifests, and portable exports.</summary>
public sealed record KeyMaterialDescriptor(
    string Provider,
    string KeyId,
    string Scope,
    KeyPurpose Purpose,
    string Version,
    bool IsCurrent = true);

/// <summary>
/// A short-lived lease over resolved symmetric key bytes. The bytes are deliberately excluded from
/// JSON, logs, equality, and descriptors and are zeroed when the lease is disposed.
/// </summary>
public sealed class ResolvedKeyMaterial : IDisposable
{
    private byte[]? _bytes;

    public ResolvedKeyMaterial(KeyMaterialDescriptor descriptor, byte[] bytes)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        if (bytes is null || bytes.Length < 32)
            throw new ArgumentException("Resolved key material must contain at least 256 bits.", nameof(bytes));
        _bytes = bytes.ToArray();
    }

    public KeyMaterialDescriptor Descriptor { get; }

    [JsonIgnore]
    public ReadOnlyMemory<byte> Bytes => _bytes is null
        ? throw new ObjectDisposedException(nameof(ResolvedKeyMaterial))
        : _bytes;

    public void Dispose()
    {
        if (_bytes is null) return;
        CryptographicOperations.ZeroMemory(_bytes);
        _bytes = null;
    }

    public override string ToString() =>
        $"{Descriptor.Provider}:{Descriptor.KeyId}@{Descriptor.Version} " +
        $"[{Descriptor.Scope}/{Descriptor.Purpose}]";
}

/// <summary>
/// Provider-neutral key boundary. Providers may resolve from an HSM/KMS, vault, environment-backed
/// secret, OS key ring, or test fixture. Callers receive only a disposable lease and safe metadata.
/// </summary>
public interface IKeyMaterialProvider
{
    ValueTask<ResolvedKeyMaterial> ResolveAsync(
        KeyMaterialRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Host-fixed scope used by persistence factories; never accepted from job payloads.</summary>
public sealed record KeyMaterialHostScope(string Value, bool RequireExplicitScope = false);

/// <summary>Non-secret binding from a key authority tuple to an environment secret name.</summary>
public sealed record EnvironmentKeyMaterialBinding(
    string EnvironmentVariable,
    KeyMaterialDescriptor Descriptor);

/// <summary>
/// Host adapter that resolves base64 key bytes from environment variables. Configuration and
/// exports contain only the variable name/key id and version; material remains host-local.
/// </summary>
public sealed class EnvironmentKeyMaterialProvider(
    IEnumerable<EnvironmentKeyMaterialBinding> bindings,
    Func<string, string?>? readVariable = null) : IKeyMaterialProvider
{
    private readonly Func<string, string?> _readVariable = readVariable ?? Environment.GetEnvironmentVariable;
    private readonly Dictionary<(string Scope, KeyPurpose Purpose, string Version), EnvironmentKeyMaterialBinding> _bindings =
        Build(bindings);

    public ValueTask<ResolvedKeyMaterial> ResolveAsync(
        KeyMaterialRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = request.Normalize();
        EnvironmentKeyMaterialBinding? binding;
        if (normalized.Version is null)
        {
            var matches = _bindings.Values.Where(candidate =>
                candidate.Descriptor.Scope.Equals(normalized.Scope, StringComparison.Ordinal)
                && candidate.Descriptor.Purpose == normalized.Purpose
                && candidate.Descriptor.IsCurrent).ToArray();
            binding = matches.Length == 1 ? matches[0] : null;
        }
        else
        {
            _bindings.TryGetValue(
                (normalized.Scope, normalized.Purpose, normalized.Version), out binding);
        }

        if (binding is null)
            throw new KeyNotFoundException(
                $"No environment key binding is available for scope '{normalized.Scope}', purpose " +
                $"'{normalized.Purpose}', version '{normalized.Version ?? "current"}'.");

        var encoded = _readVariable(binding.EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(encoded))
            throw new InvalidOperationException(
                $"Key material environment variable '{binding.EnvironmentVariable}' is not configured.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(encoded); }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"Key material environment variable '{binding.EnvironmentVariable}' must be base64.", ex);
        }
        try { return ValueTask.FromResult(new ResolvedKeyMaterial(binding.Descriptor, bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static Dictionary<(string, KeyPurpose, string), EnvironmentKeyMaterialBinding> Build(
        IEnumerable<EnvironmentKeyMaterialBinding> bindings)
    {
        var result = new Dictionary<(string, KeyPurpose, string), EnvironmentKeyMaterialBinding>();
        foreach (var binding in bindings)
        {
            if (string.IsNullOrWhiteSpace(binding.EnvironmentVariable))
                throw new ArgumentException("An environment variable name is required.", nameof(bindings));
            var descriptor = binding.Descriptor;
            var request = new KeyMaterialRequest(descriptor.Scope, descriptor.Purpose, descriptor.Version).Normalize();
            if (!result.TryAdd((request.Scope, descriptor.Purpose, descriptor.Version), binding))
                throw new ArgumentException("Duplicate scope/purpose/version key binding.", nameof(bindings));
        }
        return result;
    }
}

/// <summary>
/// Small provider used by host adapters after their configured secret source has resolved a key.
/// Entries are keyed by the complete authority tuple, preventing purpose or scope reuse by lookup.
/// </summary>
public sealed class ResolvedKeyMaterialProvider(
    string providerName,
    IEnumerable<(KeyMaterialDescriptor Descriptor, byte[] Bytes)> entries) : IKeyMaterialProvider
{
    private readonly Dictionary<(string Scope, KeyPurpose Purpose, string Version), Entry> _entries =
        Build(providerName, entries);

    public ValueTask<ResolvedKeyMaterial> ResolveAsync(
        KeyMaterialRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = request.Normalize();
        var version = normalized.Version;
        Entry? match;
        if (version is null)
        {
            var matches = _entries
                .Where(pair => pair.Key.Scope.Equals(normalized.Scope, StringComparison.Ordinal)
                    && pair.Key.Purpose == normalized.Purpose)
                .Select(pair => pair.Value)
                .Where(entry => entry.IsCurrent)
                .ToArray();
            match = matches.Length == 1 ? matches[0] : null;
        }
        else
        {
            _entries.TryGetValue((normalized.Scope, normalized.Purpose, version), out match);
        }

        if (match is null)
            throw new KeyNotFoundException(
                $"No key is available for scope '{normalized.Scope}', purpose " +
                $"'{normalized.Purpose}', version '{version ?? "current"}'.");

        return ValueTask.FromResult(new ResolvedKeyMaterial(match.Descriptor, match.Bytes));
    }

    private static Dictionary<(string, KeyPurpose, string), Entry> Build(
        string providerName,
        IEnumerable<(KeyMaterialDescriptor Descriptor, byte[] Bytes)> entries)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            throw new ArgumentException("Provider name is required.", nameof(providerName));
        var result = new Dictionary<(string, KeyPurpose, string), Entry>();
        foreach (var (descriptor, bytes) in entries)
        {
            if (!descriptor.Provider.Equals(providerName, StringComparison.Ordinal))
                throw new ArgumentException("Every descriptor must name the owning provider.", nameof(entries));
            if (string.IsNullOrWhiteSpace(descriptor.KeyId) || string.IsNullOrWhiteSpace(descriptor.Version))
                throw new ArgumentException("Key id and version are required.", nameof(entries));
            var scope = new KeyMaterialRequest(descriptor.Scope, descriptor.Purpose, descriptor.Version)
                .Normalize().Scope;
            var key = (scope, descriptor.Purpose, descriptor.Version);
            if (!result.TryAdd(key, new Entry(descriptor with { Scope = scope }, bytes.ToArray())))
                throw new ArgumentException("Duplicate scope/purpose/version key binding.", nameof(entries));
        }
        var duplicateCurrent = result.Values
            .Where(entry => entry.Descriptor.IsCurrent)
            .GroupBy(entry => (entry.Descriptor.Scope, entry.Descriptor.Purpose))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateCurrent is not null)
            throw new ArgumentException(
                $"Scope '{duplicateCurrent.Key.Scope}' purpose '{duplicateCurrent.Key.Purpose}' has multiple current keys.",
                nameof(entries));
        return result;
    }

    private sealed record Entry(KeyMaterialDescriptor Descriptor, byte[] Bytes)
    {
        public bool IsCurrent => Descriptor.IsCurrent;
    }
}
