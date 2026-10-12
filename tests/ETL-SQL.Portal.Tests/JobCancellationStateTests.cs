using System.Reflection;
using ETL_SQL.Orchestrator.Service;

namespace ETL_SQL.Portal.Tests;

public sealed class JobCancellationStateTests
{
    private static readonly Type EntryType = typeof(JobApiEndpoints).GetNestedType("JobEntry", BindingFlags.NonPublic)!;

    [Theory]
    [InlineData("Completed")]
    [InlineData("Failed")]
    public void CancellationDoesNotOverwriteFinishedJobs(string outcome)
    {
        using var cts = new CancellationTokenSource();
        var entry = NewEntry(cts);
        Assert.True((bool)Call(entry, "TryStart")!);
        Assert.True(Finish(entry, outcome));
        Assert.Equal(outcome, Call(entry, "Cancel")!.ToString());
        Assert.False(cts.IsCancellationRequested);
    }

    [Fact]
    public void CancellationBeforeStartPreventsExecutionAndLaterCompletion()
    {
        using var cts = new CancellationTokenSource();
        var entry = NewEntry(cts);
        Assert.Equal("Cancelled", Call(entry, "Cancel")!.ToString());
        Assert.True(cts.IsCancellationRequested);
        Assert.False((bool)Call(entry, "TryStart")!);
        Assert.False(Finish(entry, "Completed"));
    }

    [Fact]
    public async Task ConcurrentCompletionAndCancellationPreserveTheWinningTerminalState()
    {
        for (var i = 0; i < 100; i++)
        {
            using var cts = new CancellationTokenSource();
            var entry = NewEntry(cts);
            Call(entry, "TryStart");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = Task.Run(async () => { await start.Task; return Finish(entry, "Completed"); });
            var cancel = Task.Run(async () => { await start.Task; return Call(entry, "Cancel")!.ToString(); });
            start.SetResult();
            await Task.WhenAll(finish, cancel);
            var expected = await finish ? "Completed" : "Cancelled";
            Assert.Equal(expected, await cancel);
            Assert.Equal(expected, EntryType.GetProperty("Status")!.GetValue(entry)!.ToString());
        }
    }

    private static object NewEntry(CancellationTokenSource cts) => Activator.CreateInstance(EntryType,
        "test-job", cts, null, "owner", "tenant")!;
    private static object? Call(object entry, string method) => EntryType.GetMethod(method)!.Invoke(entry, null);
    private static bool Finish(object entry, string outcome)
    {
        var method = EntryType.GetMethod("TryFinish")!;
        return (bool)method.Invoke(entry, [Enum.Parse(method.GetParameters()[0].ParameterType, outcome)])!;
    }
}
