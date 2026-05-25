using Xunit;
using Moq;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ETL_SQL.Tests.Orchestration
{
    public class ScriptHashPolicyTests
    {
        private const string Script = "SELECT 1;";

        private static string ComputeHash(string text) =>
            "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

        private static (SchedulerService service, Mock<IJobHistoryStore> store, Mock<IScriptExecutor> executor)
            Build()
        {
            var mockExecutor = new Mock<IScriptExecutor>();
            var mockStore    = new Mock<IJobHistoryStore>();
            var mockConfig   = new Mock<IConfiguration>();
            var mockSessions = new Mock<ISessionStateManager>();

            var throttleOptions = Options.Create(new JobThrottleOptions { MaxConcurrentJobs = 1 });
            var throttle = new JobThrottle(throttleOptions, new Mock<ILogger<JobThrottle>>().Object);

            mockStore.Setup(s => s.LogJobStartAsync(It.IsAny<string>())).ReturnsAsync(1L);
            mockStore.Setup(s => s.LogJobEndAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<long>(), It.IsAny<long>(), It.IsAny<double>(),
                It.IsAny<string?>(), It.IsAny<bool?>()))
                .Returns(Task.CompletedTask);
            mockStore.Setup(s => s.UpdateJobLastRunAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>()))
                .Returns(Task.CompletedTask);
            mockConfig.Setup(c => c.GetSection(It.IsAny<string>()))
                .Returns(new Mock<IConfigurationSection>().Object);

            mockExecutor.Setup(e => e.ExecuteTextAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
                .ReturnsAsync(new ScriptExecutionResult(true, 10, null, SessionId: "sess_test"));

            var services = new ServiceCollection();
            services.AddSingleton(mockExecutor.Object);
            var sp = services.BuildServiceProvider();

            var service = new SchedulerService(sp, mockStore.Object,
                new Mock<ILogger<SchedulerService>>().Object, throttle, mockConfig.Object, mockSessions.Object);

            return (service, mockStore, mockExecutor);
        }

        private static Task InvokeExecuteJobAsync(SchedulerService service, JobDefinition job)
        {
            var method = typeof(SchedulerService).GetMethod("ExecuteJobAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            return (Task)method.Invoke(service, new object[] { job })!;
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task HashMatch_JobRuns_HistoryRecordsHashMatchedTrue()
        {
            var hash = ComputeHash(Script);
            var job  = new JobDefinition("HashMatchJob", Script, 1, "HOUR", null, null, null,
                ScriptHash: hash, HashPolicy: "Warn");
            var (service, store, executor) = Build();

            await InvokeExecuteJobAsync(service, job);

            executor.Verify(e => e.ExecuteTextAsync(Script, It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()),
                Times.Once());
            store.Verify(s => s.LogJobEndAsync(
                1L, "SUCCESS", It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<double>(),
                hash, true),
                Times.Once());
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task HashMismatch_WarnPolicy_JobRunsAndRecordsHashMatchedFalse()
        {
            const string storedHash = "sha256:0000000000000000000000000000000000000000000000000000000000000000";
            var actualHash = ComputeHash(Script);
            var job = new JobDefinition("WarnJob", Script, 1, "HOUR", null, null, null,
                ScriptHash: storedHash, HashPolicy: "Warn");
            var (service, store, executor) = Build();

            await InvokeExecuteJobAsync(service, job);

            executor.Verify(e => e.ExecuteTextAsync(Script, It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()),
                Times.Once());
            store.Verify(s => s.LogJobEndAsync(
                1L, "SUCCESS", It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<double>(),
                actualHash, false),
                Times.Once());
        }

        [Fact]
        public async Task HashMismatch_BlockPolicy_ScriptNeverExecutesAndRecordsBlocked()
        {
            const string storedHash = "sha256:0000000000000000000000000000000000000000000000000000000000000000";
            var actualHash = ComputeHash(Script);
            var job = new JobDefinition("BlockJob", Script, 1, "HOUR", null, null, null,
                ScriptHash: storedHash, HashPolicy: "Block");
            var (service, store, executor) = Build();

            await InvokeExecuteJobAsync(service, job);

            executor.Verify(e => e.ExecuteTextAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()),
                Times.Never());
            store.Verify(s => s.LogJobEndAsync(
                1L, "BLOCKED", It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<double>(),
                actualHash, false),
                Times.Once());
        }
    }
}
