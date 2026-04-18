using Xunit;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using ETL_SQL.App;

namespace ETL_SQL.Tests.Integration
{
    public class CliOrchestratorTests
    {
        [Fact]
        public async Task CliOrchestrator_ParsesPerfFlag()
        {
            CliContext? capturedContext = null;
            var root = CliOrchestrator.BuildRootCommand(async (ctx) => 
            {
                capturedContext = ctx;
                return 0;
            });

            await root.InvokeAsync(new[] { "run", "script.sql", "--perf" });
            
            Assert.NotNull(capturedContext);
            Assert.True(capturedContext!.IsPerfMode);
        }

        [Fact]
        public async Task CliOrchestrator_ParsesTestFlag()
        {
            CliContext? capturedContext = null;
            var root = CliOrchestrator.BuildRootCommand(async (ctx) => 
            {
                capturedContext = ctx;
                return 0;
            });

            await root.InvokeAsync(new[] { "test", "unit" });
            
            Assert.NotNull(capturedContext);
            Assert.True(capturedContext!.IsTestMode);
            Assert.Equal("unit", capturedContext.TestVal);
        }

        [Fact]
        public async Task CliOrchestrator_ParsesBatchSize()
        {
            CliContext? capturedContext = null;
            var root = CliOrchestrator.BuildRootCommand(async (ctx) => 
            {
                capturedContext = ctx;
                return 0;
            });

            await root.InvokeAsync(new[] { "run", "script.sql", "-b", "5000" });
            
            Assert.NotNull(capturedContext);
            Assert.Equal(5000, capturedContext!.BatchSize);
        }
    }
}
