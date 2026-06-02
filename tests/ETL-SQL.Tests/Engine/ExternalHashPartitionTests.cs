using Xunit;
using System;
using System.Threading.Tasks;
using ETL_SQL.Engine;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Tests.Engine
{
    public class ExternalHashPartitionTests
    {
        [Fact]
        public async Task TestSetHashPartitions_Zero_ShouldFail()
        {
            var provider = DependencyInjectionSetup.BuildServiceProvider();
            var eval = provider.GetRequiredService<Evaluator>();

            // Should throw error when setting to 0
            var ex = await Assert.ThrowsAsync<ExecutionException>(async () => 
                await TestHelpers.Execute(eval, "SET EXTERNAL_HASH_PARTITIONS = 0;")
            );
            Assert.Contains("must be at least 1", ex.Message);
        }

        [Fact]
        public async Task TestSetHashPartitions_Negative_ShouldFail()
        {
            var provider = DependencyInjectionSetup.BuildServiceProvider();
            var eval = provider.GetRequiredService<Evaluator>();

            // Should throw error when setting to negative
            var ex = await Assert.ThrowsAsync<ExecutionException>(async () => 
                await TestHelpers.Execute(eval, "SET EXTERNAL_HASH_PARTITIONS = -5;")
            );
            Assert.Contains("must be at least 1", ex.Message);
        }
    }
}
