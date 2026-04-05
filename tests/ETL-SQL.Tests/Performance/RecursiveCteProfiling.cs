using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ETL_SQL.Core;
using ETL_SQL.Engine;

namespace ETL_SQL.Tests.Performance
{
    public class RecursiveCteProfiling
    {
        private readonly ITestOutputHelper _output;

        public RecursiveCteProfiling(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(5000)]
        public async Task Profile_RecursiveCte_Depth(int depth)
        {
            // Arrange
            var sql = $@"
                WITH RECURSIVE Counter AS (
                    SELECT 1 AS n
                    UNION ALL
                    SELECT n + 1 FROM Counter WHERE n < {depth}
                )
                SELECT n FROM Counter;
            ";

            var eval = (Evaluator)Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService(ETL_SQL.Program.ServiceProvider, typeof(Evaluator));
            var sw = Stopwatch.StartNew();

            // Act
            await TestHelpers.Execute(eval, sql);
            sw.Stop();

            // Assert
            _output.WriteLine($"Recursion Depth: {depth}");
            _output.WriteLine($"Execution Time: {sw.ElapsedMilliseconds}ms");
            
            // Basic sanity check: should at least finish in a reasonable time
            Assert.True(sw.ElapsedMilliseconds < 10000, $"Recursion depth {depth} took too long: {sw.ElapsedMilliseconds}ms");
        }
    }
}
