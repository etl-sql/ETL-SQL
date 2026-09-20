using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Governance;
using ETL_SQL.Orchestrator.Channels;
using ETL_SQL.Orchestrator.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class AdHocJobFailureTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetupFailure_IsSanitizedDisposedAndEligibleForEviction(bool failCreatingScope)
    {
        // Exercise the private execution boundary without starting a scheduler or real provider.
        var api = typeof(JobApiEndpoints);
        var entryType = api.GetNestedType("JobEntry", BindingFlags.NonPublic)!;
        using var cts = new CancellationTokenSource();
        var id = Guid.NewGuid().ToString("N");
        var entry = Activator.CreateInstance(entryType, id, cts, "test-correlation", "test-owner")!;
        var factory = new FailingScopeFactory(failCreatingScope);
        var logger = new CapturingLogger();
        var run = api.GetMethod("RunJobAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        await (Task)run.Invoke(null, [entry, new JobSubmitRequest { ScriptText = "" },
            new ExecutionIdentity { EffectiveUser = "test", RealUser = "test", IsAdmin = false },
            factory, logger, cts.Token])!;

        Assert.Equal("Failed", entryType.GetProperty("Status")!.GetValue(entry)!.ToString());
        Assert.NotNull(entryType.GetProperty("CompletedAt")!.GetValue(entry));
        Assert.Throws<ObjectDisposedException>(() => cts.Token);
        var message = Assert.IsType<string>(entryType.GetProperty("ErrorMessage")!.GetValue(entry));
        Assert.DoesNotContain("setup-test-secret", message);
        Assert.NotEmpty(logger.Messages);
        Assert.All(logger.Messages, value => Assert.DoesNotContain("setup-test-secret", value));
        Assert.Equal(!failCreatingScope, factory.Scope.Disposed);

        var jobs = (IDictionary)api.GetField("_jobs", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        entryType.GetProperty("CompletedAt")!.SetValue(entry, DateTimeOffset.UtcNow.AddHours(-2));
        jobs.Add(id, entry);
        try
        {
            api.GetMethod("EvictStaleAdHocJobs", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
            Assert.False(jobs.Contains(id));
        }
        finally
        {
            jobs.Remove(id);
        }
    }

    [Fact]
    public void HighWatermark_EvictsOldestCompletedJobsAndRetainsActiveJobs()
    {
        var api = typeof(JobApiEndpoints);
        var entryType = api.GetNestedType("JobEntry", BindingFlags.NonPublic)!;
        var jobs = (IDictionary)api.GetField("_jobs", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var ids = new List<string>();
        using var activeCts = new CancellationTokenSource();
        var activeId = Guid.NewGuid().ToString("N");
        jobs.Add(activeId, Activator.CreateInstance(entryType, activeId, activeCts, null, "test-owner")!);
        try
        {
            for (var i = 0; i < 1001; i++)
            {
                var id = Guid.NewGuid().ToString("N");
                ids.Add(id);
                using var cts = new CancellationTokenSource();
                var entry = Activator.CreateInstance(entryType, id, cts, null, "test-owner")!;
                entryType.GetProperty("CompletedAt")!.SetValue(entry, DateTimeOffset.UtcNow.AddMinutes(-30).AddMilliseconds(i));
                jobs.Add(id, entry);
            }
            api.GetMethod("EvictStaleAdHocJobs", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
            Assert.True(jobs.Count <= 1000);
            Assert.False(jobs.Contains(ids[0]));
            Assert.True(jobs.Contains(ids[^1]));
            Assert.True(jobs.Contains(activeId));
            activeCts.Cancel();
        }
        finally
        {
            foreach (var id in ids) jobs.Remove(id);
            jobs.Remove(activeId);
        }
    }
    private sealed class FailingScopeFactory(bool failCreatingScope) : IServiceScopeFactory
    {
        public FailingScope Scope { get; } = new();
        public IServiceScope CreateScope() => failCreatingScope
            ? throw new InvalidOperationException("Setup failed: Password=setup-test-secret;")
            : Scope;
    }

    private sealed class FailingScope : IServiceScope, IServiceProvider
    {
        public bool Disposed { get; private set; }
        public IServiceProvider ServiceProvider => this;
        public object? GetService(Type serviceType) =>
            throw new InvalidOperationException("Resolution failed: Password=setup-test-secret;");
        public void Dispose() => Disposed = true;
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception) + exception);
    }
}
