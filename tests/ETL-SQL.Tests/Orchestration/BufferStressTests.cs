using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Execution;
using ETL_SQL.Orchestrator.Execution;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    public class BufferStressTests
    {
        private class NoOpLogger<T> : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => false;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
        }

        private BufferManager CreateBufferManager(int maxMemMB = 1024)
        {
            var options = Options.Create(new BufferManagerOptions
            {
                MaxGlobalMemoryMB = maxMemMB,
                MaxStreamingCursors = 100,
                ResourceWaitTimeoutSeconds = 30,
                HysteresisMemoryMB = 100,
                SystemMemoryFloorMB = 1024 // 1GB floor
            });

            var mock = new Mock<ISystemResources>();
            mock.Setup(r => r.GetAvailableMemoryBytes()).Returns(32L * 1024 * 1024 * 1024); // 32GB available

            return new BufferManager(options, new NoOpLogger<BufferManager>(), mock.Object);
        }

        [Fact]
        public async Task BufferManager_Stress_HighConcurrency_FastPath()
        {
            // Max 1024MB. 
            // We'll run 100 threads, each doing 1000 small reservations (10KB each).
            // Total volume: 100 * 1000 * 10KB = 100 * 10MB = 1000MB.
            // This is just under the limit, so mostly it hits the FAST PATH.
            var bm = CreateBufferManager(maxMemMB: 1024);
            int threadCount = 64;
            int iterationsPerThread = 1000;
            long reservationSize = 10 * 1024; // 10KB

            var tasks = new List<Task>();
            var sw = Stopwatch.StartNew();

            for (int t = 0; t < threadCount; t++)
            {
                int tid = t;
                tasks.Add(Task.Run(async () =>
                {
                    for (int i = 0; i < iterationsPerThread; i++)
                    {
                        using (await bm.ReserveMemoryAsync($"session_{tid}_{i}", reservationSize))
                        {
                            // Simulate small work
                            await Task.Yield();
                        }
                    }
                }));
            }

            await Task.WhenAll(tasks);
            sw.Stop();

            // Verification: Ensure memory was released correctly and there were no deadlocks
            // Accessing internal state if possible, or just checking after WhenAll
            // Check if any requests timed out (they would throw Exception and fail Task.WhenAll)

            Console.WriteLine($"Stress Test Complete: {threadCount * iterationsPerThread} operations in {sw.ElapsedMilliseconds}ms ({(threadCount * iterationsPerThread) / (sw.Elapsed.TotalSeconds):F0} ops/sec)");
        }

        [Fact]
        public async Task BufferManager_Stress_Limit_Contention()
        {
            // Max 10MB.
            // 20 threads each trying to take 1MB.
            // Total 20MB. This will force heavy queuing and lock usage (slow path).
            var bm = CreateBufferManager(maxMemMB: 10);
            int threadCount = 20;
            long reservationSize = 1024 * 1024; // 1MB

            var tasks = new List<Task>();
            var sw = Stopwatch.StartNew();

            for (int t = 0; t < threadCount; t++)
            {
                int tid = t;
                tasks.Add(Task.Run(async () =>
                {
                    // Every session tries to take 1MB
                    using (await bm.ReserveMemoryAsync($"slow_session_{tid}", reservationSize))
                    {
                        await Task.Delay(100); // Hold it for a bit to force queuing
                    }
                }));
            }

            await Task.WhenAll(tasks);
            sw.Stop();

            // If we didn't timeout, it means the queue was processed correctly despite contention.
        }
    }
}
