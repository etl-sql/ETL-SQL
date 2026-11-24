using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Mongodb;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Services;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using Xunit;

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

        [Fact]
        public async Task MongodbDataSource_KeyColumnsUpsert_RerunLeavesOneDocumentPerKey()
        {
            var ctx = MakeContext();
            var collName = $"upsert_products_{Guid.NewGuid():N}";
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "DATABASE", _fixture.DatabaseName },
                { "COLLECTION", collName },
                { "KEY_COLUMNS", "tenant_id, sku" }
            };

            var ds = new MongodbDataSource(ctx, _fixture.ConnectionString, _fixture.DatabaseName, collName, options);

            static async IAsyncEnumerable<DataTable> SingleBatch(DataTable t)
            {
                yield return t;
                await Task.CompletedTask;
            }

            // First load
            var batch1 = new DataTable();
            batch1.SetColumns(["tenant_id", "sku", "price"]);
            await batch1.AddRowAsync(new Row { ["tenant_id"] = "t1", ["sku"] = "SKU-1", ["price"] = 10 });
            await batch1.AddRowAsync(new Row { ["tenant_id"] = "t1", ["sku"] = "SKU-2", ["price"] = 20 });
            await ds.WriteBatches(SingleBatch(batch1), append: true);

            // Second load: re-run SKU-1 with updated price, keep SKU-2 identical, add SKU-3
            var batch2 = new DataTable();
            batch2.SetColumns(["tenant_id", "sku", "price"]);
            await batch2.AddRowAsync(new Row { ["tenant_id"] = "t1", ["sku"] = "SKU-1", ["price"] = 15 });
            await batch2.AddRowAsync(new Row { ["tenant_id"] = "t1", ["sku"] = "SKU-2", ["price"] = 20 });
            await batch2.AddRowAsync(new Row { ["tenant_id"] = "t1", ["sku"] = "SKU-3", ["price"] = 30 });
            await ds.WriteBatches(SingleBatch(batch2), append: true);

            var readBatches = await ds.ReadBatches().ToListAsync();
            var rows = readBatches.SelectMany(b => b.Rows).ToList();
            Assert.Equal(3, rows.Count);

            Assert.Equal(15, Convert.ToInt32(rows.Single(r => r["sku"]?.ToString() == "SKU-1")["price"]));
            Assert.Equal(20, Convert.ToInt32(rows.Single(r => r["sku"]?.ToString() == "SKU-2")["price"]));
            Assert.Equal(30, Convert.ToInt32(rows.Single(r => r["sku"]?.ToString() == "SKU-3")["price"]));

            // APPEND=FALSE still drops first even when KEY_COLUMNS is set.
            var batch3 = new DataTable();
            batch3.SetColumns(["tenant_id", "sku", "price"]);
            await batch3.AddRowAsync(new Row { ["tenant_id"] = "t1", ["sku"] = "SKU-9", ["price"] = 99 });
            await ds.WriteBatches(SingleBatch(batch3), append: false);

            var afterDrop = (await ds.ReadBatches().ToListAsync()).SelectMany(b => b.Rows).ToList();
            Assert.Single(afterDrop);
            Assert.Equal("SKU-9", afterDrop[0]["sku"]?.ToString());
        }

        [Fact]
        public async Task MongodbDataSource_KeyColumnsUpsert_NullKeyFailsWithRowOrdinal()
        {
            var ctx = MakeContext();
            var collName = $"upsert_null_{Guid.NewGuid():N}";
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "DATABASE", _fixture.DatabaseName },
                { "COLLECTION", collName },
                { "KEY_COLUMNS", "sku" }
            };

            var ds = new MongodbDataSource(ctx, _fixture.ConnectionString, _fixture.DatabaseName, collName, options);

            var batch = new DataTable();
            batch.SetColumns(["sku", "name"]);
            await batch.AddRowAsync(new Row { ["sku"] = "OK-1", ["name"] = "Good" });
            await batch.AddRowAsync(new Row { ["sku"] = null, ["name"] = "Bad" });

            static async IAsyncEnumerable<DataTable> SingleBatch(DataTable t)
            {
                yield return t;
                await Task.CompletedTask;
            }

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => ds.WriteBatches(SingleBatch(batch), append: true));
            Assert.Contains("row 2", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("sku", ex.Message, StringComparison.OrdinalIgnoreCase);

            var after = (await ds.ReadBatches().ToListAsync()).SelectMany(b => b.Rows).ToList();
            Assert.Empty(after);
        }
    }
}
