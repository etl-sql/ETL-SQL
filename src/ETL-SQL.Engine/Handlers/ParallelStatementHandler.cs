using ETL_SQL.Data;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the PARALLEL statement, executing a block of statements concurrently using Task.WhenAll.
    /// Uses context forking to ensure thread-safety for results and metrics.
    /// </summary>
    public class ParallelStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ParallelStatement);
        /// <summary>Executes the PARALLEL block, launching all inner statements as concurrent tasks with isolated forks.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ParallelStatement)statement;
            
            // Phase 1: Determine concurrency limit (default to all if 0 or negative)
            int limit = stmt.ConcurrencyLimit > 0 ? stmt.ConcurrencyLimit : stmt.Body.Statements.Count;
            var semaphore = new System.Threading.SemaphoreSlim(limit);
            
            // Phase 2: Launch all statements with throttling
            var tasks = stmt.Body.Statements.Select(async s => {
                await semaphore.WaitAsync();
                try
                {
                    var fork = context.Fork();
                    await fork.EvaluateStatement(s);
                    return fork;
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList(); 

            // Phase 3: Wait for all to complete
            var forks = await Task.WhenAll(tasks);

            // Phase 4: Sequential merge back to parent
            foreach (var f in forks)
            {
                context.Merge(f);
            }
        }
    }
}
