using System;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.Orchestrator.Scheduling
{
    /// <summary>
    /// Runs a cluster-singleton critical section under a database-backed lock (Practical HA P1.9). Every
    /// node calls <see cref="RunExclusiveAsync"/>; the first to claim the lock runs the action while the
    /// others block-wait, then take their turn. The action must therefore be idempotent (the motivating
    /// case — applying EF migrations — is: the second node through finds nothing pending and no-ops),
    /// which serializes it cluster-wide and prevents concurrent-startup migration collisions.
    ///
    /// <para>While the action runs, the lock is renewed on a background heartbeat so a long operation
    /// cannot let the lease expire and a second node start in parallel; the lock is always released in a
    /// finally block.</para>
    /// </summary>
    public static class ClusterLock
    {
        /// <summary>
        /// Acquires <paramref name="lockName"/> (blocking up to <paramref name="maxWait"/>), runs
        /// <paramref name="criticalSection"/> exactly once while holding it, then releases it. Throws
        /// <see cref="TimeoutException"/> if the lock cannot be acquired within <paramref name="maxWait"/>
        /// (fail fast rather than run the guarded action unprotected).
        /// </summary>
        public static async Task RunExclusiveAsync(
            IClusterLockStore store,
            string lockName,
            string owner,
            Func<Task> criticalSection,
            ILogger logger,
            TimeSpan? ttl = null,
            TimeSpan? maxWait = null,
            CancellationToken ct = default)
        {
            var lease = ttl ?? TimeSpan.FromMinutes(2);
            var deadline = DateTime.UtcNow + (maxWait ?? TimeSpan.FromMinutes(2));
            var pollInterval = TimeSpan.FromMilliseconds(250);

            while (!await store.TryAcquireLockAsync(lockName, owner, lease))
            {
                if (DateTime.UtcNow >= deadline)
                    throw new TimeoutException(
                        $"Could not acquire cluster lock '{lockName}' within the wait window (held by " +
                        $"'{await store.GetLockHolderAsync(lockName) ?? "unknown"}').");
                logger.LogDebug("Cluster lock '{Lock}' is held by another node; waiting…", lockName);
                await Task.Delay(pollInterval, ct);
            }

            logger.LogInformation("Acquired cluster lock '{Lock}' as '{Owner}'.", lockName, owner);
            using var renewCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var renewal = RenewLoopAsync(store, lockName, owner, lease, logger, renewCts.Token);
            try
            {
                await criticalSection();
            }
            finally
            {
                renewCts.Cancel();
                try { await renewal; } catch { /* renewal cancellation is expected */ }
                await ReleaseWithRetryAsync(store, lockName, owner, logger);
            }
        }

        private static async Task ReleaseWithRetryAsync(
            IClusterLockStore store,
            string lockName,
            string owner,
            ILogger logger)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            Exception? lastError = null;
            do
            {
                try
                {
                    await store.ReleaseLockAsync(lockName, owner);
                    if (!string.Equals(await store.GetLockHolderAsync(lockName), owner,
                            StringComparison.Ordinal))
                    {
                        logger.LogInformation("Released cluster lock '{Lock}'.", lockName);
                        return;
                    }
                    lastError = null;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }
            while (DateTime.UtcNow < deadline);

            logger.LogWarning(lastError,
                "Failed to release cluster lock '{Lock}' after retries; it will age out.", lockName);
        }

        private static async Task RenewLoopAsync(
            IClusterLockStore store, string lockName, string owner, TimeSpan lease, ILogger logger, CancellationToken ct)
        {
            var interval = TimeSpan.FromSeconds(Math.Max(2, lease.TotalSeconds / 3));
            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(interval, ct); }
                catch (OperationCanceledException) { return; }

                try
                {
                    if (!await store.TryRenewLockAsync(lockName, owner, lease))
                    {
                        // We unexpectedly lost the lock mid-critical-section (e.g. a long stall let it
                        // expire and another node took it). Surface it loudly; the action is idempotent.
                        logger.LogWarning("Lost cluster lock '{Lock}' during the critical section.", lockName);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Transient failure renewing cluster lock '{Lock}'.", lockName);
                }
            }
        }
    }
}
