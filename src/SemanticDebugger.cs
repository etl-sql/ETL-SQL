using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core.Services;
using ETL_SQL.Data;
using ETL_SQL.Common;

namespace ETL_SQL.Debug
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var metadata = new MockMetadataManager();
            var service = new LanguageService(metadata);

            string script = @"
WITH Sales_CTE AS (
    SELECT ProductID, SUM(Amount) as Total
    FROM Production.Sales
    GROUP BY ProductID
)
SELECT s.ProductID, s.Total, p.ProductName
INTO #Summary
FROM Sales_CTE s
JOIN Production.Products p ON s.ProductID = p.ProductID
WHERE s.Total > 1000;

SELECT * FROM #Summary;
";

            var tokens = await service.GetSemanticTokensAsync(script);

            Console.WriteLine($"Found {tokens.Count} semantic tokens.");
            foreach (var t in tokens)
            {
                string typeName = SemanticTokenTypes.Legend[t.TypeIndex];
                Console.WriteLine($"Line {t.Line}, Col {t.Column}: {typeName} (Length {t.Length})");
            }
        }
    }

    class MockMetadataManager : IMetadataManager
    {
        public bool DebugMode { get; set; } = false;
        public void RegisterConnection(string name, string type, string connectionString) { }
        public void RegisterDocumentConnection(string uri, string name, string type, string connectionString) { }
        public void ClearDocumentConnections(string uri) { }
        public List<ConnectionInfo> GetConnections(string? uri = null) => new();
        public Task<IEnumerable<string>> GetTablesAsync(string connectionName, string? uri = null) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetViewsAsync(string connectionName, string? uri = null) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetTempTablesAsync(string? uri = null) => Task.FromResult(Enumerable.Empty<string>());
        public void RegisterTempTable(string uri, string name, List<string> columns) { }
        public void ClearTempTables(string uri) { }
        public Task<IEnumerable<string>> GetColumnsAsync(string connectionName, string tableName, string? uri = null) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<ColumnMetadata>> GetColumnDetailsAsync(string connectionName, string tableName, string? uri = null) => Task.FromResult(Enumerable.Empty<ColumnMetadata>());
        public IEnumerable<string> GetRegisteredNames() => Enumerable.Empty<string>();
        public IConnector? GetConnector(string name) => null;
        public string? GetConnectionType(string connectionName, string? uri = null) => "MSSQL";
        public void ClearCache() { }
        public void ClearCacheForUri(string uri) { }
        public void CleanUpDocumentConnectionsAndTempTables(string uri, IEnumerable<string> activeConnectionNames, IEnumerable<string> activeTempTableNames) { }
    }
}
