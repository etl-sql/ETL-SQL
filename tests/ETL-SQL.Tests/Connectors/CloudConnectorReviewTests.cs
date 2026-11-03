using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using ETL_SQL.Common;
using ETL_SQL.Connectors;
using ETL_SQL.Connectors.S3;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Services;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

/// <summary>Regression tests for the 2026-11-01 review of ETL-SQL.Connectors.Cloud.</summary>
public sealed class CloudConnectorReviewTests
{
    private static Mock<IExecutionContext> Context()
    {
        var context = new Mock<IExecutionContext>();
        context.SetupGet(c => c.Logger).Returns(NullLogger.Instance);
        context.SetupGet(c => c.SecurityService).Returns(new SecurityService(NullLogger.Instance));
        context.SetupGet(c => c.ServiceProvider).Returns(new Mock<IServiceProvider>().Object);
        context.Setup(c => c.ResolvePath(It.IsAny<string>())).Returns<string>(p => p);
        return context;
    }

    // ── Azure Blob ───────────────────────────────────────────────────────────

    [Fact]
    public void AzureBlob_EmptyConnectionDetails_FailAsAnExecutionException()
    {
        var ex = Assert.ThrowsAny<Exception>(() => new AzureBlobConnector().CreateDataSource(
            Context().Object, "", new Dictionary<string, string> { ["CONTAINER"] = "c" }));

        Assert.IsType<ExecutionException>(ex);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void AzureBlob_MalformedConnectionString_DoesNotEchoTheSecret()
    {
        var ex = Assert.ThrowsAny<Exception>(() => new AzureBlobConnector().CreateDataSource(
            Context().Object, "AccountName=a;AccountKey=not-base64-secret!;DefaultEndpointsProtocol=https",
            new Dictionary<string, string> { ["CONTAINER"] = "c" }));

        Assert.IsType<ExecutionException>(ex);
        Assert.DoesNotContain("not-base64-secret", ex.Message);
    }

    // ── S3 ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("//internal-host:9000")]
    [InlineData("internal-host:9000")]
    [InlineData("file:///etc/passwd")]
    public void S3_UnusableEndpoint_IsRefusedInsteadOfValidatingTheAwsDefault(string endpoint)
    {
        var options = new Dictionary<string, string> { ["ENDPOINT"] = endpoint, ["BUCKET"] = "b" };

        Assert.Throws<ExecutionException>(() => new S3Connector().GetHost("", options));
        Assert.Throws<ExecutionException>(() => new S3Connector(Context().Object, "b", options));
    }

    [Fact]
    public void S3_HttpEndpoint_ReportsItsOwnHost()
    {
        var host = new S3Connector().GetHost("", new Dictionary<string, string> { ["ENDPOINT"] = "http://minio.example.com:9000" });

        Assert.Equal("minio.example.com", host);
    }

    private static Mock<IAmazonS3> PagedBucket(int pages, int perPage)
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(c => c.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ListObjectsV2Request request, CancellationToken _) =>
            {
                var page = string.IsNullOrEmpty(request.ContinuationToken) ? 0 : int.Parse(request.ContinuationToken);
                return new ListObjectsV2Response
                {
                    S3Objects = Enumerable.Range(0, perPage)
                        .Select(i => new S3Object { Key = $"p{page}/f{i}.csv", Size = 10 + i, LastModified = DateTime.UtcNow })
                        .ToList(),
                    IsTruncated = page < pages - 1,
                    NextContinuationToken = page < pages - 1 ? (page + 1).ToString() : null
                };
            });
        return s3;
    }

    [Fact]
    public async Task S3_ListFiles_FollowsContinuationTokens()
    {
        var s3 = PagedBucket(pages: 3, perPage: 2);
        var connector = new S3Connector(Context().Object, "b", new Dictionary<string, string>(), s3.Object);

        var files = new List<FileMetaData>();
        await foreach (var file in connector.ListFilesAsync("")) files.Add(file);

        Assert.Equal(6, files.Count);
        Assert.Contains(files, f => f.FullPath == "p2/f1.csv");
    }

    [Fact]
    public async Task S3_ReadBatches_HonoursBatchSizeAndStoresSizeAsDecimal()
    {
        var s3 = PagedBucket(pages: 1, perPage: 5);
        var connector = new S3Connector(Context().Object, "b", new Dictionary<string, string>(), s3.Object);

        var batches = new List<DataTable>();
        await foreach (var batch in connector.ReadBatches(2)) batches.Add(batch);

        Assert.Equal(new[] { 2, 2, 1 }, batches.Select(b => b.Rows.Count));
        Assert.Equal(10m, Assert.IsType<decimal>(batches[0].Rows[0]["Size"]));
    }

    // ── SharePoint ───────────────────────────────────────────────────────────

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();
        public bool Disposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private static SharePointConnector SharePoint(HttpMessageHandler handler, string list = "Review") =>
        new(Context().Object, "https://review.example/sites/x", new Dictionary<string, string>
        {
            ["AUTH_MODE"] = "INTEGRATED",
            ["LIST_NAME"] = list
        }, handler);

    [Fact]
    public async Task SharePoint_GetVersionOnTheRegisteredConnector_ProbesTheGivenSite()
    {
        // The registered connector has no site of its own. The probe must use the connection string
        // it is handed: before the fix it always answered "Offline - Invalid Site URL".
        var context = Context().Object;
        var ex = await Record.ExceptionAsync(() =>
            new SharePointConnector().GetVersionAsync(context, "https://169.254.169.254/sites/x"));

        Assert.IsType<SecurityException>(ex);
    }

    [Fact]
    public async Task SharePoint_TransportFailure_IsWrappedAtTheConnectorBoundary()
    {
        using var handler = new Handler(_ => throw new HttpRequestException("connect to 10.1.2.3 failed"));
        var connector = SharePoint(handler);

        var ex = await Assert.ThrowsAsync<ExecutionException>(() => connector.FileExistsAsync("a.csv"));
        Assert.DoesNotContain("10.1.2.3", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task SharePoint_ListRows_KeepNumbersBooleansAndNulls()
    {
        using var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"value\":[{\"Id\":7,\"Amount\":12.5,\"Done\":true,\"Note\":null}]}")
        });

        var rows = new List<Row>();
        await foreach (var batch in SharePoint(handler).ReadBatches()) rows.AddRange(batch.Rows);

        var row = Assert.Single(rows);
        Assert.Equal(7m, Assert.IsType<decimal>(row["Id"]));
        Assert.Equal(12.5m, Assert.IsType<decimal>(row["Amount"]));
        Assert.Equal(true, row["Done"]);
        Assert.Null(row["Note"]);
    }

    [Fact]
    public async Task SharePoint_WithTable_SharesTheParentClientAndLeavesItOpen()
    {
        using var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"value\":[{\"Id\":1}]}")
        });
        var parent = SharePoint(handler);
        var clone = parent.WithTable("Other");

        await foreach (var _ in clone.ReadBatches()) { }
        await clone.DisposeAsync();
        await foreach (var _ in parent.ReadBatches()) { }

        Assert.Contains(handler.Requests, r => r.Contains("GetByTitle('Other')", StringComparison.Ordinal));
        Assert.Contains(handler.Requests, r => r.Contains("GetByTitle('Review')", StringComparison.Ordinal));
        Assert.False(handler.Disposed);
    }
}
