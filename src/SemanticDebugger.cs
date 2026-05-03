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
        public Task<IEnumerable<string>> GetTablesAsync(string connectionName) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(string connectionName, string tableName) => Task.FromResult(Enumerable.Empty<string>());
        public IEnumerable<string> GetConnections() => Enumerable.Empty<string>();
        public string GetConnectionType(string connectionName) => "MSSQL";
        public void RegisterConnection(string name, string type, string target, Dictionary<string, string> options) { }
        public void UnregisterConnection(string name) { }
        public bool ConnectionExists(string name) => false;
        public Task RefreshConnections(string script, bool force = false) => Task.CompletedTask;
    }
}
