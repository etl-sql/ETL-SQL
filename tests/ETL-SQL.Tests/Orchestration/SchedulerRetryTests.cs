using Xunit;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Orchestrator.Execution;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ETL_SQL.Core.Execution;

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
            
            // Setup JobThrottle with 1 slot
            var throttleOptions = Options.Create(new JobThrottleOptions { MaxConcurrentJobs = 1 });
            var throttle = new JobThrottle(throttleOptions, new Mock<ILogger<JobThrottle>>().Object);

            mockStore.Setup(s => s.LogJobStartAsync(It.IsAny<string>())).ReturnsAsync(1L);
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
            mockExecutor.Setup(e => e.ExecuteTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
                .ReturnsAsync((string s, string sid, CancellationToken ct, string jn) => {
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
            mockExecutor.Verify(e => e.ExecuteTextAsync(job.Script, null, It.IsAny<CancellationToken>(), job.Name), Times.Once());
            mockExecutor.Verify(e => e.ExecuteTextAsync(job.Script, "sess_123", It.IsAny<CancellationToken>(), job.Name), Times.Exactly(2));
            
            // Verify history was logged for each attempt
            mockStore.Verify(s => s.LogJobStartAsync(job.Name), Times.Exactly(3));
            mockStore.Verify(s => s.LogJobEndAsync(It.IsAny<long>(), "FAILURE", "Fake Failure", 0, It.IsAny<long>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<bool?>()), Times.Exactly(2));
            mockStore.Verify(s => s.LogJobEndAsync(It.IsAny<long>(), "SUCCESS", null, 10, It.IsAny<long>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<bool?>()), Times.Once());
        }
    }
}
