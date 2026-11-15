using System;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Orchestrator.Service
{
    /// <summary>
    /// Readiness of the job store behind <c>GET /health/ready</c>. <c>/health</c> is liveness only and
    /// stays 200 while the shared database is down; a load balancer or supervisor that probes readiness
    /// takes the node out of rotation instead (HA fault PostgresOutageBrief).
    /// </summary>
    public interface IOrchestratorReadinessProbe
    {
        /// <summary>Completes when the job store answered a read; throws when it did not.</summary>
        Task ProbeAsync(CancellationToken cancellationToken);
    }

    /// <summary>Reads one job definition page from the configured job store.</summary>
    public sealed class JobStoreReadinessProbe(IJobHistoryStore store) : IOrchestratorReadinessProbe
    {
        public Task ProbeAsync(CancellationToken cancellationToken) =>
            store.GetJobsPageAsync(limit: 1, offset: 0).WaitAsync(cancellationToken);
    }

    public static class OrchestratorReadiness
    {
        /// <summary>A store that has not answered in this long is not ready.</summary>
        public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(3);

        public static async Task<bool> IsReadyAsync(IOrchestratorReadinessProbe probe, CancellationToken requestAborted)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
            deadline.CancelAfter(Deadline);
            try
            {
                await probe.ProbeAsync(deadline.Token).ConfigureAwait(false);
                return true;
            }
            catch (Exception) when (!requestAborted.IsCancellationRequested)
            {
                // Any store failure (connection refused, timeout, provider error) means not ready. The
                // probe is unauthenticated, so the reason is not echoed; the store logs its own errors.
                return false;
            }
        }
    }
}
