using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Execution;

namespace ETL_SQL.Engine.Services;
/// <summary>
/// Tracks evaluator-owned memory and clears reclaimable evaluator caches under memory pressure.
/// </summary>
internal sealed class EvaluatorSpillCoordinator(IExecutionContext context, ILogger logger) : ISpillable
{
    private readonly IExecutionContext _context = context;
    private readonly ILogger _logger = logger;

    public long MemoryUsageBytes
    {
        get
        {
            long varBytes = _context.Variables.Count * 256;
            long subqueryBytes = 0;
            foreach (var result in _context.SubqueryCache.Values)
            {
                subqueryBytes += result.MemoryUsageBytes;
            }

            return varBytes + subqueryBytes;
        }
    }

    public async Task<bool> SpillAsync()
    {
        if (_context.SubqueryCache.Count > 0)
        {
            await _context.SubqueryCache.ClearAsync();
            _logger.Warning("Evaluator spilled: Subquery cache cleared to reclaim memory.");
            return true;
        }

        return false;
    }

    public string SpillToken => $"Session_{_context.SessionId}";
}
