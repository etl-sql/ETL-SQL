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

        [Fact]
        public async Task CliOrchestrator_DoctorParsesJsonStrictAndFullProfile()
        {
            CliContext? capturedContext = null;
            var root = CliOrchestrator.BuildRootCommand(ctx =>
            {
                capturedContext = ctx;
                return Task.FromResult(7);
            });

            var exitCode = await root.InvokeAsync(new[] { "doctor", "--json", "--strict", "--profile", "full" });

            Assert.Equal(7, exitCode);
            Assert.NotNull(capturedContext);
            Assert.Equal("doctor", capturedContext!.Command);
            Assert.True(capturedContext.IsJsonMode);
            Assert.True(capturedContext.DoctorStrict);
            Assert.Equal("full", capturedContext.DoctorProfile);
        }

        [Fact]
        public async Task CliOrchestrator_DoctorDefaultsToQuickProfile()
        {
            CliContext? capturedContext = null;
            var root = CliOrchestrator.BuildRootCommand(ctx =>
            {
                capturedContext = ctx;
                return Task.FromResult(0);
            });

            var exitCode = await root.InvokeAsync(new[] { "doctor" });

            Assert.Equal(0, exitCode);
            Assert.NotNull(capturedContext);
            Assert.False(capturedContext!.IsJsonMode);
            Assert.False(capturedContext.DoctorStrict);
            Assert.Equal("quick", capturedContext.DoctorProfile);
        }
    }
}
