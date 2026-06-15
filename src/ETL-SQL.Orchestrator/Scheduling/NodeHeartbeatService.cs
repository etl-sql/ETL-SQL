using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.Orchestrator.Scheduling
{
    /// <summary>
    /// Practical HA P1.7: keeps this process's row in the shared cluster node registry fresh, generalizing
    /// the per-job lease heartbeat (<c>SchedulerService.StartLeaseHeartbeat</c>) to the node level. On a
    /// configurable interval it renews a TTL heartbeat via <see cref="INodeRegistryStore"/> so every node
    /// has a live view of the cluster; on graceful shutdown it deregisters immediately, and a crashed node
    /// simply ages out when its lease expires.
    ///
    /// <para>All registry I/O happens on the background loop (not in <c>StartAsync</c>) and every failure is
    /// swallowed with a warning: a degraded registry must never take down the host, and at worst the node's
    /// lease lapses and it is treated as offline until the next successful heartbeat.</para>
    /// </summary>
    public sealed class NodeHeartbeatService : BackgroundService
    {
        private readonly INodeRegistryStore _store;
        private readonly IConfiguration _configuration;
        private readonly ILogger<NodeHeartbeatService> _logger;

        /// <summary>Stable, process-unique node id (machine:pid:guid), like the scheduler's lease owner id.</summary>
        public string NodeId { get; } =
            $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

        /// <summary>This node's role in the cluster (e.g. "Portal", "Orchestrator").</summary>
        public string Role { get; }

        public NodeHeartbeatService(
            INodeRegistryStore store, IConfiguration configuration, ILogger<NodeHeartbeatService> logger, string role)
        {
            _store = store;
            _configuration = configuration;
            _logger = logger;
            Role = role;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Nodes heartbeat faster than jobs (default 30s). The minimum guards against a misconfigured
            // tiny TTL that would churn the registry; the interval renews at a third of the TTL so two
            // missed beats still leave the lease valid.
            var ttl = TimeSpan.FromSeconds(Math.Max(10, _configuration.GetValue("Cluster:NodeHeartbeatSeconds", 30)));
            var interval = TimeSpan.FromSeconds(Math.Max(5, ttl.TotalSeconds / 3));
            var metadata = JsonSerializer.Serialize(new
            {
                machine = Environment.MachineName,
                pid = Environment.ProcessId,
                startedUtc = DateTime.UtcNow.ToString("O"),
            });

            _logger.LogInformation(
                "Node heartbeat started: {NodeId} role={Role} ttl={Ttl}s interval={Interval}s.",
                NodeId, Role, ttl.TotalSeconds, interval.TotalSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _store.RegisterOrRenewNodeAsync(NodeId, Role, ttl, metadata);
                }
                catch (Exception ex)
                {
                    // A transient registry failure must not kill the host; the lease simply lapses until
                    // the next successful beat, and other nodes treat this one as offline meanwhile.
                    _logger.LogWarning(ex, "Node {NodeId}: heartbeat renewal failed transiently.", NodeId);
                }

                try
                {
                    await Task.Delay(interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken);
            try
            {
                await _store.DeregisterNodeAsync(NodeId);
                _logger.LogInformation("Node heartbeat stopped and deregistered: {NodeId}.", NodeId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Node {NodeId}: deregistration on shutdown failed (it will age out).", NodeId);
            }
        }
    }

    public static class NodeHeartbeatServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the node heartbeat as a hosted service for a long-running host (Portal, Orchestrator
        /// daemon). Not added by <c>AddEtlSqlEngine</c> so one-shot CLI invocations never register a node.
        /// </summary>
        public static IServiceCollection AddNodeHeartbeat(this IServiceCollection services, string role)
        {
            services.AddHostedService(sp => new NodeHeartbeatService(
                sp.GetRequiredService<INodeRegistryStore>(),
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<ILogger<NodeHeartbeatService>>(),
                role));
            return services;
        }
    }
}
