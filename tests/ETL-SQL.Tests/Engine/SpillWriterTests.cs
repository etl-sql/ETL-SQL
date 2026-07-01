using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Spill;
using ETL_SQL.Orchestrator.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Engine
{
    public class StubSpillWriter : ISpillWriter
    {
        public string ChunkName => "test";
        public long BytesWritten => 0;
        public Task WriteRowAsync(Row row) => Task.CompletedTask;
        public Task WriteRowsAsync(IEnumerable<Row> rows) => Task.CompletedTask;
        public ValueTask DisposeAsync() => default;
    }

    internal sealed class BlockingSpillWriter : ISpillWriter
    {
        private readonly TaskCompletionSource _releaseFirstWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _firstWriteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _writeCount;

        public string ChunkName => "blocking";
        public long BytesWritten => 0;
        public Task WriteRowAsync(Row row) => WriteRowsAsync(new[] { row });
        public Task WriteRowsAsync(IEnumerable<Row> rows)
        {
            if (Interlocked.Increment(ref _writeCount) != 1)
                return Task.CompletedTask;
            _firstWriteStarted.TrySetResult();
            return _releaseFirstWrite.Task;
        }
        public ValueTask DisposeAsync() => default;
        public Task FirstWriteStarted => _firstWriteStarted.Task;
        public void ReleaseFirstWrite() => _releaseFirstWrite.TrySetResult();
    }

    public class SpillWriterTests
    {
        [Fact]
        public async Task InMemoryDataSource_ByteGrantSpillsBeforeRowThreshold()
        {
            var arbiter = new MemoryGrantArbiter(totalBudgetBytes: 256);
            var spillStore = new Mock<ISpillStore>();
            spillStore.Setup(s => s.CreateWriterAsync(It.IsAny<string>())).ReturnsAsync(new StubSpillWriter());

            var telemetry = new Mock<ITelemetryContext>();
            telemetry.SetupGet(t => t.IsProfiling).Returns(false);

            var context = new Mock<IExecutionContext>();
            context.SetupGet(c => c.ServiceProvider).Returns(new ServiceCollection().BuildServiceProvider());
            context.SetupGet(c => c.SpillStore).Returns(spillStore.Object);
            context.SetupGet(c => c.Telemetry).Returns(telemetry.Object);
            context.SetupGet(c => c.TempTableSpillThresholdRows).Returns(1_000_000);
            context.SetupGet(c => c.MemoryArbiter).Returns(arbiter);

            var source = new InMemoryDataSource { ExecutionContext = context.Object };
            var table = new DataTable();
            table.SetColumns(new[] { "payload" });
            var row = table.NewRow();
            row["payload"] = new string('x', 100);
            table.Rows.Add(row);

            await source.WriteBatches(new[] { table }.ToAsyncEnumerable());

            Assert.Equal(1, source.SpillChunkCount);
            Assert.Equal(0, source.MemoryUsageBytes);
            Assert.Equal(0, arbiter.ReservedBytes);
        }

        [Fact]
        public async Task InMemoryDataSource_PressureSpillRotatesBoundedExtents()
        {
            var spillStore = new Mock<ISpillStore>();
            spillStore.Setup(s => s.CreateWriterAsync(It.IsAny<string>())).ReturnsAsync(() => new StubSpillWriter());
            var context = new Mock<IExecutionContext>();
            context.SetupGet(c => c.ServiceProvider).Returns(new ServiceCollection().BuildServiceProvider());
            context.SetupGet(c => c.SpillStore).Returns(spillStore.Object);
            context.SetupGet(c => c.Telemetry).Returns(new Mock<ITelemetryContext>().Object);
            context.SetupGet(c => c.TempTableSpillThresholdRows).Returns(1_000_000);
            context.SetupGet(c => c.MemoryArbiter).Returns(UnlimitedMemoryGrantArbiter.Instance);

            var source = new InMemoryDataSource
            {
                ExecutionContext = context.Object,
                SpillExtentTargetBytes = 1_500
            };
            var batches = Enumerable.Range(0, 5).Select(batchIndex =>
            {
                var table = new DataTable();
                table.SetColumns(new[] { "id" });
                for (var i = 0; i < 10; i++)
                {
                    var row = table.NewRow();
                    row["id"] = batchIndex * 10 + i;
                    table.Rows.Add(row);
                }
                return table;
            });
            await source.WriteBatches(batches.ToAsyncEnumerable());

            Assert.True(await source.SpillAsync());

            Assert.Equal(3, source.SpillChunkCount);
            spillStore.Verify(s => s.CreateWriterAsync(It.IsAny<string>()), Times.Exactly(3));
        }

        [Fact]
        public async Task InMemoryDataSource_MutationsRebaseRowsAndMemoryReservation()
        {
            var arbiter = new MemoryGrantArbiter(totalBudgetBytes: 1_000_000);
            var context = new Mock<IExecutionContext>();
            context.SetupGet(c => c.ServiceProvider).Returns(new ServiceCollection().BuildServiceProvider());
            context.SetupGet(c => c.Telemetry).Returns(new Mock<ITelemetryContext>().Object);
            context.SetupGet(c => c.TempTableSpillThresholdRows).Returns(1_000_000);
            context.SetupGet(c => c.MemoryArbiter).Returns(arbiter);

            var source = new InMemoryDataSource { ExecutionContext = context.Object };
            var table = new DataTable();
            table.SetColumns(new[] { "id", "value" });
            for (var i = 1; i <= 2; i++)
            {
                var row = table.NewRow();
                row["id"] = i;
                row["value"] = "short";
                table.Rows.Add(row);
            }
            await source.WriteBatches(new[] { table }.ToAsyncEnumerable());
            var initialBytes = source.MemoryUsageBytes;

            var deleted = await source.DeleteRows(row => Task.FromResult(Convert.ToInt32(row["id"]) == 1));

            Assert.Single(deleted);
            Assert.Equal(1, source.EstimatedRowCount);
            Assert.True(source.MemoryUsageBytes < initialBytes);
            Assert.Equal(source.MemoryUsageBytes, arbiter.ReservedBytes);

            await source.UpdateRows(
                _ => Task.FromResult(true),
                row =>
                {
                    row["value"] = new string('x', 1_000);
                    return Task.CompletedTask;
                });

            Assert.True(source.MemoryUsageBytes > initialBytes);
            Assert.Equal(source.MemoryUsageBytes, arbiter.ReservedBytes);
        }

        [Fact]
        public async Task InMemoryDataSource_UniqueKeysSurviveSpillAndTruncatePreservesDefinition()
        {
            var spillStore = new Mock<ISpillStore>();
            spillStore.Setup(s => s.CreateWriterAsync(It.IsAny<string>())).ReturnsAsync(() => new StubSpillWriter());
            var context = new Mock<IExecutionContext>();
            context.SetupGet(c => c.ServiceProvider).Returns(new ServiceCollection().BuildServiceProvider());
            context.SetupGet(c => c.SpillStore).Returns(spillStore.Object);
            context.SetupGet(c => c.Telemetry).Returns(new Mock<ITelemetryContext>().Object);
            context.SetupGet(c => c.TempTableSpillThresholdRows).Returns(0);
            context.SetupGet(c => c.MemoryArbiter).Returns(new MemoryGrantArbiter(1_000_000));

            var source = new InMemoryDataSource { ExecutionContext = context.Object };
            source.SetSchema(new[] { new ColumnDefinition("id", "INT", false) { IsPrimaryKey = true } });

            await source.WriteBatches(new[] { Batch(1) }.ToAsyncEnumerable());
            await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(() =>
                source.WriteBatches(new[] { Batch(1) }.ToAsyncEnumerable(), append: true));

            await source.TruncateAsync();
            Assert.True(source.HasIndex("id"));
            await source.WriteBatches(new[] { Batch(1) }.ToAsyncEnumerable(), append: true);

            static DataTable Batch(int id)
            {
                var table = new DataTable();
                table.SetColumns(new[] { "id" });
                var row = table.NewRow();
                row["id"] = id;
                table.Rows.Add(row);
                return table;
            }
        }

        [Fact]
        public async Task InMemoryDataSource_SpillPipeline_ProducesNextBatchWhileWritingCurrentBatch()
        {
            var writer = new BlockingSpillWriter();
            var spillStore = new Mock<ISpillStore>();
            spillStore.Setup(s => s.CreateWriterAsync(It.IsAny<string>())).ReturnsAsync(writer);

            var telemetry = new Mock<ITelemetryContext>();
            telemetry.SetupGet(t => t.IsProfiling).Returns(false);

            var services = new ServiceCollection().BuildServiceProvider();
            var context = new Mock<IExecutionContext>();
            context.SetupGet(c => c.ServiceProvider).Returns(services);
            context.SetupGet(c => c.SpillStore).Returns(spillStore.Object);
            context.SetupGet(c => c.Telemetry).Returns(telemetry.Object);
            context.SetupGet(c => c.TempTableSpillThresholdRows).Returns(0);

            var secondBatchProduced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var source = new InMemoryDataSource
            {
                ExecutionContext = context.Object,
                SpillExtentTargetBytes = 1
            };

            async IAsyncEnumerable<DataTable> Batches()
            {
                yield return CreateBatch(1);
                secondBatchProduced.TrySetResult();
                yield return CreateBatch(2);
                await Task.CompletedTask;
            }

            var writeTask = source.WriteBatches(Batches());
            await secondBatchProduced.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(writeTask.IsCompleted);

            writer.ReleaseFirstWrite();
            await writeTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, source.SpillChunkCount);

            static DataTable CreateBatch(int value)
            {
                var table = new DataTable();
                table.SetColumns(new[] { "id" });
                var row = table.NewRow();
                row["id"] = value;
                table.Rows.Add(row);
                return table;
            }
        }

        [Fact]
        public async Task InMemoryDataSource_SpillPipeline_CancellationDeletesIncompleteExtent()
        {
            using var cancellation = new CancellationTokenSource();
            var writer = new BlockingSpillWriter();
            var spillStore = new Mock<ISpillStore>();
            spillStore.Setup(s => s.CreateWriterAsync(It.IsAny<string>())).ReturnsAsync(writer);

            var telemetry = new Mock<ITelemetryContext>();
            telemetry.SetupGet(t => t.IsProfiling).Returns(false);

            var context = new Mock<IExecutionContext>();
            context.SetupGet(c => c.ServiceProvider).Returns(new ServiceCollection().BuildServiceProvider());
            context.SetupGet(c => c.SpillStore).Returns(spillStore.Object);
            context.SetupGet(c => c.Telemetry).Returns(telemetry.Object);
            context.SetupGet(c => c.TempTableSpillThresholdRows).Returns(0);
            context.SetupGet(c => c.CancellationToken).Returns(() => cancellation.Token);

            var source = new InMemoryDataSource { ExecutionContext = context.Object };
            var table = new DataTable();
            table.SetColumns(new[] { "id" });
            var row = table.NewRow();
            row["id"] = 1;
            table.Rows.Add(row);

            var writeTask = source.WriteBatches(new[] { table }.ToAsyncEnumerable());
            await writer.FirstWriteStarted.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            writer.ReleaseFirstWrite();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writeTask);
            Assert.Equal(0, source.SpillChunkCount);
            spillStore.Verify(s => s.DeleteChunk(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task BufferManager_TriggersGlobalSpillUnderPressure()
        {
            // 1. Setup
            var services = new ServiceCollection();
            var options = new BufferManagerOptions
            {
                MaxGlobalMemoryMB = 10, // 10MB limit
                ResourceWaitTimeoutSeconds = 5
            };

            var mockSys = new Mock<ISystemResources>();
            mockSys.Setup(s => s.GetAvailableMemoryBytes()).Returns(1024 * 1024 * 1024); // 1GB available (above floor)

            var mockSpill = new Mock<ISpillStore>();
            mockSpill.Setup(s => s.CreateWriterAsync(It.IsAny<string>())).ReturnsAsync(new StubSpillWriter());

            var mockLogger = new Mock<Microsoft.Extensions.Logging.ILogger<BufferManager>>();
            mockLogger.Setup(x => x.Log(
                It.IsAny<Microsoft.Extensions.Logging.LogLevel>(),
                It.IsAny<Microsoft.Extensions.Logging.EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
                .Callback(new InvocationAction(invocation =>
                {
                    var ex = invocation.Arguments[3] as Exception;
                    if (ex != null) Console.WriteLine($"ERROR: {ex}");
                }));

            var bufferManager = new BufferManager(Options.Create(options), mockLogger.Object, mockSys.Object);

            var mockServiceProvider = new Mock<IServiceProvider>();
            mockServiceProvider.Setup(s => s.GetService(typeof(IBufferManager))).Returns(bufferManager);

            var mockContextLogger = new Mock<ETL_SQL.Common.ILogger>();
            mockContextLogger.Setup(l => l.Error(It.IsAny<string>(), It.IsAny<Exception>(), It.IsAny<object[]>()))
                             .Callback<string, Exception, object[]>((msg, ex, args) => Console.WriteLine($"ERROR: {msg} {ex}"));

            var mockContext = new Mock<IExecutionContext>();
            mockContext.Setup(c => c.ServiceProvider).Returns(mockServiceProvider.Object);
            mockContext.Setup(c => c.SpillStore).Returns(mockSpill.Object);
            mockContext.Setup(c => c.Logger).Returns(mockContextLogger.Object);
            mockContext.Setup(c => c.TempTableSpillThresholdRows).Returns(100_000);

            // 2. Create a spillable data source that holds 8MB
            var ds = new InMemoryDataSource();
            ds.ExecutionContext = mockContext.Object; // Registers with BufferManager

            // Add some data to make it 'large'
            var table = new DataTable();
            table.SetColumns(new[] { "col1" });

            for (int i = 0; i < 20000; i++)
            {
                var row = new Row(table.Schema);
                row["col1"] = new string('x', 512); // approx 0.5KB per row
                table.Rows.Add(row);
            }
            // Approx 10MB of data (20000 * 512 = 10.24MB)
            async IAsyncEnumerable<DataTable> GetBatches()
            {
                yield return table;
                await Task.CompletedTask;
            }
            await ds.WriteBatches(GetBatches());

            var initialUsage = ds.MemoryUsageBytes;
            Assert.True(initialUsage > 4 * 1024 * 1024, $"Initial usage should be > 4MB, got {initialUsage}");

            // 3. Request more memory to trigger pressure
            try
            {
                long reclaimed = await bufferManager.TriggerSpillsUnderPressureAsync(11 * 1024 * 1024);

                Assert.Equal(0, ds.MemoryUsageBytes);
            }
            catch (Exception ex)
            {
                Assert.Fail($"TriggerSpills threw an exception: {ex}");
            }
        }
    }
}
