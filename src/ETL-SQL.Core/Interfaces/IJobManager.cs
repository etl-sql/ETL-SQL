using System.Threading.Tasks;

namespace ETL_SQL.Core;

public interface IJobManager
{
    bool KillJob(long historyId);
    Task<bool> TriggerJobAsync(string jobName);
}
