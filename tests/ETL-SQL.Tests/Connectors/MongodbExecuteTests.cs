using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Mongodb;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Services;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

[Trait("Category", "Connectors")]
public sealed class MongodbExecuteTests
{
    private static (IExecutionContext Context, Mock<ILogger> Logger) CreateContext(bool whatIf = false)
    {
        var logger = new Mock<ILogger>();
        var context = new Mock<IExecutionContext>();
        context.Setup(c => c.SecurityService).Returns(new SecurityService(logger.Object));
        context.Setup(c => c.Logger).Returns(logger.Object);
        context.Setup(c => c.IsWhatIf).Returns(whatIf);
        return (context.Object, logger);
    }

    private static MongodbDataSource CreateUnreachable(IExecutionContext context) =>
        new(context, "mongodb://192.0.2.1:27017/?serverSelectionTimeoutMS=200", "test", null, null);

    [Fact]
    public void MongodbDataSource_ImplementsIDatabaseSource_WithSupportsSqlPushdownFalse()
    {
        var (ctx, _) = CreateContext();
        IDatabaseSource ds = CreateUnreachable(ctx);

        Assert.False(ds.SupportsSqlPushdown);
        Assert.Equal("MONGODB", ds.Dialect);
    }

    [Fact]
    public async Task ExecuteRawSql_InvalidJson_FailsWithPositionAndNoBodyEcho()
    {
        var (ctx, _) = CreateContext();
        var ds = CreateUnreachable(ctx);
        const string sentinel = "UNIQUE_BODY_SENTINEL_98765";
        var badJson = "{\n  \"collection\": \"" + sentinel + "\",\n  \"filter\": { broken }\n}";

        var ex = await Assert.ThrowsAsync<ExecutionException>(async () =>
        {
            await foreach (var _ in ds.ExecuteRawSql(badJson)) { }
        });

        Assert.Contains("line 3", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("column", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(sentinel, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("broken", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"collection":"c","pipeline":[{"$match":{"a":1}},{"$out":"evil"}]}""", "$out")]
    [InlineData("""{"collection":"c","pipeline":[{"$match":{"a":1}},{"$merge":{"into":"evil"}}]}""", "$merge")]
    [InlineData("""{"collection":"c","pipeline":[{"$facet":{"branch":[{"$MERGE":{"into":"evil"}}]}}]}""", "$MERGE")]
    [InlineData("""{"collection":"c","insert":[{"a":1}]}""", "insert")]
    [InlineData("""{"collection":"c","update":{"$set":{"a":2}}}""", "update")]
    [InlineData("""{"collection":"c","delete":{"a":1}}""", "delete")]
    [InlineData("""{"collection":"c","drop":true}""", "drop")]
    [InlineData("""{"collection":"c","bulkWrite":[]}""", "bulkWrite")]
    [InlineData("""{"collection":"c","filter":{"$where":"sleep(100000)"}}""", "$where")]
    [InlineData("""{"collection":"c","filter":{"$expr":{"$function":{"body":"function(){}","args":[],"lang":"js"}}}}""", "$function")]
    [InlineData("""{"collection":"c","pipeline":[{"$group":{"_id":1,"x":{"$accumulator":{}}}}]}""", "$accumulator")]
    public async Task ExecuteRawSql_WriteStagesAndCommands_AreRefused(string commandJson, string expectedMention)
    {
        var (ctx, _) = CreateContext();
        var ds = CreateUnreachable(ctx);

        var ex = await Assert.ThrowsAsync<ExecutionException>(async () =>
        {
            await foreach (var _ in ds.ExecuteRawSql(commandJson)) { }
        });

        Assert.Contains("read-only", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedMention, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("DROP TABLE IF EXISTS products;")]
    [InlineData("CREATE TABLE products (\n  id INT\n);")]
    public async Task ExecuteRawSql_SqlText_FailsWithClearMessage(string sql)
    {
        var (ctx, _) = CreateContext();
        var ds = CreateUnreachable(ctx);

        var ex = await Assert.ThrowsAsync<ExecutionException>(async () =>
        {
            await foreach (var _ in ds.ExecuteRawSql(sql)) { }
        });

        Assert.Contains("do not run SQL", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"collection":"c"}""")]
    [InlineData("""{"collection":"c","filter":{},"pipeline":[]}""")]
    [InlineData("""{"filter":{}}""")]
    [InlineData("""{"collection":"c","pipeline":[],"limit":10}""")]
    public async Task ExecuteRawSql_MalformedCommandShape_IsRefused(string commandJson)
    {
        var (ctx, _) = CreateContext();
        var ds = CreateUnreachable(ctx);

        await Assert.ThrowsAsync<ExecutionException>(async () =>
        {
            await foreach (var _ in ds.ExecuteRawSql(commandJson)) { }
        });
    }

    [Fact]
    public async Task ExecuteRawSql_WhatIf_LogsWithoutConnecting()
    {
        var (ctx, logger) = CreateContext(whatIf: true);
        var ds = CreateUnreachable(ctx);

        var batches = await ds.ExecuteRawSql("""{"collection":"products","filter":{"price":{"$gt":10}}}""").ToListAsync();

        Assert.Empty(batches);
        logger.Verify(
            l => l.WriteLine(It.Is<string>(s => s.Contains("WHAT IF", StringComparison.Ordinal) && s.Contains("products", StringComparison.Ordinal)), It.IsAny<ConsoleColor>()),
            Times.Once);
    }
}
