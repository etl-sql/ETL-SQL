using System.Net.Http.Json;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>The read-only health summary one environment exposes to the fleet aggregator (P2.2):
/// status plus the operational signals an operator needs to triage a fleet, and nothing else — no
/// report data, scripts, secrets, or identities.</summary>
public sealed record FleetEnvironmentStatus(
    string Environment,
    string Status,
    int QueueDepth,
    int ActiveExecutions,
    int FailedRefreshes,
    int AuditOutboxPending,
    int AuditOutboxFailed,
    string Storage,
    DateTime CapturedAtUtc);

/// <summary>One environment the aggregator polls: its label, base URL, and a scoped FleetReader
/// bearer token. The token authorizes only <c>GET /api/fleet/status</c> in that environment.</summary>
public sealed record FleetEnvironmentDescriptor(string Name, Uri BaseUrl, string? BearerToken);

/// <summary>Per-environment poll outcome; <see cref="Status"/> is null when the environment is
/// unreachable or returned a non-success response (the aggregator never fails the whole fleet view
/// because one environment is down).</summary>
public sealed record FleetEnvironmentResult(
    string Name, bool Reachable, FleetEnvironmentStatus? Status, string? Error);

public sealed record FleetHealthReport(DateTime GeneratedAtUtc, IReadOnlyList<FleetEnvironmentResult> Environments)
{
    public int Total => Environments.Count;
    public int Unreachable => Environments.Count(e => !e.Reachable);
    public int Unhealthy =>
        Environments.Count(e => e.Reachable && e.Status is { } s
            && !string.Equals(s.Status, "Healthy", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Read-only fleet health aggregation (P2.2). Fans out to each environment's
/// <c>GET /api/fleet/status</c> with its scoped FleetReader token and merges the results. It only
/// ever issues that single read-only GET — it never writes, runs scripts, or reads report data — and
/// it tolerates unreachable environments rather than failing the whole view, so a fleet operator
/// gets a complete picture even during a partial outage. See the fleet trust boundary in
/// Departmental_Isolation.md.
/// </summary>
public sealed class FleetHealthAggregator(HttpClient http)
{
    public async Task<FleetHealthReport> AggregateAsync(
        IEnumerable<FleetEnvironmentDescriptor> environments, CancellationToken ct = default)
    {
        var results = await Task.WhenAll(environments.Select(e => PollAsync(e, ct)));
        return new FleetHealthReport(DateTime.UtcNow, results);
    }

    private async Task<FleetEnvironmentResult> PollAsync(FleetEnvironmentDescriptor env, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(env.BaseUrl, "api/fleet/status"));
            if (!string.IsNullOrWhiteSpace(env.BearerToken))
                request.Headers.Authorization = new("Bearer", env.BearerToken);

            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return new FleetEnvironmentResult(env.Name, false, null, $"HTTP {(int)response.StatusCode}");

            var status = await response.Content.ReadFromJsonAsync<FleetEnvironmentStatus>(ct);
            return status is null
                ? new FleetEnvironmentResult(env.Name, false, null, "empty response")
                : new FleetEnvironmentResult(env.Name, true, status, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new FleetEnvironmentResult(env.Name, false, null, ex.Message);
        }
    }
}
