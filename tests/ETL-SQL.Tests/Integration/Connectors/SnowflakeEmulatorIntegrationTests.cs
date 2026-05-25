using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Snowflake;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Services;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Integration.Connectors
{
    [Collection("SNOWFLAKE_EMULATOR collection")]
    [Trait("Category", "Integration")]
    [Trait("Connector", "SNOWFLAKE")]
    [Trait("CertificationClass", "DockerRealIntegration")]
    public class SnowflakeEmulatorIntegrationTests
    {
        private readonly SnowflakeEmulatorFixture _snowflake;

        public SnowflakeEmulatorIntegrationTests(SnowflakeEmulatorFixture snowflake)
        {
            _snowflake = snowflake;
        }

        private static IExecutionContext MakeContext()
        {
            var security = new SecurityService(NullLogger.Instance);
            var ctx = new Mock<IExecutionContext>();
            ctx.Setup(c => c.SecurityService).Returns(security);
            ctx.Setup(c => c.Logger).Returns(NullLogger.Instance);
            return ctx.Object;
        }

        private string ConnectionString()
        {
            var connector = new SnowflakeConnector();
            return connector.BuildConnectionString(new Dictionary<string, string>
            {
                ["HOST"] = _snowflake.Host,
                ["ACCOUNT"] = "test",
                ["PORT"] = _snowflake.Port.ToString(),
                ["PROTOCOL"] = "http",
                ["USERNAME"] = "test",
                ["PASSWORD"] = "test",
                ["DATABASE"] = "TEST_DB",
                ["SCHEMA"] = "PUBLIC"
            });
        }

        [Fact]
        public async Task ExecuteRawSql_SelectSnowflakeFunction_ReturnsRows()
        {
            var ds = new SnowflakeDataSource(MakeContext(), ConnectionString(), null, null);

            var batches = await ds.ExecuteRawSql("SELECT IFF(1 > 0, 'yes', 'no') AS verdict").ToListAsync();

            var row = Assert.Single(batches.SelectMany(b => b.Rows));
            Assert.Equal("yes", row["VERDICT"]?.ToString(), StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task CreateInsertSelect_RoundTrip_WorksThroughSnowflakeDriver()
        {
            var ds = new SnowflakeDataSource(MakeContext(), ConnectionString(), null, null);
            var tableName = $"ETLSQL_{Guid.NewGuid():N}";

            await ds.ExecuteRawSql("CREATE DATABASE IF NOT EXISTS TEST_DB").ToListAsync();
            await ds.ExecuteRawSql("USE DATABASE TEST_DB").ToListAsync();
            await ds.ExecuteRawSql("CREATE SCHEMA IF NOT EXISTS PUBLIC").ToListAsync();
            await ds.ExecuteRawSql("USE SCHEMA PUBLIC").ToListAsync();
            await ds.ExecuteRawSql($"CREATE TABLE {tableName} (ID INT, NAME VARCHAR)").ToListAsync();
            await ds.ExecuteRawSql($"INSERT INTO {tableName} VALUES (1, 'Alice'), (2, 'Bob')").ToListAsync();

            var reader = (SnowflakeDataSource)ds.WithTable(tableName);
            var batches = await reader.ReadBatches(batchSize: 1).ToListAsync();

            Assert.Equal(2, batches.Sum(b => b.Rows.Count));
            Assert.Contains(batches.SelectMany(b => b.Rows), r => r["NAME"]?.ToString() == "Alice");
            Assert.Contains(batches.SelectMany(b => b.Rows), r => r["NAME"]?.ToString() == "Bob");
        }

        [Fact]
        public async Task InvalidSql_WrapsAsExecutionException()
        {
            var ds = new SnowflakeDataSource(MakeContext(), ConnectionString(), null, null);

            var ex = await Assert.ThrowsAsync<ExecutionException>(
                async () => await ds.ExecuteRawSql("SELECT * FROM").ToListAsync());

            Assert.Contains("Snowflake", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password=test", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
