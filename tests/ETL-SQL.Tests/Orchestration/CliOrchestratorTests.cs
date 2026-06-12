using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using Spectre.Console;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
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

            await root.Parse(new[] { "run", "script.sql", "--perf" }, null).InvokeAsync(new InvocationConfiguration(), default);

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

            await root.Parse(new[] { "test", "unit" }, null).InvokeAsync(new InvocationConfiguration(), default);

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

            await root.Parse(new[] { "run", "script.sql", "-b", "5000" }, null).InvokeAsync(new InvocationConfiguration(), default);

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

            var exitCode = await root.Parse(new[] { "doctor", "--json", "--strict", "--profile", "full" }, null).InvokeAsync(new InvocationConfiguration(), default);

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

            var exitCode = await root.Parse(new[] { "doctor" }, null).InvokeAsync(new InvocationConfiguration(), default);

            Assert.Equal(0, exitCode);
            Assert.NotNull(capturedContext);
            Assert.False(capturedContext!.IsJsonMode);
            Assert.False(capturedContext.DoctorStrict);
            Assert.Equal("quick", capturedContext.DoctorProfile);
        }

        [Theory]
        [InlineData(false, false, false, 0)]
        [InlineData(false, false, true, 0)]
        [InlineData(false, true, false, 0)]
        [InlineData(true, false, false, 0)]
        [InlineData(true, false, true, 1)]
        [InlineData(true, true, false, 1)]
        public void DoctorExitCode_StrictOnlyFailsOnWarningsOrFailures(
            bool strict,
            bool hasFailures,
            bool hasWarnings,
            int expectedExitCode)
        {
            Assert.Equal(expectedExitCode, EngineRunner.DoctorExitCode(strict, hasFailures, hasWarnings));
        }
    }
}
