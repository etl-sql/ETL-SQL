using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using ETL_SQL.LSP;
using ETL_SQL.Data;

namespace ETL_SQL.LanguageServer.Tests
{
    public class MetadataManagerTests
    {
        private readonly Mock<ILogger<MetadataManager>> _loggerMock;
        private readonly Mock<IConnectorRegistry> _registryMock;
        private readonly MetadataManager _manager;

        public MetadataManagerTests()
        {
            _loggerMock = new Mock<ILogger<MetadataManager>>();
            _registryMock = new Mock<IConnectorRegistry>();
            _manager = new MetadataManager(_loggerMock.Object, _registryMock.Object);
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

            var connectorMock = new Mock<IConnector>();
            connectorMock.Setup(c => c.GetTablesAsync(connStr)).ReturnsAsync(new List<string> { "Table1", "Table2" });
            _registryMock.Setup(r => r.GetConnector(connType)).Returns(connectorMock.Object);

            // Act
            var tables = await _manager.GetTablesAsync(connName);

            // Assert
            Assert.Equal(2, tables.Count());
            Assert.Contains("Table1", tables);
            _registryMock.Verify(r => r.GetConnector(connType), Times.Once);
        }

        [Fact]
        public async Task GetTablesAsync_CachesResults()
        {
            // Arrange
            string connName = "MyConn";
            _manager.RegisterConnection(connName, "MSSQL", "...");
            var connectorMock = new Mock<IConnector>();
            connectorMock.Setup(c => c.GetTablesAsync(It.IsAny<string>())).ReturnsAsync(new List<string> { "T1" });
            _registryMock.Setup(r => r.GetConnector(It.IsAny<string>())).Returns(connectorMock.Object);

            // Act
            await _manager.GetTablesAsync(connName);
            await _manager.GetTablesAsync(connName);

            // Assert
            connectorMock.Verify(c => c.GetTablesAsync(It.IsAny<string>()), Times.Once);
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
            
            var connectorMock = new Mock<IConnector>();
            connectorMock.Setup(c => c.GetTablesAsync(It.IsAny<string>())).ReturnsAsync(new List<string> { "T1" });
            _registryMock.Setup(r => r.GetConnector(It.IsAny<string>())).Returns(connectorMock.Object);

            // Cache it
            await _manager.GetTablesAsync(connName, uri);
            
            // Act - Register again (simulating change)
            _manager.RegisterDocumentConnection(uri, connName, "MSSQL", "NEW_STR");
            await _manager.GetTablesAsync(connName, uri);

            // Assert
            connectorMock.Verify(c => c.GetTablesAsync(It.IsAny<string>()), Times.Exactly(2));
        }
    }
}
