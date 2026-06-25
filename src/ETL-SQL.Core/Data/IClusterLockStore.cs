using System;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Data;
/// <summary>
/// Database-backed leader election / distributed mutual exclusion (Practical HA P1.9). A named lock
/// with a TTL lease: exactly one owner may hold a lock at a time, and a crashed holder's lock ages out
/// so leadership can fail over. The holder of a lock is the leader for whatever cluster singleton that
/// lock guards — the motivating case being "run EF/database migrations once" when multiple Portal or
/// Orchestrator nodes boot concurrently against one shared database.
///
/// <para>This lives in the self-initializing coordination store (created via <c>CREATE TABLE IF NOT
/// EXISTS</c>, not EF migrations), so it is available <i>before</i> any node runs migrations — avoiding
/// the chicken-and-egg of guarding migrations with a lock table that a migration would create.</para>
/// </summary>
public interface IClusterLockStore
{
    /// <summary>
    /// Atomically acquires <paramref name="lockName"/> for <paramref name="owner"/> until now +
    /// <paramref name="ttl"/>. Succeeds if the lock is free, expired, or already held by this owner
    /// (idempotent re-acquire); returns false if another live owner holds it.
    /// </summary>
    Task<bool> TryAcquireLockAsync(string lockName, string owner, TimeSpan ttl);

    /// <summary>Extends the lease if still held by <paramref name="owner"/>. Returns false if lost.</summary>
    Task<bool> TryRenewLockAsync(string lockName, string owner, TimeSpan ttl);

    /// <summary>Releases the lock if held by <paramref name="owner"/> (idempotent otherwise).</summary>
    Task ReleaseLockAsync(string lockName, string owner);

    /// <summary>The current live holder of the lock, or null if free/expired (diagnostics).</summary>
    Task<string?> GetLockHolderAsync(string lockName);
}
