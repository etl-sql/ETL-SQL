using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Data;

/// <summary>
/// Cancel requests for runs, kept in the shared store so any Orchestrator node can accept one. A
/// node can only signal the runs it is executing; when a request lands on another node behind the
/// load balancer, that node records it here and the node running the job picks it up.
/// </summary>
public interface IJobRunCancelStore
{
    /// <summary>Records a cancel request for run <paramref name="historyId"/>. Repeating it is harmless.</summary>
    Task RequestRunCancelAsync(long historyId, string requestedBy, CancellationToken cancellationToken = default);

    /// <summary>The runs among <paramref name="historyIds"/> that someone has asked to cancel.</summary>
    Task<IReadOnlyList<long>> GetRequestedRunCancelsAsync(
        IReadOnlyCollection<long> historyIds, CancellationToken cancellationToken = default);

    /// <summary>Removes the request once its run has ended.</summary>
    Task ClearRunCancelAsync(long historyId, CancellationToken cancellationToken = default);
}
