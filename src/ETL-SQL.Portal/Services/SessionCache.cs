using System.Collections.Concurrent;
using ETL_SQL.Core.Observability;
using ETL_SQL.ReportHosting;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Per-user DashboardService pool with LRU eviction.
/// Keyed on (tenantId, reportId, userId) so tenants and users have independent parameter state.
/// On eviction the user's next interaction transparently rebuilds from the current snapshot.
/// </summary>
public class SessionCache : IHostedService, IDisposable, IAsyncDisposable
{
    private readonly record struct SessionKey(string TenantId, int ReportId, int UserId);

    private readonly record struct Entry(DashboardService Service, string ScriptPath, string CallerContext)
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
        _log = log;
    }

    /// <summary>Returns the existing session or creates a fresh one from scriptPath.</summary>
    public DashboardService GetOrCreate(
        int reportId,
        int userId,
        string scriptPath,
        bool isAdministrator = false,
        string? keyScope = null)
    {
        var tenantId = string.IsNullOrWhiteSpace(keyScope) ? "portal-host" : keyScope;
        var key = new SessionKey(tenantId, reportId, userId);
        // Interactive viewing runs as the real user so dataset ACLs (CanReadAsync) are enforced;
        // reportId links any datasets the script CREATEs to this report (and thus its folder).
        var callerContext = isAdministrator
            ? $"UserId={userId};IsAdmin=true"
            : $"UserId={userId}";

        while (true)
        {
            if (_sessions.TryGetValue(key, out var existing))
            {
                // A session is reusable only for the same script *and* caller context — a role
                // change must not keep serving through a previously admin-elevated session.
                if (existing.ScriptPath == scriptPath && existing.CallerContext == callerContext)
                {
                    _sessions.TryUpdate(key, existing.Touch(), existing); // best-effort LRU touch
                    return existing.Service;
                }

                var replacement = CreateEntry(reportId, scriptPath, callerContext, keyScope);
                if (_sessions.TryUpdate(key, replacement, existing))
                {
                    _ = existing.Service.DisposeAsync();
                    Evict();
                    return replacement.Service;
                }

                // Lost the replace race — discard our fresh service and re-evaluate.
                _ = replacement.Service.DisposeAsync();
                continue;
            }

            var entry = CreateEntry(reportId, scriptPath, callerContext, keyScope);
            if (_sessions.TryAdd(key, entry))
            {
                Evict();
                return entry.Service;
            }

            // Lost the add race — discard our fresh service and reuse the winner's.
            _ = entry.Service.DisposeAsync();
        }
    }

    private Entry CreateEntry(int reportId, string scriptPath, string callerContext, string? keyScope)
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(1, _config.Resources.ExecutionTimeoutSeconds));
        var svc = new DashboardService(
            scriptPath,
            _scopeFactory,
            timeout,
            callerContext,
            reportId,
            _config.KeyManagement.Enabled ? null : _config.Dataset.AtRestKey,
            keyScope: keyScope);
        return new Entry(svc, scriptPath, callerContext);
    }

    /// <summary>Removes all sessions for a report (called on snapshot invalidation).</summary>
    public async Task InvalidateReportAsync(int reportId, string keyScope = "portal-host")
    {
        foreach (var key in _sessions.Keys.Where(k => k.TenantId == keyScope && k.ReportId == reportId))
            if (_sessions.TryRemove(key, out var entry)) await entry.Service.DisposeAsync();
    }

    // ── IHostedService ────────────────────────────────────────────────────────

    public Task StartAsync(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = BackgroundServiceObservability.StartRun("portal", "session-cache", "start");
        // Evict idle sessions every minute
        _evictionTimer = new Timer(_ => EvictScheduled(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        sw.Stop();
        BackgroundServiceObservability.CompleteRun(
            activity,
            "portal",
            "session-cache",
            "start",
            "success",
            sw.ElapsedMilliseconds);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = BackgroundServiceObservability.StartRun("portal", "session-cache", "stop");
        _evictionTimer?.Change(Timeout.Infinite, 0);
        sw.Stop();
        BackgroundServiceObservability.CompleteRun(
            activity,
            "portal",
            "session-cache",
            "stop",
            "success",
            sw.ElapsedMilliseconds);
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

    private void EvictScheduled()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = BackgroundServiceObservability.StartRun("portal", "session-cache", "evict_idle");
        var status = "success";
        var removed = 0;
        try
        {
            removed = Evict();
        }
        catch
        {
            status = "failure";
            throw;
        }
        finally
        {
            sw.Stop();
            BackgroundServiceObservability.SetRowsProcessed(activity, removed);
            BackgroundServiceObservability.CompleteRun(
                activity,
                "portal",
                "session-cache",
                "evict_idle",
                status,
                sw.ElapsedMilliseconds);
        }
    }

    private int Evict()
    {
        var maxSize = _config.Resources.SessionCacheMaxSize;
        var ttl = TimeSpan.FromMinutes(_config.Resources.SessionCacheTtlMinutes);
        var now = DateTime.UtcNow;
        var removed = 0;

        // First remove idle sessions beyond TTL
        foreach (var (key, entry) in _sessions)
            if (now - entry.LastAccess > ttl)
                if (_sessions.TryRemove(key, out var e))
                {
                    removed++;
                    _ = e.Service.DisposeAsync();
                }

        // Then trim to max size by evicting oldest
        if (_sessions.Count > maxSize)
        {
            var evict = _sessions
                .OrderBy(kv => kv.Value.LastAccess)
                .Take(_sessions.Count - maxSize)
                .Select(kv => kv.Key);

            foreach (var key in evict)
                if (_sessions.TryRemove(key, out var e))
                {
                    removed++;
                    _ = e.Service.DisposeAsync();
                }

            _log.LogDebug("SessionCache evicted to {Size} entries", _sessions.Count);
        }

        return removed;
    }
}
