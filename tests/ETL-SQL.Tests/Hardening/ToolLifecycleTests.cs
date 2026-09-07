using System.Diagnostics;
using System.Text;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;
using ETL_SQL.Engine.Handlers;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Hardening;

public class ToolLifecycleTests
{
    [Theory]
    [InlineData("input")]
    [InlineData("output")]
    [InlineData("wait")]
    public async Task RunCancellationStopsAndAwaitsProcess(string stage)
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var process = new FakeProcess { BlockWait = stage == "wait", Entered = entered };
        if (stage == "output") process.Output = new BlockingReader(entered);
        var context = Context(cancellation.Token);
        TableReference? source = null;
        if (stage == "input")
        {
            source = new TableReference("#input");
            var data = new Mock<IDataSource>();
            data.Setup(d => d.GetColumnsAsync(It.IsAny<CancellationToken>())).Returns(async (CancellationToken token) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new List<string>();
            });
            context.Object.Connections["#input"] = data.Object;
        }
        var factory = new FakeFactory(process);
        var handler = Handler(factory);
        var running = handler.Execute(new ExecuteToolStatement("test", source), context.Object);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, process.Kills);
        Assert.True(process.Waits > 0);
        Assert.True(process.Disposed);
        AssertRemoval(factory);
    }

    [Fact]
    public async Task DeadlineIsReportedAsTimeoutAndStillRemovesContainer()
    {
        var process = new FakeProcess { BlockWait = true, Entered = new TaskCompletionSource() };
        var factory = new FakeFactory(process);
        var error = await Assert.ThrowsAsync<ExecutionException>(() => Handler(factory, timeout: "1")
            .Execute(new ExecuteToolStatement("test"), Context().Object).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Contains("timed out after 1 seconds", error.Message);
        Assert.Equal(1, process.Kills);
        AssertRemoval(factory);
    }

    [Fact]
    public async Task StreamFailureStopsProcessAndRemovesContainer()
    {
        var process = new FakeProcess { Output = new FailingReader() };
        var factory = new FakeFactory(process);
        await Assert.ThrowsAsync<IOException>(() => Handler(factory).Execute(new ExecuteToolStatement("test"), Context().Object));
        Assert.Equal(1, process.Kills);
        AssertRemoval(factory);
    }

    [Fact]
    public async Task FailedStartStillChecksContainerRemoval()
    {
        var factory = new FakeFactory(new FakeProcess { FailStart = true });
        await Assert.ThrowsAsync<IOException>(() => Handler(factory).Execute(new ExecuteToolStatement("test"), Context().Object));
        AssertRemoval(factory);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnprovenRemovalFailsExecutionAndLogsOperationalError(bool daemonUnavailable)
    {
        var factory = new FakeFactory(new FakeProcess()) { RemainingContainer = !daemonUnavailable, QueryExitCode = daemonUnavailable ? 1 : 0 };
        var logger = new Mock<ILogger>();
        var error = await Assert.ThrowsAsync<ExecutionException>(() => Handler(factory, logger: logger.Object)
            .Execute(new ExecuteToolStatement("test"), Context().Object));
        Assert.Contains("removal could not be verified", error.Message);
        logger.Verify(l => l.Error(It.Is<string>(s => s.Contains("teardown failed")), It.IsAny<Exception>(), It.IsAny<object?[]>()), Times.Once);
    }

    [Fact]
    public async Task StderrIsDrainedWithBoundedHeadAndTail()
    {
        var stderr = "diagnostic-head " + new string('x', 1000000) + " diagnostic-tail";
        var process = new FakeProcess { Error = Reader(stderr), Code = 1 };
        var logger = new Mock<ILogger>();
        var error = await Assert.ThrowsAsync<ExecutionException>(() => Handler(new FakeFactory(process),
            new Dictionary<string, string?> { ["Tools:Limits:MaxStderrChars"] = "128" }, logger.Object)
            .Execute(new ExecuteToolStatement("test"), Context().Object));
        Assert.Contains("diagnostic-head", error.Message);
        Assert.Contains("diagnostic-tail", error.Message);
        Assert.Contains("truncated", error.Message);
        Assert.True(error.Message.Length < 512);
        logger.Verify(l => l.Warning(It.Is<string>(s => s.Contains("stderr truncated")), It.IsAny<object?[]>()), Times.Once);
    }

    [Theory]
    [InlineData("Tools:Limits:MaxLineChars", "line exceeded")]
    [InlineData("Tools:Limits:MaxBytes", "bytes")]
    public async Task DiscardedOutputAlsoHonorsBounds(string limit, string expected)
    {
        var process = new FakeProcess { Output = Reader(new string('x', 10000)) };
        var factory = new FakeFactory(process);
        var error = await Assert.ThrowsAsync<ExecutionException>(() => Handler(factory,
            new Dictionary<string, string?> { [limit] = "128" }).Execute(new ExecuteToolStatement("test"), Context().Object));
        Assert.Contains(expected, error.Message);
        Assert.Equal(1, process.Kills);
        AssertRemoval(factory);
    }

    [Fact]
    public async Task CatalogReceivesRunCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var catalog = new Mock<IToolCatalogProvider>();
        catalog.Setup(c => c.ResolveAsync("test", It.IsAny<ExecutionIdentity>(), cancellation.Token))
            .Returns(Task.FromCanceled<ToolDefinition>(cancellation.Token));
        var factory = new FakeFactory(new FakeProcess());
        var handler = new ExecuteToolStatementHandler(new Mock<ILogger>().Object, catalog.Object, processFactory: factory);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.Execute(new ExecuteToolStatement("test"), Context(cancellation.Token).Object));
        Assert.Empty(factory.Commands);
    }

    private static ExecuteToolStatementHandler Handler(FakeFactory factory, Dictionary<string, string?>? settings = null, ILogger? logger = null, string timeout = "60")
    {
        var catalog = new Mock<IToolCatalogProvider>();
        catalog.Setup(c => c.ResolveAsync(It.IsAny<string>(), It.IsAny<ExecutionIdentity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolDefinition("test", "CONTAINER", new Dictionary<string, string>
            {
                ["COMMAND"] = "test",
                ["IMAGE"] = "test@sha256:" + new string('a', 64),
                ["TIMEOUT"] = timeout
            }, false));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? new()).Build();
        return new ExecuteToolStatementHandler(logger ?? new Mock<ILogger>().Object, catalog.Object, configuration, processFactory: factory);
    }

    private static Mock<IExecutionContext> Context(CancellationToken token = default)
    {
        var context = new Mock<IExecutionContext>();
        context.SetupGet(c => c.CancellationToken).Returns(token);
        context.SetupGet(c => c.Connections).Returns(new Dictionary<string, IDataSource>());
        return context;
    }

    private static void AssertRemoval(FakeFactory factory)
    {
        Assert.Equal(3, factory.Commands.Count);
        var run = factory.Commands[0].ArgumentList.ToList();
        var name = run[run.IndexOf("--name") + 1];
        Assert.StartsWith("etlsql-tool-", name);
        Assert.Equal(new[] { "rm", "--force", name }, factory.Commands[1].ArgumentList);
        Assert.Contains($"name=^/{name}$", factory.Commands[2].ArgumentList);
    }

    private static StreamReader Reader(string value) => new(new MemoryStream(Encoding.UTF8.GetBytes(value)));

    private sealed class FakeFactory(FakeProcess main) : IToolProcessFactory
    {
        public List<ProcessStartInfo> Commands { get; } = [];
        public bool RemainingContainer { get; init; }
        public int QueryExitCode { get; init; }
        public IToolProcess Create(ProcessStartInfo start)
        {
            Commands.Add(start);
            if (Commands.Count == 1) return main;
            return new FakeProcess { Output = Reader(Commands.Count == 3 && RemainingContainer ? "container-id" : ""), Code = Commands.Count == 3 ? QueryExitCode : 0 };
        }
    }

    private sealed class FakeProcess : IToolProcess
    {
        public StreamWriter StandardInput { get; } = new(new MemoryStream());
        public StreamReader Output { get; set; } = Reader("");
        public StreamReader Error { get; init; } = Reader("");
        public StreamReader StandardOutput => Output;
        public StreamReader StandardError => Error;
        public bool HasExited { get; private set; }
        public int Code { get; init; }
        public int ExitCode => Code;
        public bool BlockWait { get; init; }
        public bool FailStart { get; init; }
        public TaskCompletionSource? Entered { get; init; }
        public int Kills { get; private set; }
        public int Waits { get; private set; }
        public bool Disposed { get; private set; }
        public void Start() { if (FailStart) throw new IOException("Synthetic start failure"); }
        public async Task WaitForExitAsync(CancellationToken token)
        {
            Waits++;
            if (BlockWait && !HasExited)
            {
                Entered!.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            HasExited = true;
        }
        public void Kill() { Kills++; HasExited = true; }
        public void Dispose() { StandardInput.Dispose(); Output.Dispose(); Error.Dispose(); Disposed = true; }
    }

    private sealed class BlockingReader(TaskCompletionSource entered) : StreamReader(Stream.Null)
    {
        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }

    private sealed class FailingReader() : StreamReader(Stream.Null)
    {
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromException<int>(new IOException("Synthetic stream failure"));
    }
}
