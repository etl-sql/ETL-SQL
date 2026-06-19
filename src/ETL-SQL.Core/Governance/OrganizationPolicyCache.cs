using System.Text.Json;
using System.Text.Json.Serialization;

namespace ETL_SQL.Core.Governance;

public sealed record OrganizationPolicyCacheEntry(
    OrganizationPolicyDocument Document,
    string Source,
    DateTimeOffset LoadedAt,
    DateTimeOffset CachedAt)
{
    public bool IsFresh(DateTimeOffset now, TimeSpan maxOfflineAge) =>
        maxOfflineAge > TimeSpan.Zero && now - LoadedAt <= maxOfflineAge;

    public OrganizationPolicySourceResult ToSourceResult() =>
        new(Document, Source, LoadedAt);
}

public sealed class OrganizationPolicyCacheOptions
{
    public TimeSpan MaxOfflineAge { get; set; } = TimeSpan.Zero;
}

public interface IOrganizationPolicyCacheStore
{
    Task<OrganizationPolicyCacheEntry?> ReadAsync(CancellationToken cancellationToken = default);
    Task WriteAsync(OrganizationPolicyCacheEntry entry, CancellationToken cancellationToken = default);
}

public sealed class FileOrganizationPolicyCacheStore(
    string path,
    IProtectedPolicyFileValidator? validator = null) : IOrganizationPolicyCacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly IProtectedPolicyFileValidator _validator = validator ?? new ProtectedPolicyFileValidator();

    public async Task<OrganizationPolicyCacheEntry?> ReadAsync(CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath();
        if (!File.Exists(fullPath))
            return null;

        _validator.ValidateProtectedFile(fullPath);
        var json = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var entry = JsonSerializer.Deserialize<OrganizationPolicyCacheEntry>(json, JsonOptions);
        if (entry == null)
            return null;

        var validation = OrganizationPolicySchema.Validate(entry.Document);
        if (!validation.IsValid)
            throw new InvalidOperationException("Cached organization policy is invalid: " + string.Join("; ", validation.Errors));

        return entry;
    }

    public async Task WriteAsync(OrganizationPolicyCacheEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var validation = OrganizationPolicySchema.Validate(entry.Document);
        if (!validation.IsValid)
            throw new InvalidOperationException("Cannot cache invalid organization policy: " + string.Join("; ", validation.Errors));

        var fullPath = GetFullPath();
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(entry, JsonOptions);
        await File.WriteAllTextAsync(fullPath, json, cancellationToken).ConfigureAwait(false);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(fullPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private string GetFullPath()
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Policy cache path is required.", nameof(path));
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("Organization policy cache files must use fully qualified paths.");

        return Path.GetFullPath(path);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

public sealed class CachedOrganizationPolicyLoader(
    OrganizationPolicyLoader liveLoader,
    IOrganizationPolicyCacheStore cacheStore,
    OrganizationPolicyCacheOptions options,
    Func<DateTimeOffset>? clock = null)
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public async Task<OrganizationPolicySourceResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var live = await liveLoader.LoadFirstAvailableAsync(cancellationToken).ConfigureAwait(false);
            await cacheStore.WriteAsync(
                new OrganizationPolicyCacheEntry(live.Document, live.Source, live.LoadedAt, _clock()),
                cancellationToken).ConfigureAwait(false);
            return live;
        }
        catch (Exception liveFailure) when (liveFailure is not OperationCanceledException)
        {
            var cached = await cacheStore.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (cached == null)
                throw new InvalidOperationException("Organization policy could not be loaded and no offline cache is available.", liveFailure);

            var now = _clock();
            if (!cached.IsFresh(now, options.MaxOfflineAge))
            {
                throw new InvalidOperationException(
                    $"Organization policy offline cache expired at {cached.LoadedAt.Add(options.MaxOfflineAge):O}; failing secure.",
                    liveFailure);
            }

            return cached.ToSourceResult();
        }
    }
}
