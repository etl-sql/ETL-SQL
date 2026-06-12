using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

    public class SpillWriterTests
    {
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
