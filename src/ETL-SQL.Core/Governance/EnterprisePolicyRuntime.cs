using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Core.Governance;

public sealed record SignedOrganizationPolicyEnvelope
{
    public const string CurrentSchemaVersion = "1.0";

    public string SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required string Tenant { get; init; }
    public required string PolicyVersion { get; init; }
    public DateTimeOffset IssuedAtUtc { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public required string PolicyPayload { get; init; }
    public required string Signature { get; init; }
}

public sealed record EnterprisePolicyCacheEntry(
    SignedOrganizationPolicyEnvelope Envelope,
    DateTimeOffset CachedAtUtc);

public sealed record EffectiveEnterprisePolicy(
    bool IsEnrolled,
    bool IsAvailable,
    string Status,
    string? PolicyVersion,
    string? Source,
    DateTimeOffset? IssuedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? LoadedAtUtc,
    OrganizationPolicyDocument? Document,
    IReadOnlyDictionary<string, string?> ConfigurationValues,
    string? Error = null)
{
    public static EffectiveEnterprisePolicy Standalone { get; } = new(
        false, false, "Standalone", null, null, null, null, null, null,
        new Dictionary<string, string?>());
}

public static class EnterprisePolicySignature
{
    public static byte[] GetSigningPayload(SignedOrganizationPolicyEnvelope envelope)
    {
        var text = string.Join('\n',
            envelope.SchemaVersion,
            envelope.Tenant,
            envelope.PolicyVersion,
            envelope.IssuedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            envelope.ExpiresAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            envelope.PolicyPayload);
        return Encoding.UTF8.GetBytes(text);
    }

    public static string Sign(SignedOrganizationPolicyEnvelope envelope, RSA privateKey) =>
        Convert.ToBase64String(privateKey.SignData(
            GetSigningPayload(envelope), HashAlgorithmName.SHA256, RSASignaturePadding.Pss));

    /// <summary>Checks only whether the envelope's signature verifies under the given public key —
    /// no metadata validation. Used by the authority to detect signing-key rotation (a stored
    /// envelope that no longer verifies under the currently configured key).</summary>
    public static bool VerifiesWithKey(SignedOrganizationPolicyEnvelope envelope, string publicKeyPem)
    {
        try
        {
            using var key = RSA.Create();
            key.ImportFromPem(publicKeyPem);
            return key.VerifyData(GetSigningPayload(envelope), Convert.FromBase64String(envelope.Signature),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or FormatException)
        {
            return false;
        }
    }

    public static OrganizationPolicyDocument VerifyAndParse(
        SignedOrganizationPolicyEnvelope envelope,
        EnterpriseEnrollmentDocument enrollment,
        DateTimeOffset now)
    {
        ValidateMetadata(envelope, enrollment, now);
        byte[] signature;
        byte[] policyBytes;
        try
        {
            signature = Convert.FromBase64String(envelope.Signature);
            policyBytes = Convert.FromBase64String(envelope.PolicyPayload);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Enterprise policy payload or signature is not valid base64.", ex);
        }

        using var key = RSA.Create();
        try { key.ImportFromPem(enrollment.PolicySigningPublicKey); }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            throw new InvalidOperationException("Enterprise enrollment signing key cannot be loaded.", ex);
        }
        if (!key.VerifyData(GetSigningPayload(envelope), signature,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            throw new InvalidOperationException("Enterprise policy signature verification failed.");

        try
        {
            return OrganizationPolicySchema.ParseAndValidateJson(Encoding.UTF8.GetString(policyBytes));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            throw new InvalidOperationException("Signed enterprise policy document is invalid.", ex);
        }
    }

    private static void ValidateMetadata(SignedOrganizationPolicyEnvelope envelope,
        EnterpriseEnrollmentDocument enrollment, DateTimeOffset now)
    {
        if (envelope.SchemaVersion != SignedOrganizationPolicyEnvelope.CurrentSchemaVersion)
            throw new InvalidOperationException($"Unsupported signed policy envelope schema '{envelope.SchemaVersion}'.");
        if (!string.Equals(envelope.Tenant, enrollment.Tenant, StringComparison.Ordinal))
            throw new InvalidOperationException("Enterprise policy tenant does not match machine enrollment.");
        if (string.IsNullOrWhiteSpace(envelope.PolicyVersion) || envelope.PolicyVersion.Length > 200)
            throw new InvalidOperationException("Enterprise policy version is required and must not exceed 200 characters.");
        if (envelope.IssuedAtUtc > now.AddMinutes(5))
            throw new InvalidOperationException("Enterprise policy issuance time is in the future.");
        if (envelope.ExpiresAtUtc <= envelope.IssuedAtUtc)
            throw new InvalidOperationException("Enterprise policy expiry must be after issuance.");
        if (envelope.ExpiresAtUtc <= now)
            throw new InvalidOperationException("Enterprise policy has expired.");
        if (string.IsNullOrWhiteSpace(envelope.PolicyPayload) || string.IsNullOrWhiteSpace(envelope.Signature))
            throw new InvalidOperationException("Enterprise policy payload and signature are required.");
    }
}

public interface ISignedEnterprisePolicySource
{
    string Source { get; }
    Task<SignedOrganizationPolicyEnvelope> LoadAsync(
        EnterpriseEnrollmentDocument enrollment,
        CancellationToken cancellationToken = default);
}

/// <summary>Header names shared by the enrolled-machine client and the policy-authority server so
/// the retrieval contract cannot drift between them.</summary>
public static class EnterprisePolicyTransport
{
    public const string TenantHeader = "X-ETL-SQL-Tenant";
    public const string EnrollmentHeader = "X-ETL-SQL-Enrollment";
    public const string MachineHeader = "X-ETL-SQL-Machine";
}

public sealed class HttpsSignedEnterprisePolicySource(HttpClient http, Uri endpoint) : ISignedEnterprisePolicySource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string Source => endpoint.ToString();

    public async Task<SignedOrganizationPolicyEnvelope> LoadAsync(
        EnterpriseEnrollmentDocument enrollment,
        CancellationToken cancellationToken = default)
    {
        if (endpoint.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Enterprise policy endpoint must use HTTPS.");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Add(EnterprisePolicyTransport.TenantHeader, enrollment.Tenant);
        request.Headers.Add(EnterprisePolicyTransport.EnrollmentHeader, enrollment.EnrollmentId);
        request.Headers.Add(EnterprisePolicyTransport.MachineHeader, enrollment.MachineId);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<SignedOrganizationPolicyEnvelope>(stream,
                   JsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Enterprise policy endpoint returned an empty envelope.");
    }
}

public interface IEnterprisePolicyCacheStore
{
    Task<EnterprisePolicyCacheEntry?> ReadAsync(CancellationToken cancellationToken = default);
    Task WriteAsync(EnterprisePolicyCacheEntry entry, string? serviceIdentity,
        CancellationToken cancellationToken = default);
}

public sealed class FileEnterprisePolicyCacheStore(
    string path,
    IEnterpriseEnrollmentProtectionValidator? validator = null,
    IEnterpriseEnrollmentProtector? protector = null) : IEnterprisePolicyCacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly IEnterpriseEnrollmentProtectionValidator _validator =
        validator ?? new EnterpriseEnrollmentProtectionValidator();
    private readonly IEnterpriseEnrollmentProtector _protector =
        protector ?? new OsEnterpriseEnrollmentProtector();
    private readonly string _path = Path.GetFullPath(path);

    public async Task<EnterprisePolicyCacheEntry?> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return null;
        _validator.Validate(_path);
        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<EnterprisePolicyCacheEntry>(stream,
            JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Enterprise policy cache is empty.");
    }

    public async Task WriteAsync(EnterprisePolicyCacheEntry entry, string? serviceIdentity,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("Enterprise policy cache path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = File.Create(temporary))
                await JsonSerializer.SerializeAsync(stream, entry, JsonOptions, cancellationToken).ConfigureAwait(false);
            _protector.ProtectFile(temporary, serviceIdentity);
            File.Move(temporary, _path, overwrite: true);
            _validator.Validate(_path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

public sealed class EnterprisePolicyLoader(
    ISignedEnterprisePolicySource source,
    IEnterprisePolicyCacheStore cache,
    Func<DateTimeOffset>? clock = null)
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public async Task<EffectiveEnterprisePolicy> LoadAsync(
        EnterpriseEnrollmentDocument enrollment,
        CancellationToken cancellationToken = default)
    {
        var now = _clock();
        EnterprisePolicyCacheEntry? cached = null;
        Exception? cacheFailure = null;
        try { cached = await cache.ReadAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException) { cacheFailure = ex; }

        try
        {
            var live = await source.LoadAsync(enrollment, cancellationToken).ConfigureAwait(false);
            var document = EnterprisePolicySignature.VerifyAndParse(live, enrollment, now);
            if (cached is not null && live.IssuedAtUtc < cached.Envelope.IssuedAtUtc)
                throw new InvalidOperationException("Enterprise policy rollback was rejected.");
            await cache.WriteAsync(new EnterprisePolicyCacheEntry(live, now), enrollment.ServiceIdentity,
                cancellationToken).ConfigureAwait(false);
            return CreateEffective(live, document, "Live", source.Source, now);
        }
        catch (Exception liveFailure) when (liveFailure is not OperationCanceledException)
        {
            try
            {
                if (cached is null)
                    throw new InvalidOperationException("No verified enterprise policy cache is available.", cacheFailure);
                var document = EnterprisePolicySignature.VerifyAndParse(cached.Envelope, enrollment, now);
                var offlineExpiry = cached.CachedAtUtc.AddHours(enrollment.MaxOfflineHours);
                if (offlineExpiry <= now)
                    throw new InvalidOperationException($"Enterprise policy offline cache expired at {offlineExpiry:O}.");
                SecurityEventRuntime.EmitPolicyLoadFailure(enrollment, source.Source,
                    $"Live policy retrieval failed; verified cache is in use. {liveFailure.Message}",
                    cacheAvailable: true, cached.Envelope.PolicyVersion);
                return CreateEffective(cached.Envelope, document, "Cached", "Protected cache", now,
                    $"Live retrieval failed: {liveFailure.Message}");
            }
            catch (Exception cacheUseFailure) when (cacheUseFailure is not OperationCanceledException)
            {
                var message = $"Enterprise policy is unavailable. Live: {liveFailure.Message} Cache: {cacheUseFailure.Message}";
                SecurityEventRuntime.EmitPolicyLoadFailure(enrollment, source.Source, message,
                    cacheAvailable: false, cached?.Envelope.PolicyVersion);
                if (enrollment.FailClosed) throw new InvalidOperationException(message, cacheUseFailure);
                return new EffectiveEnterprisePolicy(true, false, "Unavailable", null, null, null, null,
                    now, null, new Dictionary<string, string?>(), message);
            }
        }
    }

    private static EffectiveEnterprisePolicy CreateEffective(SignedOrganizationPolicyEnvelope envelope,
        OrganizationPolicyDocument document, string status, string source, DateTimeOffset loadedAt,
        string? warning = null) => new(true, true, status, envelope.PolicyVersion, source,
        envelope.IssuedAtUtc, envelope.ExpiresAtUtc, loadedAt, document,
        EnterprisePolicyConfiguration.Flatten(document.ToPolicyValues()), warning);
}

public static class EnterprisePolicyConfiguration
{
    public static IReadOnlyDictionary<string, string?> Flatten(IReadOnlyDictionary<string, object> values)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values)
        {
            if (value is IEnumerable<string> strings)
            {
                var index = 0;
                foreach (var item in strings) result[$"{key}:{index++}"] = item;
            }
            else
            {
                result[key] = value switch
                {
                    bool flag => flag ? "true" : "false",
                    IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                    _ => value.ToString()
                };
            }
        }
        return result;
    }

    public static IConfigurationBuilder AddEnterprisePolicy(
        this IConfigurationBuilder builder,
        EffectiveEnterprisePolicy? policy = null)
    {
        var effective = policy ?? EnterprisePolicyRuntime.Current;
        builder.Add(new EnterprisePolicyConfigurationSource(effective));
        return builder;
    }
}

internal sealed class EnterprisePolicyConfigurationSource(EffectiveEnterprisePolicy initial)
    : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new EnterprisePolicyConfigurationProvider(initial);
}

internal sealed class EnterprisePolicyConfigurationProvider : ConfigurationProvider
{
    public EnterprisePolicyConfigurationProvider(EffectiveEnterprisePolicy initial)
    {
        Replace(initial);
        EnterprisePolicyRuntime.PolicyChanged += ReplaceAndReload;
    }

    private void ReplaceAndReload(EffectiveEnterprisePolicy policy)
    {
        Replace(policy);
        OnReload();
    }

    private void Replace(EffectiveEnterprisePolicy policy) => Data = policy.IsAvailable
        ? new Dictionary<string, string?>(policy.ConfigurationValues, StringComparer.OrdinalIgnoreCase)
        : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
}

public static class EnterprisePolicyRuntime
{
    private static readonly object Sync = new();
    private static readonly SemaphoreSlim TransportSync = new(1, 1);
    private static EffectiveEnterprisePolicy _current = EffectiveEnterprisePolicy.Standalone;
    private static SecurityEventTransportWorker? _securityEventWorker;
    private static HttpClient? _securityEventHttp;
    public static EffectiveEnterprisePolicy Current { get { lock (Sync) return _current; } }
    public static event Action<EffectiveEnterprisePolicy>? PolicyChanged;

    public static async Task<EffectiveEnterprisePolicy> InitializeFromMachineAsync(
        EnterpriseEnrollmentStore? enrollmentStore = null,
        CancellationToken cancellationToken = default)
    {
        var store = enrollmentStore ?? new EnterpriseEnrollmentStore();
        var enrollment = EnterpriseEnrollmentRuntime.ValidateBeforeStartup(store);
        if (enrollment is null)
        {
            SecurityEventRuntime.ConfigureLocalOutbox(SecurityEventOutboxPaths.Standalone());
            await ReplaceSecurityEventTransportAsync(null, null, null).ConfigureAwait(false);
            return SetCurrent(EffectiveEnterprisePolicy.Standalone);
        }

        var outbox = SecurityEventRuntime.ConfigureLocalOutbox(SecurityEventOutboxPaths.Enrolled(store.Path));
        await ReplaceSecurityEventTransportAsync(null, null, null).ConfigureAwait(false);

        using var http = CreateHttpClient(enrollment);
        var source = new HttpsSignedEnterprisePolicySource(http, new Uri(enrollment.PolicyEndpoint));
        var cachePath = Path.Combine(Path.GetDirectoryName(store.Path)!, "cache", "policy-cache.json");
        var loader = new EnterprisePolicyLoader(source, new FileEnterprisePolicyCacheStore(cachePath));
        var effective = SetCurrent(await loader.LoadAsync(enrollment, cancellationToken).ConfigureAwait(false));
        await ReplaceSecurityEventTransportAsync(outbox, enrollment, effective).ConfigureAwait(false);
        return effective;
    }

    public static EffectiveEnterprisePolicy SetCurrent(EffectiveEnterprisePolicy policy)
    {
        lock (Sync) _current = policy;
        PolicyChanged?.Invoke(policy);
        return policy;
    }

    private static HttpClient CreateHttpClient(EnterpriseEnrollmentDocument enrollment)
    {
        var sslOptions = new SslClientAuthenticationOptions();
        if (!string.IsNullOrWhiteSpace(enrollment.ClientCertificateThumbprint))
            sslOptions.ClientCertificates = new X509CertificateCollection
            {
                FindCertificate(enrollment.ClientCertificateThumbprint)
            };
        return PolicyBoundHttp.CreateClient(sslOptions: sslOptions, timeout: TimeSpan.FromSeconds(15));
    }

    private static X509Certificate2 FindCertificate(string thumbprint)
    {
        var normalized = thumbprint.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        foreach (var location in new[] { StoreLocation.LocalMachine, StoreLocation.CurrentUser })
        {
            using var store = new X509Store(StoreName.My, location);
            store.Open(OpenFlags.ReadOnly);
            foreach (var cert in store.Certificates)
            {
                if (cert.HasPrivateKey && (string.Equals(cert.Thumbprint, normalized, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(cert.GetCertHashString(HashAlgorithmName.SHA256), normalized,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    return cert;
                }
                cert.Dispose();
            }
        }
        throw new InvalidOperationException(
            $"Enterprise client certificate '{normalized}' was not found with an accessible private key.");
    }

    private static async Task ReplaceSecurityEventTransportAsync(
        SecurityEventOutbox? outbox,
        EnterpriseEnrollmentDocument? enrollment,
        EffectiveEnterprisePolicy? policy)
    {
        await TransportSync.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_securityEventWorker is not null)
            {
                await _securityEventWorker.DisposeAsync().ConfigureAwait(false);
                _securityEventWorker = null;
            }
            _securityEventHttp?.Dispose();
            _securityEventHttp = null;

            var settings = policy?.Document?.SecurityEvents;
            if (outbox is null || enrollment is null || settings is null
                || string.IsNullOrWhiteSpace(settings.CollectorEndpoint))
                return;
            var transportOptions = CreateSecurityEventTransportOptions(enrollment, settings);
            var client = CreateHttpClient(enrollment);
            var transport = new SecurityEventTransport(outbox, client, transportOptions);
            var worker = new SecurityEventTransportWorker(transport,
                TimeSpan.FromSeconds(settings.IntervalSeconds));
            worker.Start();
            _securityEventHttp = client;
            _securityEventWorker = worker;
        }
        finally
        {
            TransportSync.Release();
        }
    }

    internal static SecurityEventTransportOptions CreateSecurityEventTransportOptions(
        EnterpriseEnrollmentDocument enrollment,
        SecurityEventPolicySection settings)
    {
        ArgumentNullException.ThrowIfNull(enrollment);
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(enrollment.ClientCertificateThumbprint))
            throw new InvalidOperationException(
                "Security event collector delivery requires an enrolled client certificate.");
        if (string.IsNullOrWhiteSpace(settings.CollectorEndpoint))
            throw new InvalidOperationException("Security event collector endpoint is not configured.");
        return new SecurityEventTransportOptions
        {
            CollectorEndpoint = new Uri(settings.CollectorEndpoint),
            TenantId = enrollment.Tenant,
            EnrollmentId = enrollment.EnrollmentId,
            MachineId = enrollment.MachineId,
            BatchSize = settings.BatchSize,
            LeaseDuration = TimeSpan.FromSeconds(settings.LeaseSeconds)
        };
    }
}

public static class SecurityEventOutboxPaths
{
    public static string Standalone()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local))
            throw new InvalidOperationException("Local application data directory could not be resolved for the security event outbox.");
        return Path.Combine(local, "ETL-SQL", "Security", "security-events.db");
    }

    public static string Enrolled(string enrollmentPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(enrollmentPath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(enrollmentPath))
            ?? throw new InvalidOperationException("Enterprise enrollment path has no parent directory.");
        return Path.Combine(directory, "cache", "security-events.db");
    }
}
