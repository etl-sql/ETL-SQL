using System.Net;
using ETL_SQL.Core.Governance;
using Xunit;

namespace ETL_SQL.Tests.Core;

public class OrganizationPolicySourceTests
{
    [Fact]
    public async Task LocalProtectedSource_LoadsValidatedPolicyDocument()
    {
        var path = WritePolicyFile();
        var source = new LocalProtectedOrganizationPolicySource(
            path,
            new AllowProtectedFileValidator(),
            () => DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

        var result = await source.LoadAsync();

        Assert.Equal(path, result.Source);
        Assert.Equal("1.0", result.Document.SchemaVersion);
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T00:00:00Z"), result.LoadedAt);
    }

    [Fact]
    public async Task LocalProtectedSource_RejectsUnprotectedPolicyFile()
    {
        var source = new LocalProtectedOrganizationPolicySource(
            WritePolicyFile(),
            new RejectProtectedFileValidator());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => source.LoadAsync());
        Assert.Contains("not protected", ex.Message);
    }

    [Fact]
    public async Task HttpsSource_LoadsValidatedPolicyDocument()
    {
        using var http = new HttpClient(new StubHttpHandler(HttpStatusCode.OK, ValidPolicyJson()));
        var source = new HttpsOrganizationPolicySource(
            new Uri("https://policy.example.test/org-policy.json"),
            http,
            () => DateTimeOffset.Parse("2026-01-02T00:00:00Z"));

        var result = await source.LoadAsync();

        Assert.Equal("https://policy.example.test/org-policy.json", result.Source);
        Assert.Equal("1.0", result.Document.SchemaVersion);
        Assert.Equal(DateTimeOffset.Parse("2026-01-02T00:00:00Z"), result.LoadedAt);
    }

    [Fact]
    public async Task HttpsSource_RejectsHttpEndpoint()
    {
        using var http = new HttpClient(new StubHttpHandler(HttpStatusCode.OK, ValidPolicyJson()));
        var source = new HttpsOrganizationPolicySource(new Uri("http://policy.example.test/org-policy.json"), http);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => source.LoadAsync());
        Assert.Contains("HTTPS", ex.Message);
    }

    [Fact]
    public async Task Loader_FallsBackToNextConfiguredSource()
    {
        using var http = new HttpClient(new StubHttpHandler(HttpStatusCode.OK, ValidPolicyJson()));
        var sources = new IOrganizationPolicySource[]
        {
            new LocalProtectedOrganizationPolicySource(WritePolicyFile(), new RejectProtectedFileValidator()),
            new HttpsOrganizationPolicySource(new Uri("https://policy.example.test/org-policy.json"), http)
        };

        var result = await new OrganizationPolicyLoader(sources).LoadFirstAvailableAsync();

        Assert.Equal("https://policy.example.test/org-policy.json", result.Source);
    }

    [Fact]
    public void SourceFactory_CreatesLocalAndHttpsSources()
    {
        using var http = new HttpClient(new StubHttpHandler(HttpStatusCode.OK, ValidPolicyJson()));
        var factory = new OrganizationPolicySourceFactory(http, new AllowProtectedFileValidator());

        var sources = factory.Create(new OrganizationPolicySourceOptions
        {
            LocalPath = WritePolicyFile(),
            HttpsEndpoint = "https://policy.example.test/org-policy.json"
        });

        Assert.Equal(2, sources.Count);
    }

    private static string WritePolicyFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"org-policy-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, ValidPolicyJson());
        return path;
    }

    private static string ValidPolicyJson() => """
    {
      "schemaVersion": "1.0",
      "connectors": {
        "allowedTypes": [ "MSSQL" ]
      },
      "execution": {
        "allowedModes": [ "Batch" ],
        "maxParallelDegree": 4
      }
    }
    """;

    private sealed class AllowProtectedFileValidator : IProtectedPolicyFileValidator
    {
        public void ValidateProtectedFile(string path)
        {
        }
    }

    private sealed class RejectProtectedFileValidator : IProtectedPolicyFileValidator
    {
        public void ValidateProtectedFile(string path) =>
            throw new InvalidOperationException("Policy file is not protected.");
    }

    private sealed class StubHttpHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content)
            });
    }
}
