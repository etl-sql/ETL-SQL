using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Common;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Connectors.BigQuery;
using ETL_SQL.Services;

namespace ETL_SQL.Tests.Integration.Connectors
{
    /// <summary>
    /// T1 smoke test against the goccy/bigquery-emulator Docker container.
    /// Requires Docker — run with: dotnet test --filter "Category=Integration"
    /// The fixture sets BIGQUERY_EMULATOR_HOST so BigQueryClientBuilder routes there.
    /// </summary>
    [Collection("BigQuery collection")]
    [Trait("Category", "Integration")]
    [Trait("Connector", "BIGQUERY")]
    [Trait("CertificationClass", "DockerRealIntegration")]
    public class BigQueryIntegrationTests
    {
        private readonly BigQueryFixture _bq;
        private static SystemExecutionContext Ctx => SystemExecutionContext.Instance;

        public BigQueryIntegrationTests(BigQueryFixture bq) => _bq = bq;

        private BigQueryDataSource MakeDs(string? query = null) =>
            new BigQueryDataSource(Ctx,
                $"project={BigQueryFixture.TestProject};dataset={BigQueryFixture.TestDataset};",
                null,
                new Dictionary<string, string>
                {
                    ["PROJECT_ID"] = BigQueryFixture.TestProject,
                    ["DATASET"]    = BigQueryFixture.TestDataset,
                });

        // ── T1 — Smoke test ───────────────────────────────────────────────────────

        [Fact]
        public async Task Smoke_GetVersion_ReturnsProjectInfo()
        {
            var ds = MakeDs();
            var version = await ds.GetVersionAsync();
            Assert.Contains(BigQueryFixture.TestProject, version, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Smoke_ExecuteRawSql_SelectLiteral_ReturnsRow()
        {
            var ds = MakeDs();
            var batches = await ds.ExecuteRawSql("SELECT 1 AS value, 'hello' AS label").ToListAsync();
            Assert.NotEmpty(batches);
            var rows = batches.SelectMany(b => b.Rows).ToList();
            Assert.NotEmpty(rows);
        }

        [Fact]
        public async Task Smoke_GetTables_DoesNotThrow()
        {
            var ds = MakeDs();
            var tables = await ds.GetTablesAsync();
            Assert.NotNull(tables);
        }
    }
}
