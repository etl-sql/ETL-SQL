using System.Collections.Concurrent;
using ETL_SQL.ReportHosting;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// Per-user DashboardService pool with LRU eviction.
/// Keyed on (reportId, userId) so each user has independent parameter state.
/// On eviction the user's next interaction transparently rebuilds from the current snapshot.
/// </summary>
public class SessionCache : IHostedService, IDisposable, IAsyncDisposable
{
    private readonly record struct SessionKey(int ReportId, int UserId);

    private readonly record struct Entry(DashboardService Service, string ScriptPath)
    {
        public DateTime LastAccess { get; init; } = DateTime.UtcNow;
        public Entry Touch() => this with { LastAccess = DateTime.UtcNow };
    }

    private readonly ConcurrentDictionary<SessionKey, Entry> _sessions = new();
    private readonly PortalConfig _config;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SessionCache> _log;
    private Timer? _evictionTimer;

    public SessionCache(PortalConfig config, IServiceScopeFactory scopeFactory, ILogger<SessionCache> log)
    {
        _config = config;
        _scopeFactory = scopeFactory;
        _log    = log;
    }

    /// <summary>Returns the existing session or creates a fresh one from scriptPath.</summary>
    public DashboardService GetOrCreate(
        int reportId,
        int userId,
        string scriptPath,
        bool isAdministrator = false)
    {
        var key = new SessionKey(reportId, userId);

        if (_sessions.TryGetValue(key, out var existing) && existing.ScriptPath == scriptPath)
        {
            _sessions[key] = existing.Touch();
            return existing.Service;
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(1, _config.Resources.ExecutionTimeoutSeconds));
        // Interactive viewing runs as the real user so dataset ACLs (CanReadAsync) are enforced;
        // reportId links any datasets the script CREATEs to this report (and thus its folder).
        var callerContext = isAdministrator
            ? $"UserId={userId};IsAdmin=true"
            : $"UserId={userId}";
        var svc = new DashboardService(
            scriptPath,
            _scopeFactory,
            timeout,
            callerContext,
            reportId,
            _config.Dataset.AtRestKey);
        var entry = new Entry(svc, scriptPath);
        _sessions[key] = entry;

        Evict();
        return svc;
    }

    /// <summary>Removes all sessions for a report (called on snapshot invalidation).</summary>
    public async Task InvalidateReportAsync(int reportId)
    {
        foreach (var key in _sessions.Keys.Where(k => k.ReportId == reportId))
            if (_sessions.TryRemove(key, out var entry)) await entry.Service.DisposeAsync();
    }

    // ── IHostedService ────────────────────────────────────────────────────────

    public Task StartAsync(CancellationToken ct)
    {
        // Evict idle sessions every minute
        _evictionTimer = new Timer(_ => Evict(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _evictionTimer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _evictionTimer?.Dispose();
        foreach (var entry in _sessions.Values)
        {
            _ = entry.Service.DisposeAsync();
        }
        _sessions.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        _evictionTimer?.Dispose();
        foreach (var entry in _sessions.Values)
            await entry.Service.DisposeAsync();

        _sessions.Clear();
        GC.SuppressFinalize(this);
    }

    // ── Eviction ──────────────────────────────────────────────────────────────

    private void Evict()
    {
        var maxSize = _config.Resources.SessionCacheMaxSize;
        var ttl     = TimeSpan.FromMinutes(_config.Resources.SessionCacheTtlMinutes);
        var now     = DateTime.UtcNow;

        // First remove idle sessions beyond TTL
        foreach (var (key, entry) in _sessions)
            if (now - entry.LastAccess > ttl)
                if (_sessions.TryRemove(key, out var e)) _ = e.Service.DisposeAsync();

        // Then trim to max size by evicting oldest
        if (_sessions.Count > maxSize)
        {
            var evict = _sessions
                .OrderBy(kv => kv.Value.LastAccess)
                .Take(_sessions.Count - maxSize)
                .Select(kv => kv.Key);

            foreach (var key in evict)
                if (_sessions.TryRemove(key, out var e)) _ = e.Service.DisposeAsync();

            _log.LogDebug("SessionCache evicted to {Size} entries", _sessions.Count);
        }
    }
}
