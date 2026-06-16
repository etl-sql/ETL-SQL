using System.Threading.Tasks;

namespace ETL_SQL.Core.Data
{
    /// <summary>
    /// Database-backed monotonic write epochs (Practical HA P1.8). SMB/UNC shared storage has no native
    /// write-fencing, so a stale node recovering from a GC pause could overwrite a newer node's file. This
    /// makes the shared database the fencing authority: each protected resource records the highest fence
    /// token that has written it, and a writer must atomically claim an epoch &ge; the current one before
    /// it is allowed to write. A stale writer (older token) is rejected, so it can never clobber a newer one.
    /// </summary>
    public interface IWriteEpochStore
    {
        /// <summary>
        /// Atomically claims the write epoch for <paramref name="scope"/>/<paramref name="key"/> at
        /// <paramref name="token"/>. Returns true if the token is &ge; the current epoch (the resource is
        /// now stamped with it and the write may proceed); false if a newer token already wrote, fencing
        /// this writer out. Re-claiming with the same token is idempotent (returns true).
        /// </summary>
        Task<bool> TryClaimWriteEpochAsync(string scope, string key, long token);

        /// <summary>The current epoch for a resource, or 0 if it has never been written.</summary>
        Task<long> GetWriteEpochAsync(string scope, string key);
    }
}
