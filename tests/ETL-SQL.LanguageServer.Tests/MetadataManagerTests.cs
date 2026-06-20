using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Interfaces;
using ETL_SQL.Core.Services;
using ETL_SQL.Data;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ETL_SQL.LanguageServer.Tests
{
    public class MetadataManagerTests
    {
        private readonly Mock<ETL_SQL.Common.ILogger> _loggerMock;
        private readonly Mock<IConnectorRegistry> _registryMock;
        private readonly MetadataManager _manager;

        public MetadataManagerTests()
        {
            _loggerMock = new Mock<ETL_SQL.Common.ILogger>();
            _registryMock = new Mock<IConnectorRegistry>();
            _manager = new MetadataManager(_loggerMock.Object, _registryMock.Object)
            {
                SchemaCacheDirectory = null,
                DisableBackgroundRefresh = true
            };
        }

        [Fact]
        public void RegisterConnection_AddsToGlobalConnections()
        {
            // Arrange
            string name = "MyConn";
            string type = "MSSQL";
            string connStr = "Server=myServer;Database=myDb;";

            // Act
            _manager.RegisterConnection(name, type, connStr);
            var connections = _manager.GetConnections();

            // Assert
            Assert.Contains(connections, c => c.Name == name && c.Type == type && !c.IsDocument);
        }

        [Fact]
        public void RegisterDocumentConnection_IsolatesToDoc()
        {
            // Arrange
            string uri1 = "file:///doc1.etlsql";
            string uri2 = "file:///doc2.etlsql";
            _manager.RegisterDocumentConnection(uri1, "DOC_CONN", "CSV", "path1");

            // Act
            var conn1 = _manager.GetConnections(uri1);
            var conn2 = _manager.GetConnections(uri2);

            // Assert
            Assert.Contains(conn1, c => c.Name == "DOC_CONN");
            Assert.DoesNotContain(conn2, c => c.Name == "DOC_CONN");
        }

        [Fact]
        public async Task GetTablesAsync_UsesConnectorRegistry()
        {
            // Arrange
            string connName = "MyConn";
            string connType = "MSSQL";
            string connStr = "...";
            _manager.RegisterConnection(connName, connType, connStr);

            var dataSourceMock = new Mock<IDataSource>();
            dataSourceMock.Setup(d => d.GetTablesAsync()).ReturnsAsync(new List<string> { "Table1", "Table2" });

            var connectorMock = new Mock<IConnector>();
            connectorMock.Setup(c => c.CreateDataSource(It.IsAny<IExecutionContext>(), connStr, It.IsAny<Dictionary<string, string>>()))
                         .Returns(dataSourceMock.Object);

            _registryMock.Setup(r => r.GetConnector(connType)).Returns(connectorMock.Object);

            // Act
            var tables = await _manager.GetTablesAsync(connName);

            // Assert
            Assert.Equal(3, tables.Count()); // 2 from mock + 1 virtual DUAL table
            Assert.Contains("Table1", tables);
            Assert.Contains("DUAL", tables);
            _registryMock.Verify(r => r.GetConnector(connType), Times.Once);
            connectorMock.Verify(c => c.CreateDataSource(It.IsAny<IExecutionContext>(), connStr, It.IsAny<Dictionary<string, string>>()), Times.Once);
        }

        [Fact]
        public async Task GetTablesAsync_CachesResults()
        {
            // Arrange
            string connName = "MyConn";
            _manager.RegisterConnection(connName, "MSSQL", "...");

            var dataSourceMock = new Mock<IDataSource>();
            dataSourceMock.Setup(d => d.GetTablesAsync()).ReturnsAsync(new List<string> { "T1" });

            var connectorMock = new Mock<IConnector>();
            connectorMock.Setup(c => c.CreateDataSource(It.IsAny<IExecutionContext>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                         .Returns(dataSourceMock.Object);

            _registryMock.Setup(r => r.GetConnector(It.IsAny<string>())).Returns(connectorMock.Object);

            // Act
            await _manager.GetTablesAsync(connName);
            await _manager.GetTablesAsync(connName);

            // Assert
            connectorMock.Verify(c => c.CreateDataSource(It.IsAny<IExecutionContext>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Once);
        }

        [Fact]
        public void ClearDocumentConnections_RemovesOnlySpecificDoc()
        {
            // Arrange
            _manager.RegisterDocumentConnection("doc1", "C1", "T", "S");
            _manager.RegisterDocumentConnection("doc2", "C2", "T", "S");

            // Act
            _manager.ClearDocumentConnections("doc1");

            // Assert
            Assert.DoesNotContain(_manager.GetConnections("doc1"), c => c.IsDocument);
            Assert.Single(_manager.GetConnections("doc2"), c => c.IsDocument);
        }

        [Fact]
        public async Task RegisterDocumentConnection_ClearsCacheForThatDocument()
        {
            // Arrange
            string uri = "file:///doc1.etlsql";
            string connName = "DOC_CONN";
            _manager.RegisterDocumentConnection(uri, connName, "MSSQL", "...");

            var dataSourceMock = new Mock<IDataSource>();
            dataSourceMock.Setup(d => d.GetTablesAsync()).ReturnsAsync(new List<string> { "T1" });

            var connectorMock = new Mock<IConnector>();
            connectorMock.Setup(c => c.CreateDataSource(It.IsAny<IExecutionContext>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                         .Returns(dataSourceMock.Object);

            _registryMock.Setup(r => r.GetConnector(It.IsAny<string>())).Returns(connectorMock.Object);

            // Cache it
            await _manager.GetTablesAsync(connName, uri);

            // Act - Register again (simulating change)
            _manager.RegisterDocumentConnection(uri, connName, "MSSQL", "NEW_STR");
            await _manager.GetTablesAsync(connName, uri);

            // Assert
            connectorMock.Verify(c => c.CreateDataSource(It.IsAny<IExecutionContext>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Exactly(2));
        }

        [Fact]
        public async Task SchemaCache_WritesEncryptedDataToDisk_AndLoadsItSuccessfully()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try
            {
                var manager = new MetadataManager(_loggerMock.Object, _registryMock.Object)
                {
                    SchemaCacheDirectory = tempDir,
                    DisableBackgroundRefresh = true
                };

                string connName = "CacheTestConn";
                string connType = "MSSQL";
                string connStr = "Server=myServer;Database=myDb;";
                manager.RegisterConnection(connName, connType, connStr);

                var dataSourceMock = new Mock<IDataSource>();
                dataSourceMock.Setup(d => d.GetTablesAsync()).ReturnsAsync(new List<string> { "CachedTable1", "CachedTable2" });

                var connectorMock = new Mock<IConnector>();
                connectorMock.Setup(c => c.CreateDataSource(It.IsAny<IExecutionContext>(), connStr, It.IsAny<Dictionary<string, string>>()))
                             .Returns(dataSourceMock.Object);

                _registryMock.Setup(r => r.GetConnector(connType)).Returns(connectorMock.Object);

                // Act - Invoke RefreshSchemaInternalAsync via reflection to write the cache to disk
                var refreshMethod = typeof(MetadataManager).GetMethod("RefreshSchemaInternalAsync", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.NotNull(refreshMethod);
                await (Task)refreshMethod.Invoke(manager, new object[] { connName, connStr, null });

                // Assert 1: A cache file exists in the directory
                var files = Directory.GetFiles(tempDir);
                Assert.Single(files);
                var filePath = files[0];
                Assert.EndsWith(".cache", filePath);

                // Assert 2: The cache file is encrypted (is not plaintext JSON containing the table names)
                var fileBytes = await File.ReadAllBytesAsync(filePath);
                var fileContent = System.Text.Encoding.UTF8.GetString(fileBytes);
                Assert.DoesNotContain("CachedTable1", fileContent);

                // Assert 3: We can load the disk cache using LoadSchemaFromDisk
                var loadMethod = typeof(MetadataManager).GetMethod("LoadSchemaFromDisk", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.NotNull(loadMethod);
                var loadedCache = (ConnectionSchemaCache)loadMethod.Invoke(manager, new object[] { connName, connStr });
                Assert.NotNull(loadedCache);

                Assert.Contains("CachedTable1", loadedCache.Tables);
                Assert.Contains("CachedTable2", loadedCache.Tables);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }
    }
}
