using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors;
using ETL_SQL.Connectors.Avro;
using ETL_SQL.Connectors.Email;
using ETL_SQL.Connectors.Excel;
using ETL_SQL.Connectors.Odbc;
using ETL_SQL.Connectors.Oracle;
using ETL_SQL.Connectors.Orchestrator;
using ETL_SQL.Connectors.Parquet;
using ETL_SQL.Connectors.ReportPortal;
using ETL_SQL.Connectors.Rest;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using Xunit;

namespace ETL_SQL.Tests.Connectors
{
    /// <summary>
    /// T4 — Exception wrapping tests.  Each test injects a provider-level failure and
    /// asserts that it surfaces as <see cref="ExecutionException"/> with a sanitized
    /// message (no raw provider type leaking to the caller).
    /// </summary>
    [Trait("Connector", "MULTIPLE")]
    [Trait("CertificationClass", "MockedIntegration")]
    public class ConnectorExceptionWrappingTests : IDisposable
    {
        private readonly string _dir;
        private static SystemExecutionContext Ctx => SystemExecutionContext.Instance;

        public ConnectorExceptionWrappingTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "T4-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        private string WriteGarbage(string name)
        {
            var path = Path.Combine(_dir, name);
            File.WriteAllBytes(path, new byte[] { 0xFF, 0xFE, 0x00, 0x01, 0xDE, 0xAD, 0xBE, 0xEF });
            return path;
        }

        // ── ORACLE ────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Oracle_BadConnectionString_WrapsAsExecutionException()
        {
            // No DATA SOURCE in the connection string — Oracle ODP.NET throws OracleException immediately.
            var ds = new OracleDataSource(Ctx, "User Id=invalid;Password=invalid;Connection Timeout=1;");
            var ex = await Assert.ThrowsAsync<ExecutionException>(() => ds.GetVersionAsync());
            Assert.Contains("Oracle", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ── ODBC ──────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Odbc_NonExistentDsn_WrapsAsExecutionException()
        {
            // ODBC driver manager throws OdbcException immediately when the DSN is not found.
            var ds = new OdbcDataSource(Ctx, "DSN=__ETL_SQL_T4_NONEXISTENT_DSN__;");
            var ex = await Assert.ThrowsAsync<ExecutionException>(() => ds.GetVersionAsync());
            Assert.Contains("ODBC", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ── EXCEL ─────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Excel_CorruptFile_WrapsAsExecutionException()
        {
            var path = WriteGarbage("corrupt.xlsx");
            var ds = new ExcelDataSource(Ctx, path);
            var ex = await Assert.ThrowsAsync<ExecutionException>(
                async () => await ds.ReadBatches().ToListAsync());
            Assert.Contains("Excel", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ── PARQUET ───────────────────────────────────────────────────────────────

        [Fact]
        public async Task Parquet_CorruptFile_WrapsAsExecutionException()
        {
            var path = WriteGarbage("corrupt.parquet");
            var ds = new ParquetDataSource(Ctx, path);
            var ex = await Assert.ThrowsAsync<ExecutionException>(
                async () => await ds.ReadBatches().ToListAsync());
            Assert.Contains("Parquet", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ── AVRO ──────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Avro_CorruptFile_WrapsAsExecutionException()
        {
            var path = WriteGarbage("corrupt.avro");
            var ds = new AvroDataSource(Ctx, path);
            var ex = await Assert.ThrowsAsync<ExecutionException>(
                async () => await ds.ReadBatches().ToListAsync());
            Assert.Contains("Avro", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ── FTP ───────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Ftp_UnreachableHost_WrapsAsExecutionException()
        {
            // Port 21 on localhost is not running; connection is refused immediately.
            var conn = new FtpConnector(Ctx, "127.0.0.1", "user", "pass");
            var ex = await Assert.ThrowsAsync<ExecutionException>(
                async () => await conn.ListFilesAsync("/").ToListAsync());
            Assert.Contains("FTP", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ── AZURE_BLOB ────────────────────────────────────────────────────────────

        [Fact]
        public async Task AzureBlob_InvalidAccount_WrapsAsExecutionException()
        {
            // Points BlobEndpoint to port 1 on localhost — connection is refused immediately.
            const string cs = "DefaultEndpointsProtocol=http;" +
                              "AccountName=devstoreaccount1;" +
                              "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
                              "BlobEndpoint=http://127.0.0.1:1/devstoreaccount1;";
            var conn = new AzureBlobConnector(Ctx, cs, "testcontainer");
            var ex = await Assert.ThrowsAsync<ExecutionException>(
                async () => await conn.ReadBatches().ToListAsync());
            Assert.Contains("Azure Blob", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ── API / REST ────────────────────────────────────────────────────────────

        [Fact]
        public async Task RestApi_UnreachableEndpoint_WrapsAsExecutionException()
        {
            // Port 1 on localhost — connection refused immediately.
            var ds = new RestDataSource(Ctx, "http://127.0.0.1:1/api/data");
            var ex = await Assert.ThrowsAsync<ExecutionException>(
                async () => await ds.ReadBatches().ToListAsync());
            Assert.Contains("REST", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ── SMTP ──────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Smtp_UnreachableHost_WrapsAsExecutionException()
        {
            var opts = new Dictionary<string, string>
            {
                ["HOST"] = "127.0.0.1",
                ["PORT"] = "1",      // port 1 is always refused
                ["USE_SSL"] = "false"
            };
            var ds = new SmtpDataSource(Ctx, opts);
            var batch = new ETL_SQL.Data.DataTable();
            batch.SetColumns(new[] { "To", "Subject", "Body" });
            var row = batch.NewRow();
            row["To"] = "test@example.com";
            row["Subject"] = "Test";
            row["Body"] = "Test body";
            await batch.AddRowAsync(row);

            var ex = await Assert.ThrowsAsync<ExecutionException>(
                () => ds.WriteBatches(new[] { batch }.ToAsyncEnumerable()));
            Assert.Contains("SMTP", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ── REPORT PORTAL ─────────────────────────────────────────────────────────

        [Fact]
        public async Task ReportPortal_UnreachableServer_WrapsAsExecutionException()
        {
            var ds = new ReportPortalDataSource("http://127.0.0.1:1", "user", "pass", NullLogger.Instance);
            var stmt = new ShowPortalUsersStatement();
            var ex = await Assert.ThrowsAsync<ExecutionException>(
                () => ds.ExecuteAdminStatementAsync(stmt, Ctx));
            Assert.Contains("Portal", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ── ORCHESTRATOR ──────────────────────────────────────────────────────────

        [Fact]
        public async Task Orchestrator_UnreachableServer_WrapsAsExecutionException()
        {
            var ds = new OrchestratorDataSource("http://127.0.0.1:1", "apikey", NullLogger.Instance);
            var stmt = new ShowJobsStatement();
            var ex = await Assert.ThrowsAsync<ExecutionException>(
                () => ds.ExecuteAdminStatementAsync(stmt, Ctx));
            Assert.Contains("Orchestrator", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
