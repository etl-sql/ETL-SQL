using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Engine.Services;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Services;
using ETL_SQL.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    public class SchedulerServiceTests : IDisposable
    {
        private readonly string _throttleRoot = Path.Combine(
            Path.GetTempPath(), "etl-sql-tests", "scheduler-throttle", Guid.NewGuid().ToString("N"));

        /// <summary>
        /// Builds a SchedulerService with mocked dependencies.
        /// The service provider is wired so CreateScope() returns a scope containing the given executor.
        /// </summary>
        private (SchedulerService service, Mock<IJobHistoryStore> store, Mock<IScriptExecutor> executor)
            Build(
                IEnumerable<JobDefinition> jobs,
                ScriptExecutionResult result,
                INodeCapacityMonitor? capacityMonitor = null,
                Dictionary<string, string?>? config = null)
        {
            capacityMonitor ??= new FixedCapacityMonitor(isOverloaded: false);
            var mockStore = new Mock<IJobHistoryStore>();
            mockStore.Setup(s => s.InitializeAsync()).Returns(Task.CompletedTask);
            mockStore.Setup(s => s.GetActiveJobsAsync()).ReturnsAsync(jobs);
            mockStore.Setup(s => s.GetHistoryAsync(It.IsAny<string?>(), It.IsAny<int>()))
                .ReturnsAsync(Array.Empty<JobHistoryEntry>());
            mockStore.Setup(s => s.LogJobStartAsync(It.IsAny<string>())).ReturnsAsync(1L);
            mockStore.Setup(s => s.TryAcquireJobLeaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(true);
            mockStore.Setup(s => s.AcquireJobLeaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(1L);
            mockStore.Setup(s => s.TryUpdateJobLastRunFencedAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<long>())).ReturnsAsync(true);
            mockStore.Setup(s => s.TryRenewJobLeaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(true);
            mockStore.Setup(s => s.ReleaseJobLeaseAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
            mockStore.Setup(s => s.LogJobEndAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<bool?>())).Returns(Task.CompletedTask);
            mockStore.Setup(s => s.SaveTenantUsageAsync(It.IsAny<TenantUsageRecord>()))
                .Returns(Task.CompletedTask);
            mockStore.Setup(s => s.UpdateJobLastRunAsync(It.IsAny<string>(), It.IsAny<DateTime>(),
                It.IsAny<DateTime?>())).Returns(Task.CompletedTask);

            var mockExecutor = new Mock<IScriptExecutor>();
            mockExecutor.Setup(e => e.ExecuteTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>(), It.IsAny<long>()))
                        .ReturnsAsync(result);

            return (BuildService(mockStore, mockExecutor, capacityMonitor, config), mockStore, mockExecutor);
        }

        private SchedulerService BuildService(
            Mock<IJobHistoryStore> mockStore,
            Mock<IScriptExecutor> mockExecutor,
            INodeCapacityMonitor? capacityMonitor = null,
            Dictionary<string, string?>? config = null,
            Mock<ISessionStateManager>? sessionManager = null)
        {
            capacityMonitor ??= new FixedCapacityMonitor(isOverloaded: false);
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

            Directory.CreateDirectory(_throttleRoot);
            var effectiveConfig = config is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?>(config);
            effectiveConfig.TryAdd(
                "Orchestrator:DatabasePath",
                Path.Combine(_throttleRoot, $"throttle-{Guid.NewGuid():N}.db"));
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(effectiveConfig)
                .Build();
            var throttleOptions = Options.Create(new JobThrottleOptions { MaxConcurrentJobs = 4 });
            var throttle = new JobThrottle(
                throttleOptions,
                new Mock<ILogger<JobThrottle>>().Object,
                configuration);

            sessionManager ??= new Mock<ISessionStateManager>();

            return new SchedulerService(
                mockServiceProvider.Object,
                mockStore.Object,
                new Mock<ILogger<SchedulerService>>().Object,
                throttle,
                configuration,
                sessionManager.Object,
                capacityMonitor);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_throttleRoot))
                    Directory.Delete(_throttleRoot, recursive: true);
            }
            catch
            {
                // SQLite can finish releasing a pooled handle just after a scheduler stops. The
                // per-test directory still prevents cross-test state even if best-effort cleanup
                // has to leave this one temporary artifact for the OS temp sweeper.
            }
        }

        // Scheduler execution is asynchronous; poll for the expected mock interaction instead of a
        // fixed sleep so these tests are not flaky on slow/loaded CI runners (a 500 ms Task.Delay
        // could elapse before the scheduler's first loop executed the job). Returns as soon as the
        // condition holds; a genuinely-never-executed job still fails the subsequent Verify.
        private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 10000)
        {
            await LoadAwareWait.UntilAsync(
                "scheduler mock invocation condition",
                _ => Task.FromResult(condition()),
                observed => observed,
                TimeSpan.FromMilliseconds(timeoutMs),
                TimeSpan.FromMilliseconds(20),
                observed => $"condition={observed}");
        }

        private static bool Invoked<T>(Mock<T> mock, string method) where T : class
            => mock.Invocations.Any(i => i.Method.Name == method);

        private static string? Tag(Activity activity, string key)
        {
            var value = activity.TagObjects.FirstOrDefault(t => t.Key == key).Value;
            return value?.ToString();
        }

        private static Dictionary<string, object?> ToDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var result = new Dictionary<string, object?>();
            foreach (var tag in tags)
                result[tag.Key] = tag.Value;
            return result;
        }

        private static bool HasTag(Dictionary<string, object?> tags, string key, object value) =>
            tags.TryGetValue(key, out var actual) && Equals(actual, value);

        [Fact]
        public void SchedulerObservability_EmitsScheduledJobSpanAndMetrics()
        {
            var stoppedActivities = new List<Activity>();
            using var activityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == SchedulerObservability.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity => stoppedActivities.Add(activity)
            };
            ActivitySource.AddActivityListener(activityListener);

            var measurements = new List<(string Name, double Value, Dictionary<string, object?> Tags)>();
            using var meterListener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == SchedulerObservability.MeterName)
                        listener.EnableMeasurementEvents(instrument);
                }
            };
            meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                measurements.Add((instrument.Name, value, ToDictionary(tags))));
            meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
                measurements.Add((instrument.Name, value, ToDictionary(tags))));
            meterListener.Start();

            var activity = SchedulerObservability.StartScheduledJobActivity(99, "sha256:abc123", attempt: 2);
            SchedulerObservability.CompleteScheduledJobActivity(
                activity,
                "SUCCESS",
                durationMs: 50,
                rowsProcessed: 77,
                peakMemoryBytes: 4096,
                cpuTimeSeconds: 0.75,
                queueWaitMs: 15,
                attempt: 2);
            activity?.Dispose();

            var span = Assert.Single(stoppedActivities);
            Assert.Equal("orchestrator.scheduled_job", span.OperationName);
            Assert.Equal("99", Tag(span, ETL_SQL.Core.Observability.ObservabilityConventions.Tags.JobId));
            Assert.Equal("sha256:abc123", Tag(span, ETL_SQL.Core.Observability.ObservabilityConventions.Tags.ScriptHash));
            Assert.Equal("SUCCESS", Tag(span, ETL_SQL.Core.Observability.ObservabilityConventions.Tags.Status));
            Assert.Equal("2", Tag(span, ETL_SQL.Core.Observability.ObservabilityConventions.Tags.JobAttempt));
            Assert.Equal("15", Tag(span, ETL_SQL.Core.Observability.ObservabilityConventions.Tags.QueueWaitMs));
            Assert.Contains(measurements, m => m.Name == "etlsql.orchestrator.scheduled_job.completed"
                && HasTag(m.Tags, ETL_SQL.Core.Observability.ObservabilityConventions.Tags.Node, Environment.MachineName)
                && HasTag(m.Tags, ETL_SQL.Core.Observability.ObservabilityConventions.Tags.Component, "orchestrator")
                && HasTag(m.Tags, ETL_SQL.Core.Observability.ObservabilityConventions.Tags.Status, "SUCCESS")
                && HasTag(m.Tags, ETL_SQL.Core.Observability.ObservabilityConventions.Tags.WorkloadKind, "scheduled"));
            Assert.Contains(measurements, m => m.Name == "etlsql.orchestrator.scheduled_job.rows_processed" && m.Value == 77);
            Assert.Contains(measurements, m => m.Name == "etlsql.orchestrator.scheduled_job.queue_wait_ms" && m.Value == 15);
            Assert.Contains(measurements, m => m.Name == "etlsql.orchestrator.scheduled_job.attempts" && m.Value == 2);
            Assert.DoesNotContain(measurements, m => m.Tags.ContainsKey(ETL_SQL.Core.Observability.ObservabilityConventions.Tags.JobId));
            Assert.DoesNotContain(measurements, m => m.Tags.ContainsKey(ETL_SQL.Core.Observability.ObservabilityConventions.Tags.ScriptHash));
        }

        [Fact]
        public void PolicyRefreshObservability_EmitsPolicyTraceAndLowCardinalityMetrics()
        {
            var stoppedActivities = new List<Activity>();
            using var activityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == PolicyRefreshObservability.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity => stoppedActivities.Add(activity)
            };
            ActivitySource.AddActivityListener(activityListener);

            var measurements = new List<(string Name, double Value, Dictionary<string, object?> Tags)>();
            using var meterListener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == PolicyRefreshObservability.MeterName)
                        listener.EnableMeasurementEvents(instrument);
                }
            };
            meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                measurements.Add((instrument.Name, value, ToDictionary(tags))));
            meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
                measurements.Add((instrument.Name, value, ToDictionary(tags))));
            meterListener.Start();

            var policy = new ETL_SQL.Core.Governance.EffectiveEnterprisePolicy(
                IsEnrolled: true,
                IsAvailable: true,
                Status: "Live",
                PolicyVersion: "v-refresh",
                Source: "test",
                IssuedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
                ExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(1),
                LoadedAtUtc: DateTimeOffset.UtcNow,
                Document: new ETL_SQL.Core.Governance.OrganizationPolicyDocument(),
                ConfigurationValues: new Dictionary<string, string?>());

            var activity = PolicyRefreshObservability.StartRefreshActivity();
            PolicyRefreshObservability.CompleteRefreshActivity(activity, policy, "success", durationMs: 10);
            activity?.Dispose();

            var span = Assert.Single(stoppedActivities);
            Assert.Equal("orchestrator.policy_refresh", span.OperationName);
            Assert.Equal("success", Tag(span, ETL_SQL.Core.Observability.ObservabilityConventions.Tags.Status));
            Assert.Equal("v-refresh", Tag(span, ETL_SQL.Core.Observability.ObservabilityConventions.Tags.PolicyVersion));
            Assert.False(string.IsNullOrWhiteSpace(Tag(span, ETL_SQL.Core.Observability.ObservabilityConventions.Tags.PolicyHash)));
            Assert.Contains(measurements, m => m.Name == "etlsql.orchestrator.policy_refresh.completed"
                && HasTag(m.Tags, ETL_SQL.Core.Observability.ObservabilityConventions.Tags.Node, Environment.MachineName)
                && HasTag(m.Tags, ETL_SQL.Core.Observability.ObservabilityConventions.Tags.Component, "orchestrator")
                && HasTag(m.Tags, ETL_SQL.Core.Observability.ObservabilityConventions.Tags.Status, "success")
                && HasTag(m.Tags, ETL_SQL.Core.Observability.ObservabilityConventions.Tags.WorkloadKind, "policy-refresh"));
            Assert.DoesNotContain(measurements, m => m.Tags.ContainsKey(ETL_SQL.Core.Observability.ObservabilityConventions.Tags.PolicyVersion));
            Assert.DoesNotContain(measurements, m => m.Tags.ContainsKey(ETL_SQL.Core.Observability.ObservabilityConventions.Tags.PolicyHash));
        }

        [Fact]
        public void EngineExecutionObservability_EmitsEngineSpanAndLowCardinalityMetrics()
        {
            var stoppedActivities = new List<Activity>();
            using var activityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == EngineExecutionObservability.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity => stoppedActivities.Add(activity)
            };
            ActivitySource.AddActivityListener(activityListener);

            var measurements = new List<(string Name, double Value, Dictionary<string, object?> Tags)>();
            using var meterListener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == EngineExecutionObservability.MeterName)
                        listener.EnableMeasurementEvents(instrument);
                }
            };
            meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                measurements.Add((instrument.Name, value, ToDictionary(tags))));
            meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
                measurements.Add((instrument.Name, value, ToDictionary(tags))));
            meterListener.Start();

            var activity = EngineExecutionObservability.StartExecutionActivity("sha256:engine", "job-a", "corr-engine-1");
            EngineExecutionObservability.CompleteExecutionActivity(
                activity,
                "success",
                "job",
                durationMs: 100,
                rowsProcessed: 12,
                peakMemoryBytes: 2048,
                cpuTimeSeconds: 0.5,
                spillBytes: 4096,
                spillReadBytes: 1024);
            activity?.Dispose();

            var span = Assert.Single(stoppedActivities);
            Assert.Equal("engine.execution", span.OperationName);
            Assert.Equal("sha256:engine", Tag(span, ETL_SQL.Core.Observability.ObservabilityConventions.Tags.ScriptHash));
            Assert.Equal("job-a", Tag(span, ETL_SQL.Core.Observability.ObservabilityConventions.Tags.JobId));
            Assert.Equal("corr-engine-1", Tag(span, ETL_SQL.Core.Observability.ObservabilityConventions.Tags.CorrelationId));
            Assert.Equal("4096", Tag(span, ETL_SQL.Core.Observability.ObservabilityConventions.Tags.SpillBytes));
            Assert.Contains(measurements, m => m.Name == "etlsql.engine.execution.completed"
                && HasTag(m.Tags, ETL_SQL.Core.Observability.ObservabilityConventions.Tags.Node, Environment.MachineName)
                && HasTag(m.Tags, ETL_SQL.Core.Observability.ObservabilityConventions.Tags.Component, "engine")
                && HasTag(m.Tags, ETL_SQL.Core.Observability.ObservabilityConventions.Tags.Status, "success")
                && HasTag(m.Tags, ETL_SQL.Core.Observability.ObservabilityConventions.Tags.WorkloadKind, "job"));
            Assert.Contains(measurements, m => m.Name == "etlsql.engine.execution.spill_bytes" && m.Value == 4096);
            Assert.DoesNotContain(measurements, m => m.Tags.ContainsKey(ETL_SQL.Core.Observability.ObservabilityConventions.Tags.ScriptHash));
            Assert.DoesNotContain(measurements, m => m.Tags.ContainsKey(ETL_SQL.Core.Observability.ObservabilityConventions.Tags.JobId));
            Assert.DoesNotContain(measurements, m => m.Tags.ContainsKey(ETL_SQL.Core.Observability.ObservabilityConventions.Tags.CorrelationId));
        }

        [Fact]
        public void EngineExecutionObservability_AllowsHashlessSpanWhenTracingContextHasNoScriptHash()
        {
            var stoppedActivities = new List<Activity>();
            using var activityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == EngineExecutionObservability.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity => stoppedActivities.Add(activity)
            };
            ActivitySource.AddActivityListener(activityListener);

            var measurements = new List<(string Name, double Value, Dictionary<string, object?> Tags)>();
            using var meterListener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == EngineExecutionObservability.MeterName)
                        listener.EnableMeasurementEvents(instrument);
                }
            };
            meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                measurements.Add((instrument.Name, value, ToDictionary(tags))));
            meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
                measurements.Add((instrument.Name, value, ToDictionary(tags))));
            meterListener.Start();

            var activity = EngineExecutionObservability.StartExecutionActivity(null, null);
            EngineExecutionObservability.CompleteExecutionActivity(
                activity,
                "success",
                "script",
                durationMs: 1,
                rowsProcessed: 0,
                peakMemoryBytes: 0,
                cpuTimeSeconds: 0,
                spillBytes: 0,
                spillReadBytes: 0);
            activity?.Dispose();

            var span = Assert.Single(stoppedActivities);
            Assert.Equal("engine.execution", span.OperationName);
            Assert.Null(Tag(span, ETL_SQL.Core.Observability.ObservabilityConventions.Tags.ScriptHash));
            Assert.Null(Tag(span, ETL_SQL.Core.Observability.ObservabilityConventions.Tags.JobId));
            Assert.Contains(measurements, m => m.Name == "etlsql.engine.execution.completed"
                && HasTag(m.Tags, ETL_SQL.Core.Observability.ObservabilityConventions.Tags.WorkloadKind, "script"));
            Assert.DoesNotContain(measurements, m => m.Tags.ContainsKey(ETL_SQL.Core.Observability.ObservabilityConventions.Tags.ScriptHash));
            Assert.DoesNotContain(measurements, m => m.Tags.ContainsKey(ETL_SQL.Core.Observability.ObservabilityConventions.Tags.JobId));
        }

        [Fact]
        public async Task Job_WithNullNextRun_IsExecuted()
        {
            var jobs = new[] { new JobDefinition("TestJob", "SELECT 1;", 1, "HOUR", null, null, null) };
            var (service, _, executor) = Build(jobs, new ScriptExecutionResult(true, 5));

            service.Start();
            await WaitUntilAsync(() => Invoked(executor, nameof(IScriptExecutor.ExecuteTextAsync)));
            service.Stop();

            executor.Verify(e => e.ExecuteTextAsync("SELECT 1;", It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>(), It.IsAny<long>()), Times.AtLeastOnce());
        }

        [Fact]
        public async Task Job_WithPastNextRun_IsExecuted()
        {
            var pastDate = DateTime.Now.AddHours(-1);
            var jobs = new[] { new JobDefinition("OldJob", "PRINT 'hi';", 1, "DAY", null, null, pastDate) };
            var (service, _, executor) = Build(jobs, new ScriptExecutionResult(true, 0));

            service.Start();
            await WaitUntilAsync(() => Invoked(executor, nameof(IScriptExecutor.ExecuteTextAsync)));
            service.Stop();

            executor.Verify(e => e.ExecuteTextAsync("PRINT 'hi';", It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>(), It.IsAny<long>()), Times.AtLeastOnce());
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

            executor.Verify(e => e.ExecuteTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never());
        }

        [Fact]
        public async Task SuccessfulJob_LogsSuccessToHistory()
        {
            var jobs = new[] { new JobDefinition("LogJob", "SELECT 1;", 1, "HOUR", null, null, null) };
            var (service, store, _) = Build(jobs, new ScriptExecutionResult(true, 42));

            service.Start();
            await WaitUntilAsync(() => Invoked(store, nameof(IJobHistoryStore.LogJobEndAsync)));
            service.Stop();

            store.Verify(s => s.LogJobStartAsync("LogJob"), Times.AtLeastOnce());
            store.Verify(s => s.LogJobEndAsync(1L, "SUCCESS", null, 42, It.IsAny<long>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<bool?>()), Times.AtLeastOnce());
        }

        [Fact]
        public async Task TenantBoundJob_WritesCountsOnlyUsageFromPersistedBinding()
        {
            var job = new JobDefinition(
                "TenantJob", "SELECT 1;", 1, "HOUR", null, null, null,
                TenantId: "tenant-alpha");
            var (service, store, _) = Build([job], new ScriptExecutionResult(
                true, 42, PeakMemoryBytes: 8192, CpuTimeSeconds: 1.5));

            service.Start();
            await WaitUntilAsync(() => Invoked(store, nameof(IJobHistoryStore.SaveTenantUsageAsync)));
            service.Stop();

            store.Verify(s => s.SaveTenantUsageAsync(It.Is<TenantUsageRecord>(usage =>
                usage.TenantId == "tenant-alpha" &&
                usage.JobHistoryId == 1 &&
                usage.WorkloadKind == nameof(JobTargetKind.Script) &&
                usage.Status == "SUCCESS" &&
                usage.RowsProcessed == 42 &&
                usage.PeakMemoryBytes == 8192 &&
                usage.CpuTimeSeconds == 1.5 &&
                usage.DurationMs >= 0)), Times.AtLeastOnce());
        }

        [Fact]
        public async Task LegacyUnboundJob_DoesNotWriteTenantUsage()
        {
            var job = new JobDefinition("LegacyJob", "SELECT 1;", 1, "HOUR", null, null, null);
            var (service, store, _) = Build([job], new ScriptExecutionResult(true, 1));

            service.Start();
            await WaitUntilAsync(() => Invoked(store, nameof(IJobHistoryStore.LogJobEndAsync)));
            service.Stop();

            store.Verify(s => s.SaveTenantUsageAsync(It.IsAny<TenantUsageRecord>()), Times.Never());
        }

        [Fact]
        public async Task MeteringFailure_DoesNotChangeSuccessfulExecutionOutcome()
        {
            var job = new JobDefinition(
                "MeterFailureJob", "SELECT 1;", 1, "HOUR", null, null, null,
                TenantId: "tenant-alpha");
            var (service, store, executor) = Build([job], new ScriptExecutionResult(true, 5));
            store.Setup(s => s.SaveTenantUsageAsync(It.IsAny<TenantUsageRecord>()))
                .ThrowsAsync(new InvalidOperationException("meter unavailable"));

            service.Start();
            await WaitUntilAsync(() => Invoked(store, nameof(IJobHistoryStore.SaveTenantUsageAsync)));
            service.Stop();

            executor.Verify(e => e.ExecuteTextAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(),
                It.IsAny<string>(), It.IsAny<long>()), Times.AtLeastOnce());
            store.Verify(s => s.LogJobEndAsync(
                1L, "SUCCESS", null, 5, It.IsAny<long>(), It.IsAny<double>(),
                It.IsAny<string?>(), It.IsAny<bool?>()), Times.AtLeastOnce());
        }

        [Fact]
        public async Task FailedJob_LogsFailureToHistory()
        {
            var jobs = new[] { new JobDefinition("FailJob", "BAD SQL;", 1, "HOUR", null, null, null) };
            var (service, store, _) = Build(jobs, new ScriptExecutionResult(false, 0, "Parse error"));

            service.Start();
            await WaitUntilAsync(() => Invoked(store, nameof(IJobHistoryStore.LogJobEndAsync)));
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
            mockStore.Setup(s => s.TryAcquireJobLeaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(true);
            mockStore.Setup(s => s.AcquireJobLeaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(1L);
            mockStore.Setup(s => s.TryUpdateJobLastRunFencedAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<long>())).ReturnsAsync(true);
            mockStore.Setup(s => s.TryRenewJobLeaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(true);
            mockStore.Setup(s => s.ReleaseJobLeaseAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
            mockStore.Setup(s => s.LogJobEndAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<bool?>())).Returns(Task.CompletedTask);
            mockStore.Setup(s => s.UpdateJobLastRunAsync(It.IsAny<string>(), It.IsAny<DateTime>(),
                It.IsAny<DateTime?>())).Returns(Task.CompletedTask);

            var mockExecutor = new Mock<IScriptExecutor>();
            mockExecutor.Setup(e => e.ExecuteTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>(), It.IsAny<long>()))
                        .ThrowsAsync(new InvalidOperationException("DB connection lost"));

            var service = BuildService(mockStore, mockExecutor);

            service.Start();
            await WaitUntilAsync(() => Invoked(mockStore, nameof(IJobHistoryStore.LogJobEndAsync)));
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
            await WaitUntilAsync(() => Invoked(store, nameof(IJobHistoryStore.LogJobEndAsync)));
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
            await WaitUntilAsync(() => Invoked(store, nameof(IJobHistoryStore.TryUpdateJobLastRunFencedAsync)));
            service.Stop();

            store.Verify(s => s.TryUpdateJobLastRunFencedAsync("UpdateJob", It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<long>()),
                Times.AtLeastOnce());
        }

        [Fact]
        public async Task OverloadedNode_DoesNotClaimLeaseOrRunJob()
        {
            var jobs = new[] { new JobDefinition("HotNodeJob", "SELECT 1;", 1, "HOUR", null, null, null) };
            var overloaded = new FixedCapacityMonitor(isOverloaded: true);
            var (service, store, executor) = Build(
                jobs,
                new ScriptExecutionResult(true, 0),
                overloaded,
                new Dictionary<string, string?> { ["Scheduler:SleepIntervalSeconds"] = "1" });

            service.Start();
            await Task.Delay(350);
            service.Stop();

            store.Verify(s => s.AcquireJobLeaseAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never());
            executor.Verify(e => e.ExecuteTextAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>(), It.IsAny<long>()),
                Times.Never());
        }

        [Fact]
        public async Task RepeatedFailures_DisablesJobAsQuarantined()
        {
            var job = new JobDefinition("FlakyJob", "BAD SQL;", 1, "HOUR", null, null, null);
            var (service, store, _) = Build(
                [job],
                new ScriptExecutionResult(false, 0, "Parse error"),
                config: new Dictionary<string, string?>
                {
                    ["Scheduler:SleepIntervalSeconds"] = "1",
                    ["Scheduler:QuarantineFailureThreshold"] = "2"
                });
            store.Setup(s => s.GetHistoryAsync("FlakyJob", 2))
                .ReturnsAsync([
                    new JobHistoryEntry(2, "FlakyJob", DateTime.Now, DateTime.Now, "FAILURE", "Parse error"),
                    new JobHistoryEntry(1, "FlakyJob", DateTime.Now.AddMinutes(-1), DateTime.Now, "FAILURE", "Parse error")
                ]);

            service.Start();
            await WaitUntilAsync(() => Invoked(store, nameof(IJobHistoryStore.SaveJobAsync)));
            service.Stop();

            store.Verify(s => s.SaveJobAsync(
                It.Is<JobDefinition>(j => j.Name == "FlakyJob" && !j.IsEnabled)), Times.AtLeastOnce());
            store.Verify(s => s.LogJobEndAsync(
                It.IsAny<long>(), "QUARANTINED", It.Is<string>(m => m!.Contains("consecutive failures")),
                It.IsAny<long>(), It.IsAny<long>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<bool?>()),
                Times.AtLeastOnce());
        }

        [Fact]
        public async Task ResumeJob_UsesRecordedSessionAndNamedCheckpoint()
        {
            const string script = "SET PERSIST ON; resume_here: SELECT 1;";
            var job = new JobDefinition("ResumeJob", script, 1, "HOUR", null, null, DateTime.Now.AddHours(1));
            var history = new JobHistoryEntry(
                42, job.Name, DateTime.Now.AddMinutes(-1), DateTime.Now, "FAILURE", "boom",
                SessionId: "session-42", CheckpointLabel: "resume_here");

            var store = new Mock<IJobHistoryStore>();
            store.Setup(s => s.GetHistoryEntryAsync(42)).ReturnsAsync(history);
            store.Setup(s => s.GetJobAsync(job.Name)).ReturnsAsync(job);
            store.Setup(s => s.LogJobStartAsync(job.Name)).ReturnsAsync(43L);
            store.Setup(s => s.AcquireJobLeaseAsync(job.Name, It.IsAny<string>(), It.IsAny<TimeSpan>()))
                .ReturnsAsync(1L);
            store.Setup(s => s.TryRenewJobLeaseAsync(job.Name, It.IsAny<string>(), It.IsAny<TimeSpan>()))
                .ReturnsAsync(true);
            store.Setup(s => s.ReleaseJobLeaseAsync(job.Name, It.IsAny<string>())).Returns(Task.CompletedTask);
            store.Setup(s => s.TryUpdateJobLastRunFencedAsync(
                job.Name, It.IsAny<DateTime>(), It.IsAny<DateTime?>(), 1L)).ReturnsAsync(true);

            var sessions = new Mock<ISessionStateManager>();
            sessions.Setup(s => s.LoadSession("session-42")).ReturnsAsync(new SessionState
            {
                SessionId = "session-42",
                GlobalVariables = new Dictionary<string, object?>
                {
                    ["@_LAST_CHECKPOINT_LABEL"] = "resume_here"
                }
            });

            var executor = new Mock<IScriptExecutor>();
            executor.Setup(e => e.ResumeTextAsync(
                    script, "session-42", It.IsAny<CancellationToken>(), job.Name, It.IsAny<long>(), null))
                .ReturnsAsync(new ScriptExecutionResult(true, 1, SessionId: "session-42"));

            var service = BuildService(store, executor, sessionManager: sessions);
            var result = await service.ResumeJobAsync(42);
            await WaitUntilAsync(() => Invoked(executor, nameof(IScriptExecutor.ResumeTextAsync)));

            Assert.Equal(ResumeTriggerStatus.Accepted, result.Status);
            Assert.Equal("resume_here", result.CheckpointLabel);
            executor.Verify(e => e.ResumeTextAsync(
                script, "session-42", It.IsAny<CancellationToken>(), job.Name, It.IsAny<long>(), null), Times.Once());
        }

        private sealed class FixedCapacityMonitor(bool isOverloaded) : INodeCapacityMonitor
        {
            public NodeCapacitySnapshot Capture() => new(
                WorkingSetBytes: 128 * 1024 * 1024,
                GcHeapBytes: 64 * 1024 * 1024,
                TotalAvailableMemoryBytes: 1024L * 1024 * 1024,
                MemoryLoadPercent: isOverloaded ? 99 : 10,
                ProcessCpuPercent: isOverloaded ? 99 : 1,
                ProcessorCount: Environment.ProcessorCount,
                IsOverloaded: isOverloaded,
                CapturedAtUtc: DateTime.UtcNow);
        }
    }
}
