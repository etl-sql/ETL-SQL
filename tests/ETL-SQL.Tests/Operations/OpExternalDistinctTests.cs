using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Engines;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Operations.Operations;

public sealed class ExternalDistinctTests
{
    [Fact]
    public async Task SpillSampleCanIncreaseFanOutAboveConfiguredBaseline()
    {
        var services = DependencyInjectionSetup.BuildServiceProvider();
        var context = services.GetRequiredService<Evaluator>();
        context.ExternalHashPartitions = 2;
        context.JoinSpillThreshold = 4096;
        context.OperatorMemoryGrantMB = 1;
        var saved = MemoryGrantArbiter.Shared.TotalBudgetBytes;
        MemoryGrantArbiter.Shared.TotalBudgetBytes = 0;
        try
        {
            var payload = new string('x', 1024);
            var rows = Enumerable.Range(0, 4096)
                .Select(id => new Row { ["id"] = id, ["payload"] = payload + id })
                .ToAsyncEnumerable();
            var engine = new ExternalDistinctEngine(context);

            var results = await engine.ApplyAsync(rows).ToListAsync();

            Assert.Equal(4096, results.Count);
            Assert.True(engine.PartitionCount > 2);
        }
        finally
        {
            MemoryGrantArbiter.Shared.TotalBudgetBytes = saved;
        }
    }

    [Fact]
    public async Task AdaptiveFanOutPreservesDuplicateEquality()
    {
        var services = DependencyInjectionSetup.BuildServiceProvider();
        var context = services.GetRequiredService<Evaluator>();
        context.ExternalHashPartitions = 2;
        context.JoinSpillThreshold = 4096;
        context.OperatorMemoryGrantMB = 1;
        var saved = MemoryGrantArbiter.Shared.TotalBudgetBytes;
        MemoryGrantArbiter.Shared.TotalBudgetBytes = 0;
        try
        {
            var payload = new string('x', 1024);
            var rows = Enumerable.Range(0, 4096)
                .Select(id => new Row { ["id"] = id % 1024, ["payload"] = payload })
                .ToAsyncEnumerable();
            var engine = new ExternalDistinctEngine(context);

            var results = await engine.ApplyAsync(rows).ToListAsync();

            Assert.Equal(1024, results.Count);
            Assert.Equal(1024, results.Select(row => row["id"]).Distinct().Count());
            Assert.True(engine.PartitionCount > 2);
            Assert.Equal(4096, engine.ColumnarBuildRows);
        }
        finally
        {
            MemoryGrantArbiter.Shared.TotalBudgetBytes = saved;
        }
    }

    [Fact]
    public async Task GovernedDedupBuildConsumesNativeSpillBatches()
    {
        var services = DependencyInjectionSetup.BuildServiceProvider();
        var context = services.GetRequiredService<Evaluator>();
        context.ExternalHashPartitions = 2;
        context.JoinSpillThreshold = 4;
        var saved = MemoryGrantArbiter.Shared.TotalBudgetBytes;
        MemoryGrantArbiter.Shared.TotalBudgetBytes = 64L * 1024 * 1024;
        try
        {
            var rows = Enumerable.Range(0, 20)
                .Select(id => new Row { ["id"] = id % 10, ["value"] = "v" + (id % 10) })
                .ToAsyncEnumerable();
            var engine = new ExternalDistinctEngine(context);

            var results = await engine.ApplyAsync(rows).ToListAsync();

            Assert.Equal(10, results.Count);
            Assert.Equal(20, engine.ColumnarBuildRows);
        }
        finally
        {
            MemoryGrantArbiter.Shared.TotalBudgetBytes = saved;
        }
    }
}
