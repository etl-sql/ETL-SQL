using ETL_SQL.Portal.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class SessionCacheTests : IDisposable
{
    [Fact]
    public void TimerEvictionFailureIsLoggedWithoutEscapingAndNextPassCanRun()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var config = new PortalConfig();
        var log = new Moq.Mock<Microsoft.Extensions.Logging.ILogger<SessionCache>>();
        using var cache = new SessionCache(config, services.GetRequiredService<IServiceScopeFactory>(), log.Object);
        var evict = typeof(SessionCache).GetMethod("EvictScheduled",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var resources = config.Resources;
        config.Resources = null!; // Inject a failure in the timer's eviction body.
        evict.Invoke(cache, null);
        config.Resources = resources;
        evict.Invoke(cache, null);
        log.Verify(x => x.Log(Microsoft.Extensions.Logging.LogLevel.Error,
            Moq.It.IsAny<Microsoft.Extensions.Logging.EventId>(), Moq.It.IsAny<Moq.It.IsAnyType>(),
            Moq.It.IsAny<Exception>(), Moq.It.IsAny<Func<Moq.It.IsAnyType, Exception?, string>>()), Moq.Times.Once);
    }

    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"session_cache_{Guid.NewGuid():N}");

    public SessionCacheTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private SessionCache NewCache()
    {
        var config = new PortalConfig
        {
            ScriptRootPath = _tempDir,
            Resources = new ResourcesConfig
            {
                ExecutionTimeoutSeconds = 30,
                SessionCacheMaxSize = 10,
                SessionCacheTtlMinutes = 5
            }
        };
        var scopeFactory = new ServiceCollection()
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        return new SessionCache(config, scopeFactory, NullLogger<SessionCache>.Instance);
    }

    [Fact]
    public void GetOrCreate_SameKeyScriptAndRole_ReturnsSameInstance()
    {
        using var cache = NewCache();
        var script = Path.Combine(_tempDir, "a.rptsql");

        var first = cache.GetOrCreate(reportId: 1, userId: 7, script);
        var second = cache.GetOrCreate(reportId: 1, userId: 7, script);

        Assert.Same(first, second);
    }

    /// <summary>
    /// A session created with admin elevation must not keep serving after the caller's role
    /// changes — the caller context is part of the session identity, so a role flip rebuilds.
    /// </summary>
    [Fact]
    public void GetOrCreate_RoleChange_ReplacesSession()
    {
        using var cache = NewCache();
        var script = Path.Combine(_tempDir, "a.rptsql");

        var asAdmin = cache.GetOrCreate(reportId: 1, userId: 7, script, isAdministrator: true);
        var asUser = cache.GetOrCreate(reportId: 1, userId: 7, script, isAdministrator: false);
        var asUserAgain = cache.GetOrCreate(reportId: 1, userId: 7, script, isAdministrator: false);

        Assert.NotSame(asAdmin, asUser);
        Assert.Same(asUser, asUserAgain);
    }

    [Fact]
    public void GetOrCreate_ScriptPathChange_ReplacesSession()
    {
        using var cache = NewCache();

        var first = cache.GetOrCreate(reportId: 1, userId: 7, Path.Combine(_tempDir, "a.rptsql"));
        var second = cache.GetOrCreate(reportId: 1, userId: 7, Path.Combine(_tempDir, "b.rptsql"));

        Assert.NotSame(first, second);
    }

    [Fact]
    public void GetOrCreate_SameNumericIdsAcrossTenants_ReturnsDifferentInstances()
    {
        using var cache = NewCache();
        var script = Path.Combine(_tempDir, "a.rptsql");

        var alpha = cache.GetOrCreate(1, 7, script, keyScope: "tenant-alpha");
        var beta = cache.GetOrCreate(1, 7, script, keyScope: "tenant-beta");

        Assert.NotSame(alpha, beta);
        Assert.Same(alpha, cache.GetOrCreate(1, 7, script, keyScope: "tenant-alpha"));
        Assert.Same(beta, cache.GetOrCreate(1, 7, script, keyScope: "tenant-beta"));
    }

    [Fact]
    public void GetOrCreate_AcrossHaNodes_RemainsNodeLocalAndStickyWithinEachNode()
    {
        using var nodeA = NewCache();
        using var nodeB = NewCache();
        var script = Path.Combine(_tempDir, "a.rptsql");

        var sessionA = nodeA.GetOrCreate(1, 7, script, keyScope: "tenant-alpha");
        var sessionB = nodeB.GetOrCreate(1, 7, script, keyScope: "tenant-alpha");

        Assert.NotSame(sessionA, sessionB);
        Assert.Same(sessionA, nodeA.GetOrCreate(1, 7, script, keyScope: "tenant-alpha"));
        Assert.Same(sessionB, nodeB.GetOrCreate(1, 7, script, keyScope: "tenant-alpha"));
    }

    /// <summary>
    /// Regression for the construction race: concurrent GetOrCreate calls for the same key
    /// must all observe a single winning session rather than each keeping its own.
    /// </summary>
    [Fact]
    public async Task GetOrCreate_ConcurrentSameKey_ConvergesOnOneInstance()
    {
        using var cache = NewCache();
        var script = Path.Combine(_tempDir, "a.rptsql");

        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
            Task.Run(() => cache.GetOrCreate(reportId: 1, userId: 7, script))));

        // After the dust settles the cached instance is what every later caller receives.
        var settled = cache.GetOrCreate(reportId: 1, userId: 7, script);
        Assert.Contains(settled, results);
        Assert.Same(settled, cache.GetOrCreate(reportId: 1, userId: 7, script));
    }

    private static ETL_SQL.Core.Governance.ExecutionIdentity Identity(string user, params string[] groups) => new()
    {
        EffectiveUser = user,
        EffectiveUserId = 7,
        RealUser = user,
        IsAdmin = false,
        Groups = groups,
        Roles = ["Viewer"]
    };

    [Fact]
    public void GetOrCreate_SameIdentity_ReusesTheSession_WhateverTheGroupOrder()
    {
        using var cache = NewCache();
        var script = Path.Combine(_tempDir, "a.rptsql");

        var first = cache.GetOrCreate(1, 7, script, executionIdentity: Identity("ann", "North", "East"));
        var second = cache.GetOrCreate(1, 7, script, executionIdentity: Identity("ann", "east", "NORTH"));

        Assert.Same(first, second);
    }

    /// <summary>A reader removed from a group must not keep a session that ran with it.</summary>
    [Fact]
    public void GetOrCreate_GroupRemoved_ReplacesTheSession()
    {
        using var cache = NewCache();
        var script = Path.Combine(_tempDir, "a.rptsql");

        var before = cache.GetOrCreate(1, 7, script, executionIdentity: Identity("ann", "North"));
        var after = cache.GetOrCreate(1, 7, script, executionIdentity: Identity("ann"));

        Assert.NotSame(before, after);
    }

    [Fact]
    public async Task InvalidateUser_RemovesOnlyThatUsersSession()
    {
        using var cache = NewCache();
        var script = Path.Combine(_tempDir, "a.rptsql");
        var ann = cache.GetOrCreate(1, 7, script);
        var bob = cache.GetOrCreate(1, 8, script);

        await cache.InvalidateUserAsync(1, 7);

        Assert.NotSame(ann, cache.GetOrCreate(1, 7, script));
        Assert.Same(bob, cache.GetOrCreate(1, 8, script));
    }
}
