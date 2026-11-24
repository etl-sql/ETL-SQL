using System;
using System.Collections.Generic;
using System.Threading;
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

namespace ETL_SQL.Tests.Connectors;

[Trait("Category", "Connectors")]
public sealed class MongodbUpsertTests
{
    private readonly MongodbConnector _connector = new();

    private static IExecutionContext CreateContext()
    {
        var context = new Mock<IExecutionContext>();
        context.Setup(c => c.SecurityService).Returns(new SecurityService(NullLogger.Instance));
        context.Setup(c => c.Logger).Returns(NullLogger.Instance);
        return context.Object;
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("id,")]
    [InlineData(",id")]
    [InlineData("id, ,name")]
    public void InvalidKeyColumns_FailsAtBuildAndCreate(string invalid)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CONNECTION_STRING"] = "mongodb://localhost:27017",
            ["DATABASE"] = "test",
            ["COLLECTION"] = "items",
            ["KEY_COLUMNS"] = invalid
        };

        Assert.Throws<ExecutionException>(() => _connector.BuildConnectionString(options));
        Assert.Throws<ExecutionException>(() => new MongodbDataSource(CreateContext(), "mongodb://localhost:27017", "test", "items", options));
    }

    [Fact]
    public async Task WriteBatches_NullKey_FailsWithRowOrdinalBeforeBulkWrite()
    {
        var collMock = new Mock<IMongoCollection<BsonDocument>>();
        var dbMock = new Mock<IMongoDatabase>();
        dbMock.Setup(d => d.GetCollection<BsonDocument>("items", null)).Returns(collMock.Object);
        var clientMock = new Mock<IMongoClient>();
        clientMock.Setup(c => c.GetDatabase("test", null)).Returns(dbMock.Object);

        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DATABASE"] = "test",
            ["COLLECTION"] = "items",
            ["KEY_COLUMNS"] = "sku"
        };

        var ds = new MongodbDataSource(CreateContext(), "mongodb://localhost:27017", "test", "items", options, clientMock.Object);

        var table = new DataTable();
        table.SetColumns(["sku", "name"]);
        await table.AddRowAsync(new Row { ["sku"] = "A-1", ["name"] = "Widget" });
        await table.AddRowAsync(new Row { ["sku"] = null, ["name"] = "Broken" });

        static async IAsyncEnumerable<DataTable> Single(DataTable t)
        {
            yield return t;
            await Task.CompletedTask;
        }

        var ex = await Assert.ThrowsAsync<ExecutionException>(() => ds.WriteBatches(Single(table), append: true));
        Assert.Contains("row 2", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sku", ex.Message, StringComparison.OrdinalIgnoreCase);

        collMock.Verify(
            c => c.BulkWriteAsync(
                It.IsAny<IEnumerable<WriteModel<BsonDocument>>>(),
                It.IsAny<BulkWriteOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WriteBatches_MissingKeyColumnInBatch_ThrowsExecutionException()
    {
        var collMock = new Mock<IMongoCollection<BsonDocument>>();
        var dbMock = new Mock<IMongoDatabase>();
        dbMock.Setup(d => d.GetCollection<BsonDocument>("items", null)).Returns(collMock.Object);
        var clientMock = new Mock<IMongoClient>();
        clientMock.Setup(c => c.GetDatabase("test", null)).Returns(dbMock.Object);

        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DATABASE"] = "test",
            ["COLLECTION"] = "items",
            ["KEY_COLUMNS"] = "missing_key"
        };

        var ds = new MongodbDataSource(CreateContext(), "mongodb://localhost:27017", "test", "items", options, clientMock.Object);

        var table = new DataTable();
        table.SetColumns(["sku", "name"]);
        await table.AddRowAsync(new Row { ["sku"] = "A-1", ["name"] = "Widget" });

        static async IAsyncEnumerable<DataTable> Single(DataTable t)
        {
            yield return t;
            await Task.CompletedTask;
        }

        var ex = await Assert.ThrowsAsync<ExecutionException>(() => ds.WriteBatches(Single(table), append: true));
        Assert.Contains("missing_key", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MetadataAndHelp_IncludeKeyColumns()
    {
        Assert.Contains("KEY_COLUMNS", _connector.GetSupportedOptions().Keys);
        Assert.Contains("KEY_COLUMNS", _connector.GetHelp(), StringComparison.Ordinal);
        Assert.True(LanguageMetadata.IsKeyword("KEY_COLUMNS"));
    }
}
