using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;

namespace ETL_SQL.WorkstationEditor;

public sealed record StudioHeartbeatRequest(string ClientId, bool Dirty);
public sealed record StudioShutdownRequest(bool Force = false);

public sealed class StudioHostLifecycleService(
    WorkstationEditorOptions options,
    IHostApplicationLifetime lifetime,
    TimeSpan? idleTimeoutOverride = null) : BackgroundService
{
    private static readonly TimeSpan ClientExpiry = TimeSpan.FromSeconds(35);
    private readonly ConcurrentDictionary<string, ClientState> _clients = new(StringComparer.Ordinal);
    private int _activeRuns;
    private bool _hasSeenClient;
    private DateTimeOffset _lastClientSeenUtc = DateTimeOffset.UtcNow;
    private readonly TimeSpan _idleTimeout = idleTimeoutOverride
        ?? TimeSpan.FromMinutes(options.IdleShutdownMinutes);

    public int ActiveRuns => Volatile.Read(ref _activeRuns);
    public int ConnectedClients
    {
        get
        {
            PruneExpiredClients();
            return _clients.Count;
        }
    }
    public int DirtyClients
    {
        get
        {
            PruneExpiredClients();
            return _clients.Values.Count(client => client.Dirty);
        }
    }

    public void Heartbeat(StudioHeartbeatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId) || request.ClientId.Length > 128)
            throw new ArgumentException("A valid Studio client ID is required.");
        var now = DateTimeOffset.UtcNow;
        _hasSeenClient = true;
        _lastClientSeenUtc = now;
        _clients[request.ClientId] = new ClientState(now, request.Dirty);
    }

    public void Disconnect(string clientId)
    {
        if (!string.IsNullOrWhiteSpace(clientId)) _clients.TryRemove(clientId, out _);
        _lastClientSeenUtc = DateTimeOffset.UtcNow;
    }

    public IDisposable BeginRun()
    {
        Interlocked.Increment(ref _activeRuns);
        return new RunLease(this);
    }

    public bool TryRequestShutdown(bool force, out string? reason)
    {
        if (!force && ActiveRuns > 0)
        {
            reason = "Studio still has an active run.";
            return false;
        }
        if (!force && DirtyClients > 0)
        {
            reason = "Studio still has an unsaved browser document.";
            return false;
        }

        reason = null;
        _ = Task.Run(async () =>
        {
            await Task.Delay(150);
            lifetime.StopApplication();
        });
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_idleTimeout <= TimeSpan.Zero) return;
        var pollingInterval = _idleTimeout < TimeSpan.FromSeconds(5) ? _idleTimeout : TimeSpan.FromSeconds(5);
        using var timer = new PeriodicTimer(pollingInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            PruneExpiredClients();
            if (!_hasSeenClient || _clients.Count > 0 || ActiveRuns > 0) continue;
            if (DateTimeOffset.UtcNow - _lastClientSeenUtc < _idleTimeout) continue;
            lifetime.StopApplication();
            return;
        }
    }

    private void PruneExpiredClients()
    {
        var cutoff = DateTimeOffset.UtcNow - ClientExpiry;
        var removed = false;
        foreach (var (clientId, client) in _clients)
        {
            if (client.LastSeenUtc < cutoff && _clients.TryRemove(clientId, out _)) removed = true;
        }
        if (removed && _clients.IsEmpty) _lastClientSeenUtc = DateTimeOffset.UtcNow;
    }

    private void EndRun() => Interlocked.Decrement(ref _activeRuns);

    private sealed record ClientState(DateTimeOffset LastSeenUtc, bool Dirty);

    private sealed class RunLease(StudioHostLifecycleService owner) : IDisposable
    {
        private StudioHostLifecycleService? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.EndRun();
    }
}
