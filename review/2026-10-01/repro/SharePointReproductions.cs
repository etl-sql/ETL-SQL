using System.Net;
using ETL_SQL.Common;
using ETL_SQL.Connectors;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Services;
using Moq;
using Xunit;

namespace ETL_SQL.Review;

public sealed class SharePointReproductions
{
    [Fact]
    public async Task ListReadMustFollowTheServerContinuation()
    {
        using var handler = new FixtureHandler(request => request.RequestUri!.Query.Contains("skiptoken", StringComparison.Ordinal)
            ? "{\"value\":[{\"Id\":2}]}"
            : "{\"value\":[{\"Id\":1}],\"odata.nextLink\":\"https://review.example/_api/items?$skiptoken=2\"}");
        var rows = await Read(handler);
        Assert.Equal(new[] { "1", "2" }, rows.Select(row => row["Id"]?.ToString()));
    }

    [Fact]
    public async Task BatchesMustNotIncludePreviouslyReturnedRows()
    {
        using var handler = new FixtureHandler(_ => "{\"value\":[{\"Id\":1},{\"Id\":2}]}");
        var rows = await Read(handler, 1);
        Assert.Equal(new[] { "1", "2" }, rows.Select(row => row["Id"]?.ToString()));
    }

    [Fact]
    public async Task VerboseODataListReadMustReturnItsResults()
    {
        using var handler = new FixtureHandler(_ => "{\"d\":{\"results\":[{\"Id\":1}]}}");
        var rows = await Read(handler);
        Assert.Single(rows);
        Assert.Equal("1", rows[0]["Id"]);
    }

    private static async Task<List<Row>> Read(HttpMessageHandler handler, int batchSize = 100)
    {
        var context = new Mock<IExecutionContext>();
        context.SetupGet(c => c.Logger).Returns(NullLogger.Instance);
        context.SetupGet(c => c.SecurityService).Returns(new SecurityService(NullLogger.Instance));
        context.SetupGet(c => c.ServiceProvider).Returns(new Mock<IServiceProvider>().Object);
        var source = new SharePointConnector(context.Object, "https://review.example", new()
        {
            ["AUTH_MODE"] = "INTEGRATED", ["LIST_NAME"] = "Review"
        }, handler);
        var rows = new List<Row>();
        await foreach (var batch in source.ReadBatches(batchSize)) rows.AddRange(batch.Rows);
        return rows;
    }

    private sealed class FixtureHandler(Func<HttpRequestMessage, string> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response(request)) });
    }
}
