using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>Coalesces concurrent Prometheus scrapes and bounds expensive snapshot refresh work.</summary>
public sealed class PortalPrometheusMetricsCache(IServiceScopeFactory scopeFactory, TimeProvider clock)
{
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(15);

    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private string? cachedText;
    private DateTimeOffset expiresAt;

    public async Task<string> GetAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var current = Volatile.Read(ref cachedText);
        if (current is not null && now < expiresAt)
            return current;

        await refreshGate.WaitAsync(cancellationToken);
        try
        {
            now = clock.GetUtcNow();
            current = Volatile.Read(ref cachedText);
            if (current is not null && now < expiresAt)
                return current;

            await using var scope = scopeFactory.CreateAsyncScope();
            var exporter = scope.ServiceProvider.GetRequiredService<PortalPrometheusMetricsExporter>();
            var refreshed = await exporter.ExportAsync(cancellationToken);
            expiresAt = clock.GetUtcNow().Add(RefreshInterval);
            Volatile.Write(ref cachedText, refreshed);
            return refreshed;
        }
        finally
        {
            refreshGate.Release();
        }
    }
}
