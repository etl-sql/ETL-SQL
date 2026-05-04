using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Microsoft.Extensions.Configuration;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Engine.Services;
using ETL_SQL.Services;
using ETL_SQL.Core.Execution;

namespace ETL_SQL.Tests.Integration.Misc
{
    public class SchedulerServiceTests
    {
        /// <summary>
        /// Builds a SchedulerService with mocked dependencies.
        /// The service provider is wired so CreateScope() returns a scope containing the given executor.
        /// </summary>
        private static (SchedulerService service, Mock<IJobHistoryStore> store, Mock<IScriptExecutor> executor)
            Build(IEnumerable<JobDefinition> jobs, ScriptExecutionResult result)
        {
            var mockStore = new Mock<IJobHistoryStore>();
            mockStore.Setup(s => s.InitializeAsync()).Returns(Task.CompletedTask);
            mockStore.Setup(s => s.GetActiveJobsAsync()).ReturnsAsync(jobs);
            mockStore.Setup(s => s.LogJobStartAsync(It.IsAny<string>())).ReturnsAsync(1L);
            mockStore.Setup(s => s.LogJobEndAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<bool?>())).Returns(Task.CompletedTask);
            mockStore.Setup(s => s.UpdateJobLastRunAsync(It.IsAny<string>(), It.IsAny<DateTime>(),
                It.IsAny<DateTime?>())).Returns(Task.CompletedTask);

            var mockExecutor = new Mock<IScriptExecutor>();
            mockExecutor.Setup(e => e.ExecuteTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(result);

            return (BuildService(mockStore, mockExecutor), mockStore, mockExecutor);
        }

        private static SchedulerService BuildService(Mock<IJobHistoryStore> mockStore, Mock<IScriptExecutor> mockExecutor)
        {
            // IServiceProvider.CreateScope() is an extension that calls
            // GetRequiredService<IServiceScopeFactory>().CreateScope()
            var mockScopeServiceProvider = new Mock<IServiceProvider>();
            mockScopeServiceProvider.Setup(p => p.GetService(typeof(IScriptExecutor)))
                                    .Returns(mockExecutor.Object);

            var mockScope = new Mock<IServiceScope>();
            mockScope.Setup(s => s.ServiceProvider).Returns(mockScopeServiceProvider.Object);

            var mockScopeFactory = new Mock<IServiceScopeFactory>();
            mockScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

            var mockServiceProvider = new Mock<IServiceProvider>();
            mockServiceProvider.Setup(p => p.GetService(typeof(IServiceScopeFactory)))
                               .Returns(mockScopeFactory.Object);

            var throttleOptions = Options.Create(new JobThrottleOptions { MaxConcurrentJobs = 4 });
            var throttle = new JobThrottle(throttleOptions, new Mock<ILogger<JobThrottle>>().Object);

            var mockConfig = new Mock<IConfiguration>();
            // Setup default values for intervals if needed by tests
            mockConfig.Setup(c => c.GetSection(It.IsAny<string>())).Returns(new Mock<IConfigurationSection>().Object);

            var mockSessLogger = new Mock<ETL_SQL.Common.ILogger>();
            var sessionManager = new Mock<ISessionStateManager>();

            return new SchedulerService(
                mockServiceProvider.Object,
                mockStore.Object,
                new Mock<ILogger<SchedulerService>>().Object,
                throttle,
                mockConfig.Object,
                sessionManager.Object);
        }

        [Fact]
        public async Task Job_WithNullNextRun_IsExecuted()
        {
            var jobs = new[] { new JobDefinition("TestJob", "SELECT 1;", 1, "HOUR", null, null, null) };
            var (service, _, executor) = Build(jobs, new ScriptExecutionResult(true, 5));

            service.Start();
            await Task.Delay(500);
            service.Stop();

            executor.Verify(e => e.ExecuteTextAsync("SELECT 1;", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce());
        }

        [Fact]
        public async Task Job_WithPastNextRun_IsExecuted()
        {
            var pastDate = DateTime.Now.AddHours(-1);
            var jobs = new[] { new JobDefinition("OldJob", "PRINT 'hi';", 1, "DAY", null, null, pastDate) };
            var (service, _, executor) = Build(jobs, new ScriptExecutionResult(true, 0));

            service.Start();
            await Task.Delay(500);
            service.Stop();

            executor.Verify(e => e.ExecuteTextAsync("PRINT 'hi';", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce());
        }

        [Fact]
        public async Task Job_WithFutureNextRun_IsNotExecuted()
        {
            var futureDate = DateTime.Now.AddHours(1);
            var jobs = new[] { new JobDefinition("FutureJob", "SELECT 2;", 1, "HOUR", null, null, futureDate) };
            var (service, _, executor) = Build(jobs, new ScriptExecutionResult(true, 0));

            service.Start();
            await Task.Delay(300);
            service.Stop();

            executor.Verify(e => e.ExecuteTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
        }

        [Fact]
        public async Task SuccessfulJob_LogsSuccessToHistory()
        {
            var jobs = new[] { new JobDefinition("LogJob", "SELECT 1;", 1, "HOUR", null, null, null) };
            var (service, store, _) = Build(jobs, new ScriptExecutionResult(true, 42));

            service.Start();
            await Task.Delay(500);
            service.Stop();

            store.Verify(s => s.LogJobStartAsync("LogJob"), Times.AtLeastOnce());
            store.Verify(s => s.LogJobEndAsync(1L, "SUCCESS", null, 42, It.IsAny<long>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<bool?>()), Times.AtLeastOnce());
        }

        [Fact]
        public async Task FailedJob_LogsFailureToHistory()
        {
            var jobs = new[] { new JobDefinition("FailJob", "BAD SQL;", 1, "HOUR", null, null, null) };
            var (service, store, _) = Build(jobs, new ScriptExecutionResult(false, 0, "Parse error"));

            service.Start();
            await Task.Delay(500);
            service.Stop();

            store.Verify(s => s.LogJobEndAsync(1L, "FAILURE", "Parse error", It.IsAny<long>(), It.IsAny<long>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<bool?>()), Times.AtLeastOnce());
        }

        [Fact]
        public async Task ExceptionDuringJob_LogsFailureToHistory()
        {
            var jobs = new[] { new JobDefinition("ExJob", "SELECT 1;", 1, "HOUR", null, null, null) };

            var mockStore = new Mock<IJobHistoryStore>();
            mockStore.Setup(s => s.InitializeAsync()).Returns(Task.CompletedTask);
            mockStore.Setup(s => s.GetActiveJobsAsync()).ReturnsAsync(jobs);
            mockStore.Setup(s => s.LogJobStartAsync(It.IsAny<string>())).ReturnsAsync(1L);
            mockStore.Setup(s => s.LogJobEndAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<bool?>())).Returns(Task.CompletedTask);
            mockStore.Setup(s => s.UpdateJobLastRunAsync(It.IsAny<string>(), It.IsAny<DateTime>(),
                It.IsAny<DateTime?>())).Returns(Task.CompletedTask);

            var mockExecutor = new Mock<IScriptExecutor>();
            mockExecutor.Setup(e => e.ExecuteTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ThrowsAsync(new InvalidOperationException("DB connection lost"));

            var service = BuildService(mockStore, mockExecutor);

            service.Start();
            await Task.Delay(500);
            service.Stop();

            mockStore.Verify(s => s.LogJobEndAsync(1L, "FAILURE", "DB connection lost", It.IsAny<long>(), It.IsAny<long>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<bool?>()),
                Times.AtLeastOnce());
        }

        [Fact]
        public async Task JobHistory_IsWrittenAfterExecution()
        {
            var jobs = new[] { new JobDefinition("HistJob", "SELECT 1;", 1, "HOUR", null, null, null) };
            var (service, store, _) = Build(jobs, new ScriptExecutionResult(true, 10));

            service.Start();
            await Task.Delay(500);
            service.Stop();

            // Both start and end should be logged
            store.Verify(s => s.LogJobStartAsync("HistJob"), Times.AtLeastOnce());
            store.Verify(s => s.LogJobEndAsync(It.IsAny<long>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<bool?>()), Times.AtLeastOnce());
        }

        [Fact]
        public async Task NextRunTimestamp_IsUpdatedAfterExecution()
        {
            var jobs = new[] { new JobDefinition("UpdateJob", "SELECT 1;", 1, "HOUR", null, null, null) };
            var (service, store, _) = Build(jobs, new ScriptExecutionResult(true, 0));

            service.Start();
            await Task.Delay(500);
            service.Stop();

            store.Verify(s => s.UpdateJobLastRunAsync("UpdateJob", It.IsAny<DateTime>(), It.IsAny<DateTime?>()),
                Times.AtLeastOnce());
        }
    }
}
