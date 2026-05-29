using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using MongoDB.Bson;
using MongoDB.Driver;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Connectors.Mongodb;
using ETL_SQL.Services;
using ETL_SQL.Data;

namespace ETL_SQL.Tests.Integration.Connectors
{
    [Collection("MongoDB collection")]
    [Trait("Category", "Integration")]
    [Trait("Connector", "MONGODB")]
    [Trait("CertificationClass", "DockerRealIntegration")]
    public class MongodbIntegrationTests
    {
        private readonly MongodbFixture _fixture;

        public MongodbIntegrationTests(MongodbFixture fixture)
        {
            _fixture = fixture;
        }

        private IExecutionContext MakeContext()
        {
            var security = new SecurityService(NullLogger.Instance);
            var ctx = new Mock<IExecutionContext>();
            ctx.Setup(c => c.SecurityService).Returns(security);
            ctx.Setup(c => c.Logger).Returns(NullLogger.Instance);
            return ctx.Object;
        }

        [Fact]
        public async Task MongodbConnector_GetVersionAsync_Success()
        {
            var ctx = MakeContext();
            var connector = new MongodbConnector();

            var version = await connector.GetVersionAsync(ctx, _fixture.ConnectionString);
            Assert.Contains("Connected", version);
            Assert.Contains("MongoDB Connector", version);
        }

        [Fact]
        public async Task MongodbConnector_GetVersionAsync_InvalidHost_ThrowsException()
        {
            var ctx = MakeContext();
            var connector = new MongodbConnector();

            // Timeout set low so the test doesn't hang long
            string invalidConnectionString = "mongodb://192.0.2.1:27017/?serverSelectionTimeoutMS=500";

            await Assert.ThrowsAsync<ExecutionException>(() =>
                connector.GetVersionAsync(ctx, invalidConnectionString));
        }

        [Fact]
        public async Task MongodbDataSource_WriteAndRead_Success()
        {
            var ctx = MakeContext();
            var options = new Dictionary<string, string>
            {
                { "DATABASE", _fixture.DatabaseName },
                { "COLLECTION", "customers" }
            };

            var ds = new MongodbDataSource(ctx, _fixture.ConnectionString, _fixture.DatabaseName, "customers", options);

            // 1. Write Data
            var table = new DataTable();
            table.SetColumns(new[] { "id", "name", "city" });
            await table.AddRowAsync(new Row { ["id"] = 101, ["name"] = "Alice", ["city"] = "Paris" });
            await table.AddRowAsync(new Row { ["id"] = 102, ["name"] = "Bob", ["city"] = "Berlin" });

            async IAsyncEnumerable<DataTable> GetBatches()
            {
                yield return table;
                await Task.CompletedTask;
            }

            await ds.WriteBatches(GetBatches(), append: true);

            // 2. Schema Discovery (GetColumnsAsync)
            var columns = (await ds.GetColumnsAsync()).ToList();
            Assert.Contains("id", columns);
            Assert.Contains("name", columns);
            Assert.Contains("city", columns);

            // 3. Read Data
            var batches = await ds.ReadBatches().ToListAsync();
            Assert.Single(batches);
            var resultTable = batches[0];
            Assert.Equal(2, resultTable.Rows.Count);

            var row1 = resultTable.Rows.FirstOrDefault(r => Convert.ToInt32(r["id"]) == 101);
            Assert.NotNull(row1);
            Assert.Equal("Alice", row1["name"]?.ToString());
            Assert.Equal("Paris", row1["city"]?.ToString());
        }
    }
}
