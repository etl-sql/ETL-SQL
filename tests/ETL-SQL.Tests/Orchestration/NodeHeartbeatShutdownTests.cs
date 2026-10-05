using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public sealed class NodeHeartbeatShutdownTests
{
    [Fact]
    public async Task ConcurrentShutdown_WaitsForOneDeregistration()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new Mock<INodeRegistryStore>();
        var deregistrations = 0;
        store.Setup(value => value.DeregisterNodeAsync(It.IsAny<string>()))
            .Returns(() =>
            {
                if (Interlocked.Increment(ref deregistrations) > 1) return Task.CompletedTask;
                entered.TrySetResult();
                return release.Task;
            });
        using var service = new NodeHeartbeatService(store.Object, new ConfigurationBuilder().Build(),
            NullLogger<NodeHeartbeatService>.Instance, "Orchestrator");

        var first = service.StopAsync(CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = service.StopAsync(CancellationToken.None);
        try
        {
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            store.Verify(value => value.DeregisterNodeAsync(service.NodeId), Times.Once);
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        }

        await service.StopAsync(CancellationToken.None);
        store.Verify(value => value.DeregisterNodeAsync(service.NodeId), Times.Once);
    }
}
