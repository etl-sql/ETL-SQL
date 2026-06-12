using System;
using ETL_SQL.Core;
using ETL_SQL.Reporting.Builders;
using Xunit;

namespace ETL_SQL.Tests.Reporting
{
    public class DatasetBuilderUnitTests
    {
        private static CreateDatasetStatement MakeStmt(string tableName,
            string? refreshInterval = null, string? ttl = null) =>
            new CreateDatasetStatement
            {
                TempTableName = tableName,
                RefreshInterval = refreshInterval,
                Ttl = ttl,
                SourceQuery = new SelectStatement(
                    new System.Collections.Generic.List<SelectColumn>
                    {
                        new SelectColumn(new IdentifierExpression("*"))
                    },
                    null, null, new System.Collections.Generic.List<JoinClause>(), null)
            };

        [Fact]
        public void Build_SetsCorrectTempTableName()
        {
            var builder = new DatasetBuilder();
            var stmt = MakeStmt("&SalesData");

            var manifest = builder.Build(stmt);

            Assert.Equal("&SalesData", manifest.TempTableName);
        }

        [Fact]
        public void Build_CopiesRefreshInterval()
        {
            var builder = new DatasetBuilder();
            var manifest = builder.Build(MakeStmt("#Data", refreshInterval: "5m"));

            Assert.Equal("5m", manifest.RefreshInterval);
        }

        [Fact]
        public void Build_CopiesTtl()
        {
            var builder = new DatasetBuilder();
            var manifest = builder.Build(MakeStmt("#Data", ttl: "1h"));

            Assert.Equal("1h", manifest.Ttl);
        }

        [Fact]
        public void Build_NullIntervalAndTtl_ManifestHasNulls()
        {
            var builder = new DatasetBuilder();
            var manifest = builder.Build(MakeStmt("#Data"));

            Assert.Null(manifest.RefreshInterval);
            Assert.Null(manifest.Ttl);
        }

        [Fact]
        public void Build_SetsLastRefreshToUtcNow()
        {
            var before = DateTime.UtcNow;
            var builder = new DatasetBuilder();
            var manifest = builder.Build(MakeStmt("#Data"));
            var after = DateTime.UtcNow;

            Assert.NotNull(manifest.LastRefresh);
            Assert.True(manifest.LastRefresh >= before);
            Assert.True(manifest.LastRefresh <= after);
        }

        [Fact]
        public void Build_InitializesRowCountToZero()
        {
            var builder = new DatasetBuilder();
            var manifest = builder.Build(MakeStmt("#Data"));

            Assert.Equal(0, manifest.RowCount);
        }
    }
}
