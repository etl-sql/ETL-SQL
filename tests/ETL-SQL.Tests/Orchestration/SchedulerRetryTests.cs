using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Governance;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    public class SchedulerRetryTests
    {
        [Fact]
        [Trait("Category", "Integration")]
        public async Task ExecuteJobAsync_RetriesOnFailure_ExponentialBackoff()
        {
            // Arrange
            var mockExecutor = new Mock<IScriptExecutor>();
            var mockStore = new Mock<IJobHistoryStore>();
            var mockLogger = new Mock<ILogger<SchedulerService>>();
            var mockConfig = new Mock<IConfiguration>();
            var mockSessionManager = new Mock<ISessionStateManager>();

            // Setup JobThrottle with 1 slot. Point it at a private temp SQLite DB rather than the
            // shared local orchestrator DB (JobThrottle's 2-arg ctor default) so this test never
            // contends with — or hangs behind — a leftover ThrottleSlots row from another process
            // whose PID has since been reused (PurgeStaleSlots keeps PID-alive rows).
            var throttleOptions = Options.Create(new JobThrottleOptions { MaxConcurrentJobs = 1 });
            var throttleDbPath = Path.Combine(Path.GetTempPath(), $"etlsql_throttle_test_{Guid.NewGuid():N}.db");
            var throttleConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Orchestrator:DatabasePath"] = throttleDbPath })
                .Build();
            var throttle = new JobThrottle(throttleOptions, new Mock<ILogger<JobThrottle>>().Object, throttleConfig);

            mockStore.Setup(s => s.LogJobStartAsync(It.IsAny<string>())).ReturnsAsync(1L);
            mockStore.Setup(s => s.TryAcquireJobLeaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(true);
            mockStore.Setup(s => s.AcquireJobLeaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(1L);
            mockStore.Setup(s => s.TryUpdateJobLastRunFencedAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<long>())).ReturnsAsync(true);
            mockStore.Setup(s => s.TryRenewJobLeaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(true);
            mockStore.Setup(s => s.ReleaseJobLeaseAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
            mockStore.Setup(s => s.LogJobEndAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<bool?>())).Returns(Task.CompletedTask);
            mockStore.Setup(s => s.UpdateJobLastRunAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>())).Returns(Task.CompletedTask);
            mockConfig.Setup(c => c.GetSection(It.IsAny<string>())).Returns(new Mock<IConfigurationSection>().Object);

            var services = new ServiceCollection();
            services.AddSingleton(mockExecutor.Object);
            var serviceProvider = services.BuildServiceProvider();

            // Job with 2 retries (3 total attempts), 1s delay
            var job = new JobDefinition(
                "RetryTest",
                "SELECT 1;",
                1, "HOUR", null, null, null, true,
                MaxRetries: 2,
                RetryDelaySeconds: 1
            );

            // Fail first 2 times, succeed on 3rd
            int attempts = 0;
            mockExecutor.Setup(e => e.ExecuteTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<ETL_SQL.Core.Governance.ExecutionIdentity?>()))
                .ReturnsAsync((string s, string sid, CancellationToken ct, string jn, long qw, ETL_SQL.Core.Governance.ExecutionIdentity? id) =>
                {
                    attempts++;
                    if (attempts < 3)
                        return new ScriptExecutionResult(false, 0, "Fake Failure", SessionId: "sess_123");
                    return new ScriptExecutionResult(true, 10, null, SessionId: "sess_123");
                });

            var service = new SchedulerService(serviceProvider, mockStore.Object, mockLogger.Object, throttle, mockConfig.Object, mockSessionManager.Object);

            // Use reflection to test the private ExecuteJobAsync method
            var method = typeof(SchedulerService).GetMethod("ExecuteJobAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Act
            await (Task)method.Invoke(service, new object[] { job });

            // Assert
            Assert.Equal(3, attempts);

            // Verify session ID was passed back in subsequent calls (attempts 2 and 3)
            mockExecutor.Verify(e => e.ExecuteTextAsync(job.Script, null, It.IsAny<CancellationToken>(), job.Name, It.IsAny<long>(), It.IsAny<ETL_SQL.Core.Governance.ExecutionIdentity?>()), Times.Once());
            mockExecutor.Verify(e => e.ExecuteTextAsync(job.Script, "sess_123", It.IsAny<CancellationToken>(), job.Name, It.IsAny<long>(), It.IsAny<ETL_SQL.Core.Governance.ExecutionIdentity?>()), Times.Exactly(2));

            // Verify history was logged for each attempt
            mockStore.Verify(s => s.LogJobStartAsync(job.Name), Times.Exactly(3));
            mockStore.Verify(s => s.LogJobEndAsync(It.IsAny<long>(), "FAILURE", "Fake Failure", 0, It.IsAny<long>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<bool?>()), Times.Exactly(2));
            mockStore.Verify(s => s.LogJobEndAsync(It.IsAny<long>(), "SUCCESS", null, 10, It.IsAny<long>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<bool?>()), Times.Once());

            throttle.Dispose();
            try { if (File.Exists(throttleDbPath)) File.Delete(throttleDbPath); } catch { /* best-effort temp cleanup */ }
        }

        [Fact]
        public async Task ExecuteJobAsync_DispatchesCompletionNotificationAfterFinalOutcome()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"etlsql_notify_test_{Guid.NewGuid():N}.db");
            var catalogRoot = Path.Combine(Path.GetTempPath(), $"etlsql_notify_catalog_{Guid.NewGuid():N}");
            var throttleDbPath = Path.Combine(Path.GetTempPath(), $"etlsql_notify_throttle_{Guid.NewGuid():N}.db");
            var store = new SQLiteJobHistoryStore(dbPath);
            try
            {
                var job = new JobDefinition(
                    "NotifyJob",
                    "SELECT 1;",
                    1, "HOUR", null, null, null, true,
                    MaxRetries: 1,
                    RetryDelaySeconds: 1);
                await store.SaveJobAsync(job);
                await store.SaveNotificationAsync(new NotificationDefinition(
                    "NotifyOps",
                    "notify_webhook",
                    Recipient: "ops@example.com"));
                await store.AddJobNotificationAsync(job.Name, "NotifyOps", NotificationTrigger.Completion);

                var connectionCatalog = new LocalConnectionCatalogProvider(catalogRoot);
                await connectionCatalog.StoreAsync(new SharedConnectionDefinition(
                    "notify_webhook",
                    "WEBHOOK",
                    "https://hooks.example.invalid/dq",
                    new Dictionary<string, string> { ["FORMAT"] = "generic" },
                    Disabled: false));

                var mockExecutor = new Mock<IScriptExecutor>();
                string? notificationScript = null;
                mockExecutor.Setup(e => e.ExecuteTextAsync(
                        It.IsAny<string>(),
                        It.IsAny<string?>(),
                        It.IsAny<CancellationToken>(),
                        It.IsAny<string?>(),
                        It.IsAny<long>(),
                        It.IsAny<ExecutionIdentity?>()))
                    .ReturnsAsync((string script, string? sessionId, CancellationToken ct, string? jobName, long queueWaitMs, ExecutionIdentity? identity) =>
                    {
                        if (script == job.Script)
                            return new ScriptExecutionResult(true, 42, null, SessionId: "job-session");

                        notificationScript = script;
                        return new ScriptExecutionResult(true, 1, null);
                    });

                var services = new ServiceCollection();
                services.AddSingleton(mockExecutor.Object);
                services.AddSingleton<IConnectionCatalogProvider>(connectionCatalog);
                var serviceProvider = services.BuildServiceProvider();

                var throttleOptions = Options.Create(new JobThrottleOptions { MaxConcurrentJobs = 1 });
                var throttleConfig = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Orchestrator:DatabasePath"] = throttleDbPath })
                    .Build();
                using var throttle = new JobThrottle(
                    throttleOptions,
                    new Mock<ILogger<JobThrottle>>().Object,
                    throttleConfig);
                var scheduler = new SchedulerService(
                    serviceProvider,
                    store,
                    new Mock<ILogger<SchedulerService>>().Object,
                    throttle,
                    new ConfigurationBuilder().Build(),
                    new Mock<ISessionStateManager>().Object);

                var method = typeof(SchedulerService).GetMethod(
                    "ExecuteJobAsync",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                await (Task)method!.Invoke(scheduler, [job])!;

                Assert.NotNull(notificationScript);
                Assert.Contains("CREATE CONNECTION __job_notification_sink AS WEBHOOK('SHARED:notify_webhook')", notificationScript);
                Assert.Contains("Job succeeded: NotifyJob", notificationScript);
                Assert.Contains("ops@example.com", notificationScript);
                mockExecutor.Verify(e => e.ExecuteTextAsync(
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string?>(),
                    It.IsAny<long>(),
                    It.IsAny<ExecutionIdentity?>()), Times.Exactly(2));
            }
            finally
            {
                try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
                try { if (File.Exists(throttleDbPath)) File.Delete(throttleDbPath); } catch { }
                try { if (Directory.Exists(catalogRoot)) Directory.Delete(catalogRoot, recursive: true); } catch { }
            }
        }
    }
}
