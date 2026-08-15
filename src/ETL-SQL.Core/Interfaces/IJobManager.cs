using System.Threading.Tasks;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Core;

public interface IJobManager
{
    bool KillJob(long historyId);

    /// <summary>
    /// Enqueues an immediate out-of-schedule execution of one job, addressed by identity.
    ///
    /// <para>The caller resolves the name and authorizes the job before calling: a name identifies a
    /// job only within a tenant, so re-resolving it here could start a different tenant's job of the
    /// same name — and would do so under an authorization decision made about the other one.</para>
    /// </summary>
    Task<bool> TriggerJobAsync(JobId jobId);
}
