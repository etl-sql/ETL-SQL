using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Engine.Handlers;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    public class BundlePublishSupportTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"etlsql-bundle-{Guid.NewGuid():N}");

        public BundlePublishSupportTests()
        {
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { }
        }

        [Fact]
        public async Task PreflightAsync_DirectorySource_IncludesIndependentScripts()
        {
            await File.WriteAllTextAsync(Path.Combine(_root, "main.etlsql"), "PRINT 'main';");
            await File.WriteAllTextAsync(Path.Combine(_root, "append.etlsql"), "PRINT 'append';");
            await File.WriteAllTextAsync(Path.Combine(_root, "report.rptsql"), "SET REPORT TITLE = 'Integration';");
            await File.WriteAllTextAsync(Path.Combine(_root, "notes.txt"), "not bundled");

            var result = await BundlePublishSupport.PreflightAsync(
                "integration",
                _root,
                "main.etlsql",
                publishPassword: null,
                encryptionPassword: "machine-key");

            var paths = result.Files.Select(f => f.VirtualPath).OrderBy(p => p).ToArray();
            Assert.Equal(new[] { "append.etlsql", "main.etlsql", "report.rptsql" }, paths);
        }

        [Fact]
        public async Task PreflightAsync_CapturesLineageForPackagedScripts()
        {
            await File.WriteAllTextAsync(Path.Combine(_root, "main.etlsql"), @"
SELECT OrderId /* @owner: SalesOps; */
INTO #stage
FROM sales.Orders;
RUN SCRIPT 'report.rptsql';
");
            await File.WriteAllTextAsync(Path.Combine(_root, "report.rptsql"), @"
CREATE VISUAL SalesCard AS CARD (
    SOURCE = #stage,
    MAPPINGS (VALUE = OrderId)
);
");

            var result = await BundlePublishSupport.PreflightAsync(
                "sales-bundle",
                Path.Combine(_root, "main.etlsql"),
                "main.etlsql",
                publishPassword: null,
                encryptionPassword: "machine-key");

            Assert.Contains(result.LineageEntries, e =>
                e.TargetTable == "#stage" &&
                e.TargetColumn == "OrderId" &&
                e.SourceTables.Contains("sales.Orders", StringComparer.OrdinalIgnoreCase) &&
                e.SourceFile == "main.etlsql" &&
                e.Metadata["owner"] == "SalesOps" &&
                e.Metadata["bundle"] == "sales-bundle");

            Assert.Contains(result.LineageEntries, e =>
                e.TargetTable == "report:SalesCard" &&
                e.Operation == "CREATE VISUAL" &&
                e.SourceFile == "report.rptsql");
        }
    }
}
