using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using ETL_SQL.Common;

namespace ETL_SQL.Core.Governance;

public sealed record SecretResolutionResult(
    string Name,
    string Value,
    string Provider);

public interface ISecretProvider
{
    string ProviderName { get; }
    Task<SecretResolutionResult> ResolveAsync(string name, CancellationToken cancellationToken = default);
}

public interface IWritableSecretProvider : ISecretProvider
{
    Task StoreAsync(string name, string value, CancellationToken cancellationToken = default);
}

public enum SecretLifecycleStatus
{
    NotFound,
    Active,
    Disabled
}

public interface ISecretLifecycleProvider : IWritableSecretProvider
{
    Task<SecretLifecycleStatus> GetStatusAsync(string name, CancellationToken cancellationToken = default);
    Task DisableAsync(string name, CancellationToken cancellationToken = default);
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}

public sealed class EnvironmentSecretProvider(
    string? prefix = null,
    Func<string, string?>? getEnvironmentVariable = null) : ISecretProvider
{
    private readonly Func<string, string?> _getEnvironmentVariable =
        getEnvironmentVariable ?? Environment.GetEnvironmentVariable;

    public string ProviderName => "Environment";

    public Task<SecretResolutionResult> ResolveAsync(string name, CancellationToken cancellationToken = default)
    {
        var key = BuildEnvironmentKey(name);
        var value = _getEnvironmentVariable(key);
        if (value == null)
            throw new KeyNotFoundException($"Secret '{name}' was not found in environment variable '{key}'.");

        return Task.FromResult(new SecretResolutionResult(name, value, ProviderName));
    }

    private string BuildEnvironmentKey(string name)
    {
        SecretNameValidator.Validate(name);
        var normalized = name.Replace('-', '_').Replace('.', '_').ToUpperInvariant();
        return string.IsNullOrWhiteSpace(prefix) ? normalized : prefix + normalized;
    }
}

public sealed class OsSecretStoreProvider(string rootDirectory) : ISecretLifecycleProvider
{
    public string ProviderName => "OsSecretStore";

    public async Task<SecretResolutionResult> ResolveAsync(string name, CancellationToken cancellationToken = default)
    {
        var path = GetSecretPath(name);
        if (!File.Exists(path))
        {
            if (File.Exists(GetDisabledPath(name)))
                throw new InvalidOperationException(
                    $"Secret '{name}' is disabled. Re-enable it by storing a value with set-secret or rotate-secret.");

            throw new KeyNotFoundException($"Secret '{name}' was not found in the OS secret store.");
        }

        var protectedValue = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (!HasRecognizedProtectionPrefix(protectedValue))
            throw new InvalidOperationException(
                $"Secret '{name}' in the OS secret store is not in a recognized protected format; the store never reads plaintext values.");

        return new SecretResolutionResult(name, CryptoUtils.Unprotect(protectedValue, name), ProviderName);
    }

    public async Task StoreAsync(string name, string value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("Secret value cannot be null or empty.", nameof(value));

        var path = GetSecretPath(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var protectedValue = CryptoUtils.ProtectMachine(value, name);
        await File.WriteAllTextAsync(path, protectedValue, cancellationToken).ConfigureAwait(false);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        // Storing a value re-enables a previously disabled secret.
        var disabledPath = GetDisabledPath(name);
        if (File.Exists(disabledPath))
            File.Delete(disabledPath);
    }

    public Task<SecretLifecycleStatus> GetStatusAsync(string name, CancellationToken cancellationToken = default)
    {
        if (File.Exists(GetSecretPath(name)))
            return Task.FromResult(SecretLifecycleStatus.Active);
        if (File.Exists(GetDisabledPath(name)))
            return Task.FromResult(SecretLifecycleStatus.Disabled);
        return Task.FromResult(SecretLifecycleStatus.NotFound);
    }

    public Task DisableAsync(string name, CancellationToken cancellationToken = default)
    {
        var path = GetSecretPath(name);
        if (!File.Exists(path))
            throw new KeyNotFoundException($"Secret '{name}' was not found in the OS secret store.");

        File.Move(path, GetDisabledPath(name), overwrite: true);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        var path = GetSecretPath(name);
        var disabledPath = GetDisabledPath(name);
        if (!File.Exists(path) && !File.Exists(disabledPath))
            throw new KeyNotFoundException($"Secret '{name}' was not found in the OS secret store.");

        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(disabledPath)) File.Delete(disabledPath);
        return Task.CompletedTask;
    }

    private string GetDisabledPath(string name) => GetSecretPath(name) + ".disabled";

    // Machine scope lets an admin-written secret be read by a differently privileged service
    // account. "DPAPI:" payloads predate machine scoping and stay readable by the account
    // that wrote them; rotating the secret upgrades it to machine scope.
    private static bool HasRecognizedProtectionPrefix(string value) =>
        value.StartsWith("DPAPI-M:", StringComparison.Ordinal)
        || value.StartsWith("DPAPI:", StringComparison.Ordinal)
        || value.StartsWith("MACHINE:", StringComparison.Ordinal);

    private string GetSecretPath(string name)
    {
        SecretNameValidator.Validate(name);
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("Secret store root directory is required.", nameof(rootDirectory));
        if (!Path.IsPathFullyQualified(rootDirectory))
            throw new InvalidOperationException("OS secret store root directory must be fully qualified.");

        return Path.Combine(Path.GetFullPath(rootDirectory), name + ".secret");
    }
}

public sealed class HttpsVaultSecretProvider(
    Uri baseUri,
    HttpClient httpClient,
    string? bearerToken = null) : ISecretProvider
{
    public string ProviderName => "HttpsVault";

    public async Task<SecretResolutionResult> ResolveAsync(string name, CancellationToken cancellationToken = default)
    {
        SecretNameValidator.Validate(name);
        if (!string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("HTTPS vault secret providers must use HTTPS.");

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildSecretUri(name));
        if (!string.IsNullOrWhiteSpace(bearerToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var value = ExtractSecretValue(body);
        return new SecretResolutionResult(name, value, ProviderName);
    }

    private Uri BuildSecretUri(string name) =>
        new(baseUri.ToString().TrimEnd('/') + "/" + Uri.EscapeDataString(name));

    private static string ExtractSecretValue(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new InvalidOperationException("HTTPS vault returned an empty secret response.");

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("value", out var valueElement) &&
                valueElement.ValueKind == JsonValueKind.String)
            {
                return valueElement.GetString() ?? "";
            }
        }
        catch (JsonException)
        {
            return body;
        }

        return body;
    }
}

public sealed class SecretProviderOptions
{
    public string Provider { get; set; } = "Environment";
    public string? EnvironmentPrefix { get; set; }
    public string? OsStoreRoot { get; set; }
    public string? VaultEndpoint { get; set; }
    public string? VaultBearerToken { get; set; }
}

public sealed class SecretProviderFactory(HttpClient httpClient)
{
    public ISecretProvider Create(SecretProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Provider.Trim().ToUpperInvariant() switch
        {
            "ENVIRONMENT" => new EnvironmentSecretProvider(options.EnvironmentPrefix),
            "OSSECRETSTORE" => new OsSecretStoreProvider(
                options.OsStoreRoot ?? throw new InvalidOperationException("OS secret store root is required.")),
            "HTTPSVAULT" => new HttpsVaultSecretProvider(
                ParseVaultEndpoint(options.VaultEndpoint),
                httpClient,
                options.VaultBearerToken),
            _ => throw new InvalidOperationException($"Secret provider '{options.Provider}' is not supported.")
        };
    }

    private static Uri ParseVaultEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("HTTPS vault endpoint is required.");
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("HTTPS vault endpoint must be an absolute URI.");
        return uri;
    }
}

internal static partial class SecretNameValidator
{
    public static void Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Secret name is required.", nameof(name));
        if (!ValidSecretNameRegex().IsMatch(name))
            throw new ArgumentException("Secret names may contain only letters, numbers, period, underscore, and hyphen.", nameof(name));
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+$")]
    private static partial Regex ValidSecretNameRegex();
}
