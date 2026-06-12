using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Data;

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

            // Phase 1: Determine concurrency limit (default to all if 0 or negative, capped by global MaxParallelDegree)
            int limit = stmt.ConcurrencyLimit > 0 ? stmt.ConcurrencyLimit : stmt.Body.Statements.Count;
            int safetyLimit = Math.Min(limit, context.MaxParallelDegree);

            var semaphore = new System.Threading.SemaphoreSlim(safetyLimit);

            // Mark the current tree node so the renderer shows it as a collapsible parallel block.
            if (context.CurrentNodeId.HasValue)
            {
                var parallelNode = context.Telemetry.ExecutionTree.GetNode(context.CurrentNodeId.Value);
                if (parallelNode != null) parallelNode.IsParallelBlock = true;
            }

            // Phase 2: Launch all statements with throttling and index-based tracking
            var indexedTasks = stmt.Body.Statements.Select(async (s, index) =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var fork = context.Fork();
                    await fork.EvaluateStatement(s);
                    return (index, fork);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            // Phase 3: Wait for all to complete
            var results = await Task.WhenAll(indexedTasks);

            // Phase 4: Strict sequential merge back to parent (sorted by submission index)
            foreach (var res in results.OrderBy(r => r.index))
            {
                context.Merge(res.fork);
            }
        }
    }
}

