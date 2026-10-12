using ETL_SQL.Portal.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// A live session answers from the script it ran. A report shown live to each reader has no
/// snapshot run to end its sessions when the author republishes, so the session itself must notice
/// that its script changed: otherwise a tightened row filter does not apply to a reader who keeps
/// the report open.
/// </summary>
[Trait("Category", "Portal")]
public sealed class SessionCacheScriptChangeTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"session_script_{Guid.NewGuid():N}");

    public SessionCacheScriptChangeTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    private SessionCache NewCache()
    {
        var config = new PortalConfig
        {
            ScriptRootPath = _tempDir,
            Resources = new ResourcesConfig { ExecutionTimeoutSeconds = 30, SessionCacheMaxSize = 10, SessionCacheTtlMinutes = 5 }
        };
        var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return new SessionCache(config, scopeFactory, NullLogger<SessionCache>.Instance);
    }

    [Fact]
    public void GetOrCreate_ScriptRewritten_ReplacesTheSession()
    {
        using var cache = NewCache();
        var script = Path.Combine(_tempDir, "a.rptsql");
        File.WriteAllText(script, "SELECT 1 AS A;");
        var before = cache.GetOrCreate(reportId: 1, userId: 7, script);
        Assert.Same(before, cache.GetOrCreate(reportId: 1, userId: 7, script));

        // Same path, same length, later write: the kind of edit that tightens a predicate.
        File.WriteAllText(script, "SELECT 2 AS A;");
        File.SetLastWriteTimeUtc(script, DateTime.UtcNow.AddSeconds(5));

        var after = cache.GetOrCreate(reportId: 1, userId: 7, script);
        Assert.NotSame(before, after);
        Assert.Same(after, cache.GetOrCreate(reportId: 1, userId: 7, script));
    }
}
