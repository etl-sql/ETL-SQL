using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core.Execution;
using ETL_SQL.Orchestrator.Execution;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

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
            // Simulate 256MB available (below default 512MB floor)
            mockSys.Setup(r => r.GetAvailableMemoryBytes()).Returns(256L * 1024 * 1024);

            var bm = CreateBufferManager(maxMemMB: 100, sysRes: mockSys.Object);

            // Request 1MB - should timeout even though engine has 100MB free,
            // because system memory is below default 512MB floor.
            await Assert.ThrowsAsync<TimeoutException>(() =>
                bm.ReserveMemoryAsync("session1", 1 * 1024 * 1024));

            // Explicit custom floor also remains honored.
            var mockExplicitSys = new Mock<ISystemResources>();
            mockExplicitSys.Setup(r => r.GetAvailableMemoryBytes()).Returns(2L * 1024 * 1024 * 1024);
            var explicitOptions = Options.Create(new BufferManagerOptions
            {
                MaxGlobalMemoryMB = 100,
                MaxStreamingCursors = 2,
                ResourceWaitTimeoutSeconds = 1,
                HysteresisMemoryMB = 0,
                SystemMemoryFloorMB = 4096
            });
            using var explicitBm = new BufferManager(explicitOptions, new TestLogger<BufferManager>(), mockExplicitSys.Object);
            await Assert.ThrowsAsync<TimeoutException>(() =>
                explicitBm.ReserveMemoryAsync("session2", 1 * 1024 * 1024));
        }

        [Fact]
        public async Task BufferManager_EnforcesCursorLimit()
        {
            var bm = CreateBufferManager(maxCursors: 1);

            using var cur1 = await bm.AcquireCursorAsync("session1");

            // Second cursor should time out
            await Assert.ThrowsAsync<TimeoutException>(() => bm.AcquireCursorAsync("session2"));
        }

        [Fact]
        public async Task BufferManager_AbandonedCursorTimeoutAndCancel_LeaveFullCapacityReusable()
        {
            using var bm = CreateBufferManager(maxCursors: 1, timeoutSec: 1);

            var cur1 = await bm.AcquireCursorAsync("holder");
            await Assert.ThrowsAsync<TimeoutException>(() => bm.AcquireCursorAsync("timed_out"));

            using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    bm.AcquireCursorAsync("cancelled", cts.Token));
            }

            // Releasing the original cursor must NOT grant a permit to the abandoned waiters.
            cur1.Dispose();

            using var cur2 = await bm.AcquireCursorAsync("fresh");
            Assert.NotNull(cur2);
        }

        [Fact]
        public async Task BufferManager_AbandonedMemoryTimeoutAndCancel_DoNotLeakBudgetOrStallQueue()
        {
            using var bm = CreateBufferManager(maxMemMB: 10, timeoutSec: 1);

            var res1 = await bm.ReserveMemoryAsync("holder", 8L * 1024 * 1024);

            // Queue one waiter that times out and one that cancels.
            await Assert.ThrowsAsync<TimeoutException>(() =>
                bm.ReserveMemoryAsync("timed_out", 6L * 1024 * 1024));

            using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    bm.ReserveMemoryAsync("cancelled", 6L * 1024 * 1024, cts.Token));
            }

            // Releasing res1 must reclaim the entire 10 MB budget and skip abandoned requests.
            res1.Dispose();

            using var full = await bm.ReserveMemoryAsync("fresh_full", 10L * 1024 * 1024);
            Assert.NotNull(full);
        }

        [Fact]
        public async Task BufferManager_ConcurrentGrantTimeoutRace_NeverLeaksMemoryOrPermits()
        {
            using var bm = CreateBufferManager(maxMemMB: 10, maxCursors: 1, timeoutSec: 1);

            for (var round = 0; round < 20; round++)
            {
                var memHolder = await bm.ReserveMemoryAsync($"m_hold_{round}", 10L * 1024 * 1024);
                var cursorHolder = await bm.AcquireCursorAsync($"c_hold_{round}");

                using var memCts = new System.Threading.CancellationTokenSource();
                using var curCts = new System.Threading.CancellationTokenSource();

                var memWaiter = bm.ReserveMemoryAsync($"m_race_{round}", 5L * 1024 * 1024, memCts.Token);
                var curWaiter = bm.AcquireCursorAsync($"c_race_{round}", curCts.Token);

                // Race cancellation directly against concurrent releases across rounds.
                var releaseTask = Task.Run(() =>
                {
                    if (round % 2 == 0) Thread.Yield();
                    memHolder.Dispose();
                    cursorHolder.Dispose();
                });
                var cancelTask = Task.Run(() =>
                {
                    if (round % 2 == 1) Thread.Yield();
                    memCts.Cancel();
                    curCts.Cancel();
                });

                await Task.WhenAll(releaseTask, cancelTask);

                try
                {
                    using var won = await memWaiter;
                }
                catch (OperationCanceledException)
                {
                }

                try
                {
                    using var won = await curWaiter;
                }
                catch (OperationCanceledException)
                {
                }

                // After every round, full memory (10 MB) and full cursor capacity (1) must be available.
                using var checkMem = await bm.ReserveMemoryAsync($"m_verify_{round}", 10L * 1024 * 1024);
                using var checkCur = await bm.AcquireCursorAsync($"c_verify_{round}");
            }
        }

        [Fact]
        public async Task BufferManager_WaitFailure_PropagatesOriginalException()
        {
            using var bm = CreateBufferManager(timeoutSec: 5);
            var faultedWait = Task.FromException(new InvalidOperationException("wait boom"));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                bm.WaitWithFeedbackForTestAsync("faulted_session", "Memory", faultedWait));
            Assert.Equal("wait boom", ex.Message);
        }
    }
}
