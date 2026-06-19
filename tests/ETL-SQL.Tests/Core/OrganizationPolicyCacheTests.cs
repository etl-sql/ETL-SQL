using ETL_SQL.Core.Governance;
using Xunit;

namespace ETL_SQL.Tests.Core;

public class OrganizationPolicyCacheTests
{
    [Fact]
    public async Task CachedLoader_WritesCacheAfterLiveLoad()
    {
        var liveResult = SourceResult(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var cache = new MemoryPolicyCacheStore();
        var loader = new CachedOrganizationPolicyLoader(
            new OrganizationPolicyLoader(new[] { new StubPolicySource(liveResult) }),
            cache,
            new OrganizationPolicyCacheOptions { MaxOfflineAge = TimeSpan.FromHours(1) },
            () => DateTimeOffset.Parse("2026-01-01T00:01:00Z"));

        var result = await loader.LoadAsync();

        Assert.Equal(liveResult, result);
        Assert.NotNull(cache.Entry);
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T00:01:00Z"), cache.Entry.CachedAt);
    }

    [Fact]
    public async Task CachedLoader_UsesFreshCacheWhenLiveLoadFails()
    {
        var cache = new MemoryPolicyCacheStore
        {
            Entry = new OrganizationPolicyCacheEntry(
                ValidPolicy(),
                "https://policy.example.test/org-policy.json",
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"))
        };
        var loader = new CachedOrganizationPolicyLoader(
            new OrganizationPolicyLoader(new[] { new FailingPolicySource() }),
            cache,
            new OrganizationPolicyCacheOptions { MaxOfflineAge = TimeSpan.FromHours(2) },
            () => DateTimeOffset.Parse("2026-01-01T01:00:00Z"));

        var result = await loader.LoadAsync();

        Assert.Equal("https://policy.example.test/org-policy.json", result.Source);
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T00:00:00Z"), result.LoadedAt);
    }

    [Fact]
    public async Task CachedLoader_FailsSecureWhenCacheExpired()
    {
        var cache = new MemoryPolicyCacheStore
        {
            Entry = new OrganizationPolicyCacheEntry(
                ValidPolicy(),
                "cache",
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"))
        };
        var loader = new CachedOrganizationPolicyLoader(
            new OrganizationPolicyLoader(new[] { new FailingPolicySource() }),
            cache,
            new OrganizationPolicyCacheOptions { MaxOfflineAge = TimeSpan.FromMinutes(30) },
            () => DateTimeOffset.Parse("2026-01-01T00:31:00Z"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => loader.LoadAsync());

        Assert.Contains("offline cache expired", ex.Message);
        Assert.Contains("failing secure", ex.Message);
    }

    [Fact]
    public async Task CachedLoader_FailsSecureWhenNoCacheExists()
    {
        var loader = new CachedOrganizationPolicyLoader(
            new OrganizationPolicyLoader(new[] { new FailingPolicySource() }),
            new MemoryPolicyCacheStore(),
            new OrganizationPolicyCacheOptions { MaxOfflineAge = TimeSpan.FromHours(1) });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => loader.LoadAsync());

        Assert.Contains("no offline cache is available", ex.Message);
    }

    [Fact]
    public async Task FileCacheStore_RoundTripsValidatedPolicy()
    {
        var path = Path.Combine(Path.GetTempPath(), $"org-policy-cache-{Guid.NewGuid():N}.json");
        var store = new FileOrganizationPolicyCacheStore(path, new AllowProtectedFileValidator());
        var entry = new OrganizationPolicyCacheEntry(
            ValidPolicy(),
            "https://policy.example.test/org-policy.json",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-01-01T00:01:00Z"));

        await store.WriteAsync(entry);
        var roundTrip = await store.ReadAsync();

        Assert.NotNull(roundTrip);
        Assert.Equal(entry.Source, roundTrip.Source);
        Assert.Equal(entry.LoadedAt, roundTrip.LoadedAt);
        Assert.Equal(entry.CachedAt, roundTrip.CachedAt);
        Assert.Equal(entry.Document.SchemaVersion, roundTrip.Document.SchemaVersion);
        Assert.Equal(entry.Document.Connectors.AllowedTypes, roundTrip.Document.Connectors.AllowedTypes);
    }

    private static OrganizationPolicySourceResult SourceResult(DateTimeOffset loadedAt) =>
        new(ValidPolicy(), "https://policy.example.test/org-policy.json", loadedAt);

    private static OrganizationPolicyDocument ValidPolicy() =>
        new()
        {
            Connectors = new ConnectorPolicySection
            {
                AllowedTypes = new[] { "MSSQL" }
            }
        };

    private sealed class MemoryPolicyCacheStore : IOrganizationPolicyCacheStore
    {
        public OrganizationPolicyCacheEntry? Entry { get; set; }

        public Task<OrganizationPolicyCacheEntry?> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Entry);

        public Task WriteAsync(OrganizationPolicyCacheEntry entry, CancellationToken cancellationToken = default)
        {
            Entry = entry;
            return Task.CompletedTask;
        }
    }

    private sealed class StubPolicySource(OrganizationPolicySourceResult result) : IOrganizationPolicySource
    {
        public string Source => result.Source;

        public Task<OrganizationPolicySourceResult> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class FailingPolicySource : IOrganizationPolicySource
    {
        public string Source => "failing";

        public Task<OrganizationPolicySourceResult> LoadAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("source unavailable");
    }

    private sealed class AllowProtectedFileValidator : IProtectedPolicyFileValidator
    {
        public void ValidateProtectedFile(string path)
        {
        }
    }
}
