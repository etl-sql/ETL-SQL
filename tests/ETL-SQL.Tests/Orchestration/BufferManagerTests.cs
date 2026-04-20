using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Core.Execution;

namespace ETL_SQL.Tests.Orchestration
{
    public class BufferManagerTests
    {
        private class TestLogger<T> : ILogger<T>
        {
            public List<string> Logs { get; } = new();
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                Logs.Add(formatter(state, exception));
            }
        }

        private BufferManager CreateBufferManager(int maxMemMB = 100, int maxCursors = 2, int timeoutSec = 2, ISystemResources? sysRes = null)
        {
            var options = Options.Create(new BufferManagerOptions
            {
                MaxGlobalMemoryMB = maxMemMB,
                MaxStreamingCursors = maxCursors,
                ResourceWaitTimeoutSeconds = timeoutSec,
                HysteresisMemoryMB = 0 // Disable for small precision tests
            });

            if (sysRes == null)
            {
                var mock = new Mock<ISystemResources>();
                mock.Setup(r => r.GetAvailableMemoryBytes()).Returns(32L * 1024 * 1024 * 1024); // 32GB available
                sysRes = mock.Object;
            }

            return new BufferManager(options, new TestLogger<BufferManager>(), sysRes);
        }

        [Fact]
        public async Task BufferManager_EnforcesMemoryLimit()
        {
            var bm = CreateBufferManager(maxMemMB: 10); // 10MB limit
            
            // Request 6MB - should succeed
            using var res1 = await bm.ReserveMemoryAsync("session1", 6 * 1024 * 1024);
            Assert.NotNull(res1);

            // Request 6MB more - should timeout since 6+6 > 10
            await Assert.ThrowsAsync<TimeoutException>(() => 
                bm.ReserveMemoryAsync("session2", 6 * 1024 * 1024));
        }

        [Fact]
        public async Task BufferManager_FifoOrder_Memory()
        {
            var bm = CreateBufferManager(maxMemMB: 10);
            
            // Hold all memory
            var res1 = await bm.ReserveMemoryAsync("session1", 10 * 1024 * 1024);
            
            var task2 = bm.ReserveMemoryAsync("session2", 5 * 1024 * 1024);
            var task3 = bm.ReserveMemoryAsync("session3", 5 * 1024 * 1024);
            
            // Release session 1
            res1.Dispose();
            
            // Task 2 should complete first (FIFO)
            var res2 = await task2;
            Assert.NotNull(res2);
            
            // Task 3 should still be waiting (since task 2 took 5MB and 10 is max)
            // If we release task 2, task 3 is next
            res2.Dispose();
            var res3 = await task3;
            Assert.NotNull(res3);
        }

        [Fact]
        public async Task BufferManager_PolicyOverride_BypassesLimitAndLogs()
        {
            var logger = new TestLogger<BufferManager>();
            var options = Options.Create(new BufferManagerOptions { MaxGlobalMemoryMB = 10 });
            var mockSys = new Mock<ISystemResources>();
            mockSys.Setup(r => r.GetAvailableMemoryBytes()).Returns(32L * 1024 * 1024 * 1024);
            var bm = new BufferManager(options, logger, mockSys.Object);

            // Request 20MB with override - should succeed immediately despite limit
            using var res = await bm.ReserveMemoryAsync("unsafe_session", 20 * 1024 * 1024, isOverride: true);
            
            Assert.Contains(logger.Logs, s => s.Contains("[POLICY_OVERRIDE]") && s.Contains("unsafe_session"));
        }

        [Fact]
        public async Task BufferManager_EnforcesSystemMemoryFloor()
        {
            var mockSys = new Mock<ISystemResources>();
            // Simulate 2GB available (below 4GB default floor)
            mockSys.Setup(r => r.GetAvailableMemoryBytes()).Returns(2L * 1024 * 1024 * 1024);

            var bm = CreateBufferManager(maxMemMB: 100, sysRes: mockSys.Object);

            // Request 1MB - should timeout even though engine has 100MB free, 
            // because system memory is below floor.
            await Assert.ThrowsAsync<TimeoutException>(() => 
                bm.ReserveMemoryAsync("session1", 1 * 1024 * 1024));
        }

        [Fact]
        public async Task BufferManager_EnforcesCursorLimit()
        {
            var bm = CreateBufferManager(maxCursors: 1);
            
            using var cur1 = await bm.AcquireCursorAsync("session1");
            
            // Second cursor should time out
            await Assert.ThrowsAsync<TimeoutException>(() => bm.AcquireCursorAsync("session2"));
        }
    }
}
