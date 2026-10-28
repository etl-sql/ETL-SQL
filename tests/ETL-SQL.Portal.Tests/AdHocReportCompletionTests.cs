using System.Reflection;
using ETL_SQL.Core;
using ETL_SQL.Core.Governance;
using ETL_SQL.Orchestrator.Channels;
using ETL_SQL.Orchestrator.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
[Trait("Category", "Smoke.Portal")]
public sealed class AdHocReportCompletionTests
{
    [Theory]
    [InlineData("complete", "Completed")]
    [InlineData("cancel", "Cancelled")]
    [InlineData("fail", "Failed")]
    public async Task ReportPreparationRemainsRunningAndPublishesTheFinalOutcome(string action, string expected)
    {
        var api = typeof(JobApiEndpoints);
        var entryType = api.GetNestedType("JobEntry", BindingFlags.NonPublic)!;
        using var cts = new CancellationTokenSource();
        var entry = Activator.CreateInstance(entryType, Guid.NewGuid().ToString("N"), cts, null, "owner", null)!;
        using var factory = new OrchestratorWebFactory();
        using var logger = new PausedReportLogger(action == "fail");
        var sessionId = "completion-" + Guid.NewGuid().ToString("N");
        var request = new JobSubmitRequest
        {
            ScriptText = CrossHostConsistencyTests.FixtureScript,
            SessionId = sessionId,
            Metadata = new() { ["IsReport"] = "true", ["ReportId"] = "completion" }
        };
        var run = api.GetMethod("RunJobAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        var execution = Task.Run(async () => await (Task)run.Invoke(null,
            [entry, request, new ExecutionIdentity { EffectiveUser = "test", RealUser = "test", IsAdmin = false },
                factory.Services.GetRequiredService<IServiceScopeFactory>(), logger, cts.Token])!);
        try
        {
            await logger.Preparing.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal("Running", entryType.GetProperty("Status")!.GetValue(entry)!.ToString());
            Assert.Null(entryType.GetProperty("ReportManifestJson")!.GetValue(entry));
            if (action == "cancel")
                Assert.Equal("Cancelled", entryType.GetMethod("Cancel")!.Invoke(entry, null)!.ToString());
        }
        finally
        {
            logger.Continue.Set();
            await execution;
            var snapshot = Path.GetFullPath(Path.Combine("Snapshots", $"report_completion_{sessionId}.snapshot.json"));
            if (File.Exists(snapshot)) File.Delete(snapshot);
        }
        Assert.Equal(expected, entryType.GetProperty("Status")!.GetValue(entry)!.ToString());
        Assert.NotNull(entryType.GetProperty("CompletedAt")!.GetValue(entry));
        if (action == "fail")
            Assert.Contains("Report preparation failed", (string)entryType.GetProperty("ErrorMessage")!.GetValue(entry)!);
        if (action == "complete")
            Assert.False(string.IsNullOrWhiteSpace((string?)entryType.GetProperty("ReportManifestJson")!.GetValue(entry)));
        else
            Assert.Null(entryType.GetProperty("ReportManifestJson")!.GetValue(entry));
    }

    [Fact]
    [Trait("CompatBreak", "0.22")]
    public async Task ReportExecutorWithoutAManifestContextFailsInsteadOfPublishingAnEmptySuccess()
    {
        var api = typeof(JobApiEndpoints);
        var entryType = api.GetNestedType("JobEntry", BindingFlags.NonPublic)!;
        using var cts = new CancellationTokenSource();
        var entry = Activator.CreateInstance(entryType, Guid.NewGuid().ToString("N"), cts, null, "owner", null)!;
        using var provider = new ServiceCollection().AddSingleton<IScriptExecutor, SuccessfulExecutor>().BuildServiceProvider();
        var run = api.GetMethod("RunJobAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        await (Task)run.Invoke(null, [entry, new JobSubmitRequest { ScriptText = "", Metadata = new() { ["IsReport"] = "true" } },
            new ExecutionIdentity { EffectiveUser = "test", RealUser = "test", IsAdmin = false },
            provider.GetRequiredService<IServiceScopeFactory>(), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, cts.Token])!;
        Assert.Equal("Failed", entryType.GetProperty("Status")!.GetValue(entry)!.ToString());
        Assert.Contains("manifest", ((string)entryType.GetProperty("ErrorMessage")!.GetValue(entry)!).ToLowerInvariant());
        Assert.Null(entryType.GetProperty("ReportManifestJson")!.GetValue(entry));
    }

    private sealed class SuccessfulExecutor : IScriptExecutor
    {
        public Task<ScriptExecutionResult> ExecuteTextAsync(string scriptText, string? sessionId = null,
            CancellationToken cancellationToken = default, string? jobName = null, long queueWaitMs = 0,
            ExecutionIdentity? executionIdentity = null) => Task.FromResult(new ScriptExecutionResult(true, 1));
    }

    private sealed class PausedReportLogger(bool fail) : ILogger, IDisposable
    {
        public TaskCompletionSource Preparing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Continue { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!formatter(state, exception).Contains("is a report; building manifest", StringComparison.Ordinal)) return;
            Preparing.SetResult();
            // Hold this exact execution boundary until the observer inspects/cancels the job.
            if (!Continue.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("Report barrier was not released.");
            if (fail) throw new InvalidOperationException("Report preparation failed.");
        }
        public void Dispose() => Continue.Dispose();
    }
}
