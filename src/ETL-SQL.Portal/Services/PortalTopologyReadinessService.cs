using ETL_SQL.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Storage;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

public sealed record PortalTopologyReadiness(
    string Status,
    string Mode,
    IReadOnlyDictionary<string, string> Checks,
    IReadOnlyList<string> Findings);

/// <summary>
/// Load-balancer readiness for the configured Portal topology. This intentionally stays narrower
/// than /health: it answers whether this node should receive traffic, while fleet/alerting surfaces
/// retain whole-environment failure detail.
/// </summary>
public sealed class PortalTopologyReadinessService(
    IServiceScopeFactory scopes,
    PortalArtifactStorageBackend artifactBackend,
    INodeRegistryStore nodes,
    IOrchestratorStoreFactory orchestratorStoreFactory,
    PortalConfig config)
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(2);

    public async Task<PortalTopologyReadiness> CheckAsync(CancellationToken ct = default)
    {
        var checks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var findings = new List<string>();
        var liveNodes = Array.Empty<NodeHeartbeat>();

        checks["database"] = await RunCheckAsync(async checkCt =>
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            return await db.Database.CanConnectAsync(checkCt) ? "ok" : "unreachable";
        }, ct);

        checks["storage"] = await RunCheckAsync(async checkCt =>
        {
            // Readiness is a host-level probe without a tenant credential. Probe the configured
            // backend itself; the request-facing facade must remain fail-closed in Shared mode.
            await foreach (var _ in artifactBackend.Storage.EnumerateAsync(
                ArtifactArea.Snapshots,
                prefix: null,
                recursive: false,
                checkCt).WithCancellation(checkCt))
            {
                break;
            }
            return "ok";
        }, ct);

        checks["lease"] = await RunCheckAsync(async checkCt =>
        {
            var result = await nodes.GetLiveNodesAsync().WaitAsync(checkCt);
            liveNodes = result.ToArray();
            return "ok";
        }, ct);

        var mode = ResolveMode();
        AddTopologyChecks(mode, liveNodes, checks, findings);

        var healthy = checks.Values.All(value => string.Equals(value, "ok", StringComparison.OrdinalIgnoreCase));
        return new PortalTopologyReadiness(
            healthy ? "Healthy" : "Unhealthy",
            mode,
            checks,
            findings);
    }

    private void AddTopologyChecks(
        string mode,
        IReadOnlyCollection<NodeHeartbeat> liveNodes,
        Dictionary<string, string> checks,
        List<string> findings)
    {
        checks["topology"] = "ok";

        if (!string.Equals(mode, "HighAvailability", StringComparison.OrdinalIgnoreCase))
            return;

        if (config.Topology.RequirePostgresForHa
            && !string.Equals(config.Database.Provider, "Postgres", StringComparison.OrdinalIgnoreCase))
        {
            checks["topology"] = "portal_database_not_postgres";
            findings.Add("ha-requires-portal-postgres");
        }

        if (config.Topology.RequirePostgresForHa
            && orchestratorStoreFactory.Provider != DatabaseProvider.Postgres)
        {
            checks["topology"] = "orchestrator_database_not_postgres";
            findings.Add("ha-requires-orchestrator-postgres");
        }

        if (config.Topology.RequireSharedKeyRingForHa
            && string.IsNullOrWhiteSpace(config.Storage.KeyRingPath))
        {
            checks["topology"] = "shared_key_ring_missing";
            findings.Add("ha-requires-shared-key-ring");
        }

        if (!config.LoadBalancer.SessionAffinityEnabled)
        {
            checks["topology"] = "session_affinity_disabled";
            findings.Add("ha-requires-session-affinity");
        }

        var livePortalNodes = liveNodes.Count(node =>
            string.Equals(node.Role, "Portal", StringComparison.OrdinalIgnoreCase));
        var liveOrchestratorNodes = liveNodes.Count(node =>
            string.Equals(node.Role, "Orchestrator", StringComparison.OrdinalIgnoreCase));

        var minPortalNodes = Math.Max(1, config.Topology.MinLivePortalNodes);
        var minOrchestratorNodes = Math.Max(0, config.Topology.MinLiveOrchestratorNodes);
        checks["portalNodes"] = livePortalNodes >= minPortalNodes ? "ok" : $"live={livePortalNodes};min={minPortalNodes}";
        checks["orchestratorNodes"] = liveOrchestratorNodes >= minOrchestratorNodes
            ? "ok"
            : $"live={liveOrchestratorNodes};min={minOrchestratorNodes}";

        if (checks["portalNodes"] != "ok")
            findings.Add("ha-live-portal-node-threshold-not-met");
        if (checks["orchestratorNodes"] != "ok")
            findings.Add("ha-live-orchestrator-node-threshold-not-met");
    }

    private string ResolveMode()
    {
        var expected = string.IsNullOrWhiteSpace(config.Topology.ExpectedMode)
            ? "Auto"
            : config.Topology.ExpectedMode.Trim();
        if (!string.Equals(expected, "Auto", StringComparison.OrdinalIgnoreCase))
            return NormalizeMode(expected);

        if (string.Equals(config.Database.Provider, "Postgres", StringComparison.OrdinalIgnoreCase)
            || orchestratorStoreFactory.Provider == DatabaseProvider.Postgres
            || !string.IsNullOrWhiteSpace(config.Storage.KeyRingPath))
        {
            return "HighAvailability";
        }

        return "Standalone";
    }

    private static string NormalizeMode(string mode) =>
        mode.Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .ToLowerInvariant() switch
        {
            "ha" or "highavailability" => "HighAvailability",
            "departmental" => "Departmental",
            "standalone" => "Standalone",
            _ => mode
        };

    private static async Task<string> RunCheckAsync(
        Func<CancellationToken, Task<string>> check,
        CancellationToken requestCt)
    {
        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(requestCt);
        var checkTask = Task.Run(() => check(timeoutCts.Token), CancellationToken.None);
        var completed = await Task.WhenAny(checkTask, Task.Delay(CheckTimeout, requestCt));
        if (completed != checkTask)
        {
            try { timeoutCts.Cancel(); } catch { }
            _ = checkTask.ContinueWith(_ => timeoutCts.Dispose(), TaskScheduler.Default);
            return nameof(TimeoutException);
        }

        try
        {
            return await checkTask;
        }
        catch (Exception ex)
        {
            return ex.GetType().Name;
        }
        finally
        {
            timeoutCts.Dispose();
        }
    }
}
