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
using Microsoft.Extensions.Logging.Abstractions;
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

            mockStore.Setup(s => s.LogJobStartAsync(It.IsAny<JobId>())).ReturnsAsync(1L);
            mockStore.Setup(s => s.TryAcquireJobLeaseAsync(It.IsAny<JobId>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(true);
            mockStore.Setup(s => s.AcquireJobLeaseAsync(It.IsAny<JobId>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(1L);
            mockStore.Setup(s => s.TryUpdateJobLastRunFencedAsync(It.IsAny<JobId>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<long>())).ReturnsAsync(true);
            mockStore.Setup(s => s.TryRenewJobLeaseAsync(It.IsAny<JobId>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(true);
            mockStore.Setup(s => s.ReleaseJobLeaseAsync(It.IsAny<JobId>(), It.IsAny<string>())).Returns(Task.CompletedTask);
            mockStore.Setup(s => s.LogJobEndAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<bool?>())).Returns(Task.CompletedTask);
            mockStore.Setup(s => s.UpdateJobLastRunAsync(It.IsAny<JobId>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>())).Returns(Task.CompletedTask);
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

            var service = new SchedulerService(
                serviceProvider,
                mockStore.Object,
                mockLogger.Object,
                throttle,
                mockConfig.Object,
                mockSessionManager.Object,
                new HealthyCapacityMonitor());

            // Use reflection to test the private ExecuteJobAsync method
            var method = typeof(SchedulerService).GetMethod("ExecuteJobAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Act
            // Reflection does not apply a method's default arguments, so every optional parameter
            // has to be named here — adding one to ExecuteJobAsync otherwise breaks this call with
            // a parameter-count mismatch rather than anything about what is under test.
            await (Task)method.Invoke(service, [job, null, null, false]);

            // Assert
            Assert.Equal(3, attempts);

            // The first attempt now carries a session id the scheduler allocates up front rather
            // than null, so the run is identifiable before the executor reports anything back.
            // What this test is about is unchanged: the id the executor returns is what the retries
            // resume under.
            mockExecutor.Verify(e => e.ExecuteTextAsync(job.Script, It.Is<string?>(id => id != null && id != "sess_123"), It.IsAny<CancellationToken>(), job.Name, It.IsAny<long>(), It.IsAny<ETL_SQL.Core.Governance.ExecutionIdentity?>()), Times.Once());
            mockExecutor.Verify(e => e.ExecuteTextAsync(job.Script, "sess_123", It.IsAny<CancellationToken>(), job.Name, It.IsAny<long>(), It.IsAny<ETL_SQL.Core.Governance.ExecutionIdentity?>()), Times.Exactly(2));

            // Verify history was logged for each attempt
            mockStore.Verify(s => s.LogJobStartAsync(job.Id), Times.Exactly(3));
            mockStore.Verify(s => s.LogJobEndAsync(It.IsAny<long>(), "FAILURE", "Fake Failure", 0, It.IsAny<long>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<bool?>()), Times.Exactly(2));
            mockStore.Verify(s => s.LogJobEndAsync(It.IsAny<long>(), "SUCCESS", null, 10, It.IsAny<long>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<bool?>()), Times.Once());

            throttle.Dispose();
            try { if (File.Exists(throttleDbPath)) File.Delete(throttleDbPath); } catch { /* best-effort temp cleanup */ }
        }

        [Fact]
        public async Task ExecuteJobAsync_DoesNotRetryAmbiguousExternalWrite()
        {
            var executor = new Mock<IScriptExecutor>();
            var store = new Mock<IJobHistoryStore>();
            store.Setup(s => s.LogJobStartAsync(It.IsAny<JobId>())).ReturnsAsync(1L);
            store.Setup(s => s.AcquireJobLeaseAsync(
                It.IsAny<JobId>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(1L);
            store.Setup(s => s.TryRenewJobLeaseAsync(
                It.IsAny<JobId>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(true);
            store.Setup(s => s.ReleaseJobLeaseAsync(
                It.IsAny<JobId>(), It.IsAny<string>())).Returns(Task.CompletedTask);
            store.Setup(s => s.LogJobEndAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(),
                It.IsAny<long>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<bool?>()))
                .Returns(Task.CompletedTask);
            store.Setup(s => s.TryUpdateJobLastRunFencedAsync(
                It.IsAny<JobId>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<long>()))
                .ReturnsAsync(true);

            var attempts = 0;
            executor.Setup(e => e.ExecuteTextAsync(
                    It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(),
                    It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<ExecutionIdentity?>()))
                .ReturnsAsync(() =>
                {
                    attempts++;
                    return new ScriptExecutionResult(
                        false, 0, "Ambiguous Gateway write requires operator triage.", RetryAllowed: false);
                });

            var throttleDbPath = Path.Combine(
                Path.GetTempPath(), $"etlsql_ambiguous_retry_{Guid.NewGuid():N}.db");
            var throttleConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Orchestrator:DatabasePath"] = throttleDbPath
                })
                .Build();
            using var throttle = new JobThrottle(
                Options.Create(new JobThrottleOptions { MaxConcurrentJobs = 1 }),
                NullLogger<JobThrottle>.Instance,
                throttleConfig);
            var services = new ServiceCollection().AddSingleton(executor.Object).BuildServiceProvider();
            var scheduler = new SchedulerService(
                services, store.Object, NullLogger<SchedulerService>.Instance, throttle,
                new ConfigurationBuilder().Build(), new Mock<ISessionStateManager>().Object,
                new HealthyCapacityMonitor());
            var job = new JobDefinition(
                "AmbiguousWrite", "SELECT 1;", 1, "HOUR", null, null, null, true,
                MaxRetries: 3, RetryDelaySeconds: 1);
            var method = typeof(SchedulerService).GetMethod(
                "ExecuteJobAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            await (Task)method!.Invoke(scheduler, [job, null, null, false])!;

            Assert.Equal(1, attempts);
            store.Verify(s => s.LogJobStartAsync(job.Id), Times.Once());
            try { if (File.Exists(throttleDbPath)) File.Delete(throttleDbPath); } catch { }
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
                // Re-read for the identity the store assigned: notification links hang off the id,
                // and the definition built above still carries JobId.None.
                job = (await store.GetJobAsync((string?)null, job.Name))!;
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
                services.AddSingleton<IJobCatalogStore>(store);
                services.AddSingleton<ILogger<NotificationDispatchService>>(NullLogger<NotificationDispatchService>.Instance);
                services.AddSingleton<NotificationDispatchService>();
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
                    new Mock<ISessionStateManager>().Object,
                    new HealthyCapacityMonitor());

                var method = typeof(SchedulerService).GetMethod(
                    "ExecuteJobAsync",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                // Reflection does not apply default arguments — every optional parameter is explicit.
                await (Task)method!.Invoke(scheduler, [job, null, null, false])!;

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

        private sealed class HealthyCapacityMonitor : INodeCapacityMonitor
        {
            public NodeCapacitySnapshot Capture() => new(
                WorkingSetBytes: 128L * 1024 * 1024,
                GcHeapBytes: 64L * 1024 * 1024,
                TotalAvailableMemoryBytes: 8L * 1024 * 1024 * 1024,
                MemoryLoadPercent: 1,
                ProcessCpuPercent: 1,
                ProcessorCount: Environment.ProcessorCount,
                IsOverloaded: false,
                CapturedAtUtc: DateTime.UtcNow);
        }

        [Theory]
        [InlineData("SUCCESS", "OnSuccess", "OnFailure", "Job succeeded: TriggerJob")]
        [InlineData("FAILURE", "OnFailure", "OnSuccess", "Job failed: TriggerJob")]
        public async Task DispatchJobNotificationsAsync_SelectsLinksForFinalOutcome(
            string finalStatus,
            string expectedNotification,
            string unexpectedNotification,
            string expectedTitle)
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"etlsql_notify_filter_{Guid.NewGuid():N}.db");
            var catalogRoot = Path.Combine(Path.GetTempPath(), $"etlsql_notify_filter_catalog_{Guid.NewGuid():N}");
            var store = new SQLiteJobHistoryStore(dbPath);
            try
            {
                var job = new JobDefinition("TriggerJob", "SELECT 1;", 1, "HOUR", null, null, null);
                await store.SaveJobAsync(job);
                // Re-read for the store-assigned identity — the links below hang off it.
                job = (await store.GetJobAsync((string?)null, job.Name))!;
                await store.SaveNotificationAsync(new NotificationDefinition("OnSuccess", "notify_webhook"));
                await store.SaveNotificationAsync(new NotificationDefinition("OnFailure", "notify_webhook"));
                await store.AddJobNotificationAsync(job.Name, "OnSuccess", NotificationTrigger.Success);
                await store.AddJobNotificationAsync(job.Name, "OnFailure", NotificationTrigger.Failure);

                var connectionCatalog = await CreateNotificationConnectionCatalogAsync(catalogRoot);
                var scripts = new List<string>();
                var mockExecutor = new Mock<IScriptExecutor>();
                mockExecutor.Setup(e => e.ExecuteTextAsync(
                        It.IsAny<string>(),
                        It.IsAny<string?>(),
                        It.IsAny<CancellationToken>(),
                        It.IsAny<string?>(),
                        It.IsAny<long>(),
                        It.IsAny<ExecutionIdentity?>()))
                    .ReturnsAsync((string script, string? _, CancellationToken _, string? _, long _, ExecutionIdentity? _) =>
                    {
                        scripts.Add(script);
                        return new ScriptExecutionResult(true, 1, null);
                    });

                var dispatch = CreateNotificationDispatchService(store, connectionCatalog, mockExecutor.Object);

                await dispatch.DispatchJobNotificationsAsync(
                    job,
                    finalStatus,
                    historyId: 42,
                    new ScriptExecutionResult(finalStatus == "SUCCESS", 3, finalStatus == "SUCCESS" ? null : "boom"));

                var script = Assert.Single(scripts);
                Assert.Contains(expectedNotification, script);
                Assert.Contains(expectedTitle, script);
                Assert.DoesNotContain(unexpectedNotification, script);
            }
            finally
            {
                try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
                try { if (Directory.Exists(catalogRoot)) Directory.Delete(catalogRoot, recursive: true); } catch { }
            }
        }

        [Theory]
        [InlineData("MissingNotification")]
        [InlineData("DisabledNotification")]
        public async Task DispatchJobNotificationsAsync_SkipsMissingOrDisabledNotifications(string notificationName)
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"etlsql_notify_skip_{Guid.NewGuid():N}.db");
            var catalogRoot = Path.Combine(Path.GetTempPath(), $"etlsql_notify_skip_catalog_{Guid.NewGuid():N}");
            var store = new SQLiteJobHistoryStore(dbPath);
            try
            {
                var job = new JobDefinition("SkipJob", "SELECT 1;", 1, "HOUR", null, null, null);
                await store.SaveJobAsync(job);
                job = (await store.GetJobAsync((string?)null, job.Name))!;

                // "Missing" is now a dangling link rather than a link to a name that was never
                // created: attachments are made against an identity, so the only way a job can point
                // at a notification that is not there is for the row to have gone afterwards. Linking
                // an unissued identity reproduces exactly that state, which is what dispatch must
                // survive — a link the catalog cannot resolve.
                NotificationId notificationId;
                if (notificationName == "DisabledNotification")
                {
                    await store.SaveNotificationAsync(new NotificationDefinition(
                        notificationName,
                        "notify_webhook",
                        IsEnabled: false));
                    notificationId = (await store.GetNotificationAsync((string?)null, notificationName))!.Id;
                }
                else
                {
                    notificationId = NotificationId.New();
                }
                await store.AddJobNotificationAsync(job.Id, notificationId, NotificationTrigger.Completion);

                var connectionCatalog = await CreateNotificationConnectionCatalogAsync(catalogRoot);
                var mockExecutor = new Mock<IScriptExecutor>();
                var dispatch = CreateNotificationDispatchService(store, connectionCatalog, mockExecutor.Object);

                await dispatch.DispatchJobNotificationsAsync(
                    job,
                    "SUCCESS",
                    historyId: 42,
                    new ScriptExecutionResult(true, 3, null));

                mockExecutor.Verify(e => e.ExecuteTextAsync(
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string?>(),
                    It.IsAny<long>(),
                    It.IsAny<ExecutionIdentity?>()), Times.Never);
            }
            finally
            {
                try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
                try { if (Directory.Exists(catalogRoot)) Directory.Delete(catalogRoot, recursive: true); } catch { }
            }
        }

        [Fact]
        public async Task DispatchNotificationAsync_SmtpPayloadIncludesRecipientAndAttachments()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"etlsql_notify_smtp_{Guid.NewGuid():N}.db");
            var catalogRoot = Path.Combine(Path.GetTempPath(), $"etlsql_notify_smtp_catalog_{Guid.NewGuid():N}");
            var attachment = Path.Combine(Path.GetTempPath(), $"etlsql_notify_attachment_{Guid.NewGuid():N}.csv");
            var store = new SQLiteJobHistoryStore(dbPath);
            try
            {
                await File.WriteAllTextAsync(attachment, "id,value");
                await store.SaveNotificationAsync(new NotificationDefinition(
                    "SubscriptionMail",
                    "notify_mail",
                    Recipient: "recipient@example.com"));

                var connectionCatalog = new LocalConnectionCatalogProvider(catalogRoot);
                await connectionCatalog.StoreAsync(new SharedConnectionDefinition(
                    "notify_mail",
                    "SMTP",
                    "smtp.example.invalid",
                    new Dictionary<string, string>
                    {
                        ["HOST"] = "smtp.example.invalid",
                        ["DEFAULT_FROM"] = "noreply@example.com"
                    },
                    Disabled: false));

                string? script = null;
                var mockExecutor = new Mock<IScriptExecutor>();
                mockExecutor.Setup(e => e.ExecuteTextAsync(
                        It.IsAny<string>(),
                        It.IsAny<string?>(),
                        It.IsAny<CancellationToken>(),
                        It.IsAny<string?>(),
                        It.IsAny<long>(),
                        It.IsAny<ExecutionIdentity?>()))
                    .ReturnsAsync((string s, string? sessionId, CancellationToken ct, string? jobName, long queueWaitMs, ExecutionIdentity? identity) =>
                    {
                        script = s;
                        return new ScriptExecutionResult(true, 1, null);
                    });

                var dispatch = CreateNotificationDispatchService(store, connectionCatalog, mockExecutor.Object);

                var result = await dispatch.DispatchNotificationAsync(new NotificationDispatchPayload(
                    "SubscriptionMail",
                    "SUBSCRIPTION",
                    "Report ready",
                    "Attached report.",
                    RecipientOverride: "override@example.com",
                    AttachmentPaths: [attachment]));

                Assert.True(result.Delivered);
                Assert.NotNull(script);
                Assert.Contains("CREATE CONNECTION __job_notification_sink AS SMTP('SHARED:notify_mail')", script);
                Assert.Contains("To, Subject, Body, Attachments", script);
                Assert.Contains("override@example.com", script);
                Assert.Contains("Report ready", script);
                Assert.Contains(attachment.Replace("\\", "/"), script);
            }
            finally
            {
                try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
                try { if (File.Exists(attachment)) File.Delete(attachment); } catch { }
                try { if (Directory.Exists(catalogRoot)) Directory.Delete(catalogRoot, recursive: true); } catch { }
            }
        }

        [Fact]
        public async Task DispatchNotificationAsync_SubscriptionSourceBypassesDisabledNotification()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"etlsql_notify_disabled_{Guid.NewGuid():N}.db");
            var catalogRoot = Path.Combine(Path.GetTempPath(), $"etlsql_notify_disabled_catalog_{Guid.NewGuid():N}");
            var store = new SQLiteJobHistoryStore(dbPath);
            try
            {
                await store.SaveNotificationAsync(new NotificationDefinition(
                    "SubscriptionMail",
                    "notify_mail",
                    Recipient: "recipient@example.com",
                    IsEnabled: false));

                var connectionCatalog = new LocalConnectionCatalogProvider(catalogRoot);
                await connectionCatalog.StoreAsync(new SharedConnectionDefinition(
                    "notify_mail",
                    "SMTP",
                    "smtp.example.invalid",
                    new Dictionary<string, string>
                    {
                        ["HOST"] = "smtp.example.invalid",
                        ["DEFAULT_FROM"] = "noreply@example.com"
                    },
                    Disabled: false));

                var mockExecutor = new Mock<IScriptExecutor>();
                mockExecutor.Setup(e => e.ExecuteTextAsync(
                        It.IsAny<string>(),
                        It.IsAny<string?>(),
                        It.IsAny<CancellationToken>(),
                        It.IsAny<string?>(),
                        It.IsAny<long>(),
                        It.IsAny<ExecutionIdentity?>()))
                    .ReturnsAsync(new ScriptExecutionResult(true, 1, null));

                var dispatch = CreateNotificationDispatchService(store, connectionCatalog, mockExecutor.Object);

                var result = await dispatch.DispatchNotificationAsync(new NotificationDispatchPayload(
                    "SubscriptionMail",
                    "SUBSCRIPTION",
                    "Report ready",
                    "Attached report."));

                Assert.True(result.Delivered);
                mockExecutor.Verify(e => e.ExecuteTextAsync(
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string?>(),
                    It.IsAny<long>(),
                    It.IsAny<ExecutionIdentity?>()), Times.Once());
            }
            finally
            {
                try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
                try { if (Directory.Exists(catalogRoot)) Directory.Delete(catalogRoot, recursive: true); } catch { }
            }
        }

        [Fact]
        public void NotificationDispatchPayload_RejectsMissingAttachment()
        {
            var missing = Path.Combine(Path.GetTempPath(), $"etlsql_missing_attachment_{Guid.NewGuid():N}.csv");

            Assert.Throws<FileNotFoundException>(() => new NotificationDispatchPayload(
                "SubscriptionMail",
                "SUBSCRIPTION",
                "Report ready",
                "Attached report.",
                AttachmentPaths: [missing]));
        }

        private static async Task<LocalConnectionCatalogProvider> CreateNotificationConnectionCatalogAsync(string catalogRoot)
        {
            var connectionCatalog = new LocalConnectionCatalogProvider(catalogRoot);
            await connectionCatalog.StoreAsync(new SharedConnectionDefinition(
                "notify_webhook",
                "WEBHOOK",
                "https://hooks.example.invalid/dq",
                new Dictionary<string, string> { ["FORMAT"] = "generic" },
                Disabled: false));
            return connectionCatalog;
        }

        private static NotificationDispatchService CreateNotificationDispatchService(
            IJobCatalogStore store,
            IConnectionCatalogProvider connectionCatalog,
            IScriptExecutor executor)
        {
            var services = new ServiceCollection();
            services.AddSingleton(executor);
            services.AddSingleton(connectionCatalog);
            services.AddSingleton(store);
            services.AddSingleton<ILogger<NotificationDispatchService>>(NullLogger<NotificationDispatchService>.Instance);
            return new NotificationDispatchService(
                services.BuildServiceProvider(),
                store,
                NullLogger<NotificationDispatchService>.Instance);
        }
    }
}
