using System.Net.Http;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace ETL_SQL.Core.Governance;

public sealed record OrganizationPolicySourceResult(
    OrganizationPolicyDocument Document,
    string Source,
    DateTimeOffset LoadedAt);

public interface IOrganizationPolicySource
{
    string Source { get; }
    Task<OrganizationPolicySourceResult> LoadAsync(CancellationToken cancellationToken = default);
}

public interface IProtectedPolicyFileValidator
{
    void ValidateProtectedFile(string path);
}

public sealed class ProtectedPolicyFileValidator : IProtectedPolicyFileValidator
{
    public void ValidateProtectedFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Policy file path is required.", nameof(path));
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("Local organization policy files must use fully qualified paths.");
        if (!File.Exists(path))
            throw new FileNotFoundException("Organization policy file was not found.", path);

        if (OperatingSystem.IsWindows())
            ValidateWindowsAcl(path);
        else
            ValidateUnixMode(path);
    }

    [SupportedOSPlatform("windows")]
    private static void ValidateWindowsAcl(string path)
    {
        var security = new FileInfo(path).GetAccessControl();
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType != AccessControlType.Allow)
                continue;
            if ((rule.FileSystemRights & (FileSystemRights.Write | FileSystemRights.Modify | FileSystemRights.FullControl)) == 0)
                continue;
            if (IsBroadPrincipal((SecurityIdentifier)rule.IdentityReference))
                throw new InvalidOperationException("Organization policy file grants write access to a broad OS principal.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsBroadPrincipal(SecurityIdentifier sid) =>
        sid.IsWellKnown(WellKnownSidType.WorldSid) ||
        sid.IsWellKnown(WellKnownSidType.BuiltinUsersSid) ||
        sid.IsWellKnown(WellKnownSidType.AuthenticatedUserSid);

    [UnsupportedOSPlatform("windows")]
    private static void ValidateUnixMode(string path)
    {
        var mode = File.GetUnixFileMode(path);
        const UnixFileMode disallowed =
            UnixFileMode.GroupWrite |
            UnixFileMode.OtherWrite;

        if ((mode & disallowed) != 0)
            throw new InvalidOperationException("Organization policy file must not be writable by group or other users.");
    }
}

public sealed class LocalProtectedOrganizationPolicySource(
    string path,
    IProtectedPolicyFileValidator? validator = null,
    Func<DateTimeOffset>? clock = null) : IOrganizationPolicySource
{
    private readonly IProtectedPolicyFileValidator _validator = validator ?? new ProtectedPolicyFileValidator();
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public string Source => Path.GetFullPath(path);

    public async Task<OrganizationPolicySourceResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        _validator.ValidateProtectedFile(Source);
        var json = await File.ReadAllTextAsync(Source, cancellationToken).ConfigureAwait(false);
        var document = OrganizationPolicySchema.ParseAndValidateJson(json);
        return new OrganizationPolicySourceResult(document, Source, _clock());
    }
}

public sealed class HttpsOrganizationPolicySource(
    Uri uri,
    HttpClient httpClient,
    Func<DateTimeOffset>? clock = null) : IOrganizationPolicySource
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public string Source => uri.ToString();

    public async Task<OrganizationPolicySourceResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Remote organization policy sources must use HTTPS.");

        using var response = await httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var document = OrganizationPolicySchema.ParseAndValidateJson(json);
        return new OrganizationPolicySourceResult(document, Source, _clock());
    }
}

public sealed class OrganizationPolicySourceOptions
{
    public string? LocalPath { get; set; }
    public string? HttpsEndpoint { get; set; }
}

public sealed class OrganizationPolicySourceFactory(
    HttpClient httpClient,
    IProtectedPolicyFileValidator? validator = null,
    Func<DateTimeOffset>? clock = null)
{
    public IReadOnlyList<IOrganizationPolicySource> Create(OrganizationPolicySourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var sources = new List<IOrganizationPolicySource>();
        if (!string.IsNullOrWhiteSpace(options.LocalPath))
            sources.Add(new LocalProtectedOrganizationPolicySource(options.LocalPath, validator, clock));

        if (!string.IsNullOrWhiteSpace(options.HttpsEndpoint))
        {
            if (!Uri.TryCreate(options.HttpsEndpoint, UriKind.Absolute, out var endpoint))
                throw new InvalidOperationException("Governance policy HTTPS endpoint must be an absolute URI.");
            sources.Add(new HttpsOrganizationPolicySource(endpoint, httpClient, clock));
        }

        return sources;
    }
}

public sealed class OrganizationPolicyLoader(IReadOnlyList<IOrganizationPolicySource> sources)
{
    public async Task<OrganizationPolicySourceResult> LoadFirstAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (sources.Count == 0)
            throw new InvalidOperationException("No organization policy sources are configured.");

        var failures = new List<string>();
        foreach (var source in sources)
        {
            try
            {
                return await source.LoadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add($"{source.Source}: {ex.Message}");
            }
        }

        throw new InvalidOperationException("No organization policy source could be loaded: " + string.Join("; ", failures));
    }
}
