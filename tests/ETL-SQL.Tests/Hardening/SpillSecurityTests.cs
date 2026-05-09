using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Core.Spill;
using ETL_SQL.Engine.Spill;

namespace ETL_SQL.Tests.Hardening
{
    public class SpillSecurityTests
    {
        [Fact]
        [Trait("Category", "Smoke.Security")]
        public async Task SpillStore_EncryptedData_IsUnreadableAsJson()
        {
            // Arrange
            var mockContext = new Mock<IExecutionContext>();
            mockContext.SetupProperty(c => c.SpillEncryptionEnabled, true);
            mockContext.SetupProperty(c => c.SpillCompressionEnabled, true);
            mockContext.Setup(c => c.SessionStateManager).Returns(new global::ETL_SQL.Core.Execution.NullSessionStateManager());
            mockContext.Setup(c => c.SessionRoot).Returns(Path.GetTempPath());

            using var store = new SpillStore(mockContext.Object);
            string chunkName = "test_data.tmp";
            var rows = new List<Row> { new Row { ["id"] = 1, ["name"] = "Secret" } };

            // Act: Write encrypted
            await using (var writer = await store.CreateWriterAsync(chunkName))
            {
                await writer.WriteRowsAsync(rows);
            }

            // Assert: Verify file exists and is garbage (encrypted/compressed)
            var rootPath = typeof(SpillStore).GetField("_cachedRootPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(store) as string;
            var filePath = Path.Combine(rootPath!, chunkName);
            
            Assert.True(File.Exists(filePath));
            var rawBytes = await File.ReadAllBytesAsync(filePath);
            
            // Should be at least 16 bytes (IV) + some encrypted/compressed data
            Assert.True(rawBytes.Length > 16);

            // Attempting to parse as JSON should fail miserably
            Assert.ThrowsAny<Exception>(() => System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(rawBytes));

            // Act: Read back via Store
            await using (var reader = await store.CreateReaderAsync(chunkName))
            {
                var readRows = await reader.AsEnumerableAsync().ToListAsync();
                Assert.Single(readRows);
                Assert.Equal(1m, Convert.ToDecimal(readRows[0]["id"]));
                Assert.Equal("Secret", readRows[0]["name"]);
            }
        }

        [Fact]
        public async Task SpillStore_NoEncryption_IsStillCompressed()
        {
            // Arrange
            var mockContext = new Mock<IExecutionContext>();
            mockContext.SetupProperty(c => c.SpillEncryptionEnabled, false);
            mockContext.SetupProperty(c => c.SpillCompressionEnabled, true);

            using var store = new SpillStore(mockContext.Object);
            string chunkName = "plain_compressed.tmp";
            var rows = new List<Row> { new Row { ["val"] = "CompressedButPlain" } };

            // Act
            await using (var writer = await store.CreateWriterAsync(chunkName))
            {
                await writer.WriteRowAsync(rows[0]);
            }

            // Assert: Read back
            await using (var reader = await store.CreateReaderAsync(chunkName))
            {
                var readRows = await reader.AsEnumerableAsync().ToListAsync();
                Assert.Single(readRows);
                Assert.Equal("CompressedButPlain", readRows[0]["val"]);
            }
        }

        [Fact]
        public async Task SpillStore_Cleanup_DeletesDirectory()
        {
            // Arrange
            var mockContext = new Mock<IExecutionContext>();
            string rootPath;
            mockContext.Setup(c => c.SessionRoot).Returns(Path.GetTempPath());
            mockContext.Setup(c => c.SessionStateManager).Returns(new global::ETL_SQL.Core.Execution.NullSessionStateManager());
            
            using (var store = new SpillStore(mockContext.Object))
            {
                // Trigger initialization
                rootPath = store.RootPath;
                Assert.True(Directory.Exists(rootPath));

                await using (var writer = await store.CreateWriterAsync("data.tmp"))
                {
                    await writer.WriteRowAsync(new Row { ["x"] = 1 });
                }
            }

            // Assert: Directory should be gone after disposal
            Assert.False(Directory.Exists(rootPath));
        }
    }
}
