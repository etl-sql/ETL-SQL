using System.Net;
using System.Net.Http;
using System.Text;
using ETL_SQL.Common;
using ETL_SQL.Connectors.BigQuery;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Services;
using Google.Apis.Bigquery.v2;
using Google.Apis.Http;
using Google.Apis.Services;
using Google.Cloud.BigQuery.V2;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

[Trait("CompatBreak", "0.21.0")]
public sealed class BigQueryInsertRetryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InsertSuccessAndRowErrorsAreDistinguished(bool rowErrors)
    {
        using var transport = new InsertTransport(false, successfulResponse: true, rowErrors: rowErrors);
        using var service = new BigqueryService(new BaseClientService.Initializer
        {
            HttpClientFactory = new TransportFactory(transport),
            ApplicationName = "ETLSQL-test"
        });
        var source = new BigQueryDataSource(ETL_SQL.Core.Common.SystemExecutionContext.Instance,
            "project=test-project", "dataset.target", null, new BigQueryClientImpl("test-project", service));
        if (rowErrors)
            await Assert.ThrowsAsync<ExecutionException>(() => source.WriteBatches(Batches(), append: true));
        else
            await source.WriteBatches(Batches(), append: true);
        Assert.Equal(1, transport.AcceptedRows);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AmbiguousInsertIsNotReplayed(bool transportFailure)
    {
        using var transport = new InsertTransport(transportFailure);
        using var service = new BigqueryService(new BaseClientService.Initializer
        {
            HttpClientFactory = new TransportFactory(transport),
            ApplicationName = "ETLSQL-test"
        });
        var client = new BigQueryClientImpl("test-project", service);
        var source = new BigQueryDataSource(ETL_SQL.Core.Common.SystemExecutionContext.Instance, "project=test-project", "dataset.target", null, client);

        var error = await Assert.ThrowsAsync<ExecutionException>(() => source.WriteBatches(Batches(), append: true));

        Assert.Equal(1, transport.AcceptedRows);
        Assert.Contains("reconcile", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("synthetic-secret", error.Message);
    }

    private static async IAsyncEnumerable<DataTable> Batches()
    {
        var batch = new DataTable();
        batch.SetColumns(["id"]);
        var row = batch.NewRow();
        row["id"] = 12345678901234567890.12345678m;
        await batch.AddRowAsync(row);
        yield return batch;
    }

    private sealed class TransportFactory(HttpMessageHandler handler) : Google.Apis.Http.IHttpClientFactory
    {
        public ConfigurableHttpClient CreateHttpClient(CreateHttpClientArgs args)
        {
            var client = new ConfigurableHttpClient(new ConfigurableMessageHandler(handler));
            foreach (var initializer in args.Initializers) initializer.Initialize(client);
            return client;
        }
    }

    private sealed class InsertTransport(bool transportFailure, bool successfulResponse = false, bool rowErrors = false) : HttpMessageHandler
    {
        public int AcceptedRows { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.EndsWith("/insertAll", request.RequestUri!.AbsolutePath);
            await using var raw = await request.Content!.ReadAsStreamAsync(cancellationToken);
            await using var input = request.Content.Headers.ContentEncoding.Contains("gzip")
                ? new System.IO.Compression.GZipStream(raw, System.IO.Compression.CompressionMode.Decompress)
                : raw;
            using var body = await System.Text.Json.JsonDocument.ParseAsync(input, cancellationToken: cancellationToken);
            var row = body.RootElement.GetProperty("rows")[0];
            Assert.Equal("12345678901234567890.12345678", row.GetProperty("json").GetProperty("id").GetString());
            Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("insertId").GetString()));
            AcceptedRows++;
            if (AcceptedRows == 1 && transportFailure)
                throw new HttpRequestException("synthetic-secret");
            var status = AcceptedRows == 1 && !successfulResponse ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
            var json = status == HttpStatusCode.ServiceUnavailable
                ? "{\"error\":{\"code\":503,\"message\":\"synthetic-secret\"}}"
                : rowErrors ? "{\"insertErrors\":[{\"index\":0,\"errors\":[{\"reason\":\"invalid\",\"message\":\"synthetic-secret\"}]}]}" : "{}";
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}
