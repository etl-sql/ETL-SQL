using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    public class ParallelForStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ParallelForStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ParallelForStatement)statement;
            var start = Convert.ToInt32(await context.EvaluateValue(stmt.StartValue, new Row()));
            var end   = Convert.ToInt32(await context.EvaluateValue(stmt.EndValue,   new Row()));
            var step  = stmt.StepValue != null
                ? Convert.ToInt32(await context.EvaluateValue(stmt.StepValue, new Row()))
                : 1;

            var values = new List<int>();
            for (int i = start; step > 0 ? i <= end : i >= end; i += step)
                values.Add(i);

            int limit = stmt.ConcurrencyLimit > 0 ? stmt.ConcurrencyLimit : values.Count;
            int safetyLimit = Math.Min(limit, context.MaxParallelDegree);
            var semaphore = new SemaphoreSlim(safetyLimit);

            var indexedTasks = values.Select(async (val, idx) =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var fork = context.Fork();
                    if (!fork.ContainsVariable(stmt.VariableName))
                        fork.DeclareVariable(stmt.VariableName, (decimal)val);
                    else
                        fork.SetVariable(stmt.VariableName, (decimal)val);
                    await fork.EvaluateStatement(stmt.Body);
                    return (idx, fork);
                }
                finally { semaphore.Release(); }
            }).ToList();

            var results = await Task.WhenAll(indexedTasks);
            foreach (var res in results.OrderBy(r => r.idx))
                context.Merge(res.fork);
        }
    }
}
