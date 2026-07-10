using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;

public class ParallelForStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(ParallelForStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ParallelForStatement)statement;
        var start = Convert.ToInt32(await context.EvaluateValue(stmt.StartValue, Row.Empty));
        var end = Convert.ToInt32(await context.EvaluateValue(stmt.EndValue, Row.Empty));
        var step = stmt.StepValue != null
            ? Convert.ToInt32(await context.EvaluateValue(stmt.StepValue, Row.Empty))
            : 1;

        var values = new List<int>();
        for (int i = start; step > 0 ? i <= end : i >= end; i += step)
            values.Add(i);

        int limit = stmt.ConcurrencyLimit > 0 ? stmt.ConcurrencyLimit : values.Count;
        int adaptiveLimit = Math.Max(1, context.EffectiveMaxParallelDegree);
        int safetyLimit = Math.Min(limit, adaptiveLimit);

        // Use a concurrent bag to collect results in the order tasks finish,
        // then sort by index before merging so the merge order is deterministic.
        var results = new System.Collections.Concurrent.ConcurrentBag<(int idx, IExecutionContext fork)>();

        // Parallel.ForEachAsync throttles at the dispatch level — no Task objects are
        // created for iterations that haven't started yet, avoiding the heap spike that
        // occurred when .Select(...).ToList() eagerly scheduled all tasks at once.
        await Parallel.ForEachAsync(
            values.Select((val, idx) => (val, idx)),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = safetyLimit,
                CancellationToken = context.CancellationToken
            },
            async (item, ct) =>
            {
                var fork = context.Fork();
                if (!fork.VarContext.ContainsVariable(stmt.VariableName))
                    fork.VarContext.DeclareVariable(stmt.VariableName, (decimal)item.val);
                else
                    fork.VarContext.SetVariable(stmt.VariableName, (decimal)item.val);
                await fork.EvaluateStatement(stmt.Body, ct);
                results.Add((item.idx, fork));
            });

        foreach (var res in results.OrderBy(r => r.idx))
            context.Merge(res.fork);
    }
}
