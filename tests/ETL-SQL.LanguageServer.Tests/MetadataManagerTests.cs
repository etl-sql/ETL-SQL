using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Interfaces;
using ETL_SQL.Core.Services;
using ETL_SQL.Data;
using ETL_SQL.TestSupport;
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
        public async Task RegisterDocumentMetadata_ServesSchemaWithoutRetainingConnectionString()
        {
            var connectorMock = new Mock<IConnector>();
            _registryMock.Setup(r => r.GetConnector(It.IsAny<string>())).Returns(connectorMock.Object);

            var uri = "portal-designer://u/1/c/Sales/default";
            _manager.RegisterDocumentMetadata(
                uri,
                "Sales",
                "MSSQL",
                new[] { "Orders" },
                new Dictionary<string, IEnumerable<ColumnMetadata>>
                {
                    ["Orders"] = new[] { new ColumnMetadata("OrderId", "INT") }
                });

            var connections = _manager.GetConnections(uri);
            var connection = Assert.Single(connections, c => c.Name == "Sales");
            Assert.True(connection.IsMetadataOnly);
            Assert.Equal(string.Empty, connection.ConnectionString);

            var tables = await _manager.GetTablesAsync("Sales", uri);
            var columns = await _manager.GetColumnDetailsAsync("Sales", "Orders", uri);

            Assert.Contains("Orders", tables);
            Assert.Contains("DUAL", tables);
            Assert.Contains(columns, c => c.Name == "OrderId" && c.DataType == "INT");
            connectorMock.Verify(
                c => c.CreateDataSource(It.IsAny<IExecutionContext>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()),
                Times.Never);
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
        public async Task RegisterConnection_DoesNotExposeConnectionStringButUsesHandleForDiscovery()
        {
            string connName = "SecretConn";
            string connType = "MSSQL";
            string connStr = "Server=myServer;User Id=etl;Password=plain;";
            _manager.RegisterConnection(connName, connType, connStr);

            var exposed = Assert.Single(_manager.GetConnections(), c => c.Name == connName);
            Assert.Equal(string.Empty, exposed.ConnectionString);
            Assert.False(string.IsNullOrWhiteSpace(exposed.SecretHandle));

            var dataSourceMock = new Mock<IDataSource>();
            dataSourceMock.Setup(d => d.GetTablesAsync()).ReturnsAsync(new List<string> { "T1" });

            var connectorMock = new Mock<IConnector>();
            connectorMock.Setup(c => c.CreateDataSource(It.IsAny<IExecutionContext>(), connStr, It.IsAny<Dictionary<string, string>>()))
                         .Returns(dataSourceMock.Object);
            _registryMock.Setup(r => r.GetConnector(connType)).Returns(connectorMock.Object);

            var tables = await _manager.GetTablesAsync(connName);

            Assert.Contains("T1", tables);
            connectorMock.Verify(c => c.CreateDataSource(It.IsAny<IExecutionContext>(), connStr, It.IsAny<Dictionary<string, string>>()), Times.Once);
        }

        [Fact]
        public void RegisterDocumentConnection_PrunesOldestDocumentContextWhenBounded()
        {
            var manager = new MetadataManager(_loggerMock.Object, _registryMock.Object)
            {
                SchemaCacheDirectory = null,
                DisableBackgroundRefresh = true,
                MaxDocumentContexts = 1
            };

            manager.RegisterDocumentConnection("file:///doc1.etlsql", "C1", "MSSQL", "Server=one;Password=plain;");
            manager.RegisterDocumentConnection("file:///doc2.etlsql", "C2", "MSSQL", "Server=two;Password=plain;");

            Assert.DoesNotContain(manager.GetConnections("file:///doc1.etlsql"), c => c.Name == "C1");
            var remaining = Assert.Single(manager.GetConnections("file:///doc2.etlsql"), c => c.Name == "C2");
            Assert.Equal(string.Empty, remaining.ConnectionString);
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
        public async Task ValidCacheHit_TriggersBackgroundRefresh_WhenStale_AndReleasesSlot()
        {
            // Stale-while-revalidate (and slot-release): a valid in-memory hit must still kick off a
            // background refresh once aged. That refresh only re-runs if the previous one released its
            // _ongoingRefreshes slot — so a second source query proves both fixes.
            var tableFetches = 0;
            var dataSourceMock = new Mock<IDataSource>();
            dataSourceMock.Setup(d => d.GetTablesAsync())
                          .Callback(() => Interlocked.Increment(ref tableFetches))
                          .ReturnsAsync(new List<string> { "T1" });
            dataSourceMock.Setup(d => d.GetColumnsAsync()).ReturnsAsync(new List<string>());
            // GetCatalogProvider() returns null by default (loose mock) → exercises the GetColumns fallback.

            var connectorMock = new Mock<IConnector>();
            connectorMock.Setup(c => c.CreateDataSource(It.IsAny<IExecutionContext>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                         .Returns(dataSourceMock.Object);
            _registryMock.Setup(r => r.GetConnector(It.IsAny<string>())).Returns(connectorMock.Object);

            var manager = new MetadataManager(_loggerMock.Object, _registryMock.Object)
            {
                SchemaCacheDirectory = null,
                DisableBackgroundRefresh = false,
                SoftRefreshInterval = TimeSpan.Zero // every read is "stale" → always revalidate
            };

            manager.RegisterConnection("C", "MSSQL", "...");      // warms + first background refresh
            await WaitUntilAsync(() => tableFetches >= 1);

            var before = tableFetches;
            await manager.GetTablesAsync("C");                    // valid hit → second background refresh
            await WaitUntilAsync(() => tableFetches > before);

            Assert.True(tableFetches > before, "A stale valid cache-hit should trigger another background refresh.");
        }

        private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
        {
            await LoadAwareWait.UntilAsync(
                "language-server metadata refresh condition",
                _ => Task.FromResult(condition()),
                observed => observed,
                TimeSpan.FromMilliseconds(timeoutMs),
                TimeSpan.FromMilliseconds(20),
                observed => $"condition={observed}");
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

                // Assert 3: We can load the disk cache using LoadSchemaFromDiskAsync
                var loadMethod = typeof(MetadataManager).GetMethod("LoadSchemaFromDiskAsync",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.NotNull(loadMethod);
                var loadedCache = await (Task<ConnectionSchemaCache>)loadMethod.Invoke(manager, new object[] { connName, connStr })!;
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

        [Fact]
        public async Task VirtualConnection_Eng_ReturnsCorrectMetadata()
        {
            // Act & Assert 1: GetConnections includes "eng"
            var conns = _manager.GetConnections();
            Assert.Contains(conns, c => c.Name.Equals("eng", StringComparison.OrdinalIgnoreCase) && c.Type.Equals("ENG", StringComparison.OrdinalIgnoreCase));

            // Act & Assert 2: GetConnectionType for "eng" returns "ENG"
            Assert.Equal("ENG", _manager.GetConnectionType("eng"));

            // Act & Assert 3: GetTablesAsync for "eng" returns virtual tables
            var tables = (await _manager.GetTablesAsync("eng")).ToList();
            Assert.Contains("connections", tables);
            Assert.Contains("tables", tables);
            Assert.Contains("columns", tables);
            Assert.Contains("tags", tables);

            // Act & Assert 4: GetViewsAsync for "eng" is empty
            var views = await _manager.GetViewsAsync("eng");
            Assert.Empty(views);

            // Act & Assert 5: GetColumnsAsync for eng.connections
            var cols = (await _manager.GetColumnsAsync("eng", "connections")).ToList();
            Assert.Contains("connection_name", cols);
            Assert.Contains("connector_type", cols);
            Assert.Contains("details", cols);
        }
    }
}
