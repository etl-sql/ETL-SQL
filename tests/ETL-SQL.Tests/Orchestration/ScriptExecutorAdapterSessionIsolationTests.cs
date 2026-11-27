using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

/// <summary>
/// The in-process scheduler resolves a transient <c>ScriptExecutorAdapter</c> per job, but every
/// adapter shares the singleton <see cref="CliContext"/>. A run's session id decides its spill
/// directory and ledger, so two concurrent jobs must never see each other's id.
/// </summary>
public sealed class ScriptExecutorAdapterSessionIsolationTests : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"etlsql-adapter-session-{Guid.NewGuid():N}");

    public ScriptExecutorAdapterSessionIsolationTests() => Directory.CreateDirectory(_root);

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Temp directory cleanup is best effort.
        }
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ConcurrentRuns_KeepTheirOwnSessionIds_AndLeaveTheSharedContextAlone()
    {
        var sessionRoot = Path.Combine(_root, "sessions");
        Directory.CreateDirectory(sessionRoot);
        await using var provider = (ServiceProvider)DependencyInjectionSetup.BuildServiceProvider(new Dictionary<string, string?>
        {
            ["Orchestrator:DatabasePath"] = Path.Combine(_root, "orch.db"),
            ["Session:Root"] = sessionRoot,
            ["Session:PersistenceDefault"] = "false",
        });

        var sharedContext = provider.GetRequiredService<CliContext>();
        var originalSessionId = sharedContext.SessionId;
        var first = provider.GetRequiredService<IScriptExecutor>();
        var second = provider.GetRequiredService<IScriptExecutor>();

        // The first run is still waiting when the second one starts with a different id.
        var firstRun = first.ExecuteTextAsync("WAITFOR DELAY '00:00:01'; SELECT 1 AS Ok;", "job-first", jobName: "First");
        var secondResult = await second.ExecuteTextAsync("SELECT 2 AS Ok;", "job-second", jobName: "Second");
        var firstResult = await firstRun;

        Assert.True(firstResult.Success, firstResult.ErrorMessage);
        Assert.True(secondResult.Success, secondResult.ErrorMessage);
        Assert.Equal("job-first", firstResult.SessionId);
        Assert.Equal("job-second", secondResult.SessionId);
        Assert.Equal(originalSessionId, sharedContext.SessionId);

        await ((IAsyncDisposable)first).DisposeAsync();
        await ((IAsyncDisposable)second).DisposeAsync();
    }
}
