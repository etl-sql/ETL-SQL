using ETL_SQL.WorkstationEditor;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class StudioBrowserContractsGeneratorTests
{
    [Fact]
    public async Task GeneratedRoutesMatchBothRegisteredHosts()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ETL-SQL.slnx")))
            root = root.Parent;
        Assert.NotNull(root);

        using var portal = new HostedPortalFactory();
        await using var desktop = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            root.FullName, null, 0, true, Guid.NewGuid().ToString("N"), StudioMode: true));
        var portalEndpoints = portal.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>();
        var desktopEndpoints = ((IEndpointRouteBuilder)desktop).DataSources
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>();
        var expected = StudioBrowserContractsGenerator.Generate(portalEndpoints, desktopEndpoints);
        var path = Path.Combine(root.FullName, "src", "ETL-SQL.ReportRuntime", "Resources", "TypeScript",
            "designer", "studio-routes.generated.ts");
        if (Environment.GetEnvironmentVariable("ETLSQL_UPDATE_BROWSER_CONTRACTS") == "1")
        {
            await File.WriteAllTextAsync(path, expected);
            return;
        }
        Assert.True(File.Exists(path), "Generate Studio routes with ETLSQL_UPDATE_BROWSER_CONTRACTS=1 and the BrowserContractsGeneratorTests filter.");
        Assert.Equal(expected, (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData("/api/renamed", "POST")]
    [InlineData("/api/example", "GET")]
    public void SharedRouteMismatchFailsForPathOrHttpMethod(string path, string method)
    {
        var shared = StudioBrowserContractsGenerator.ReadRoutes([Endpoint("/api/example", "POST", "Studio.Shared.example")], "Shared");
        Assert.Throws<InvalidOperationException>(() =>
            StudioBrowserContractsGenerator.VerifySharedRoutes(shared, [Endpoint(path, method)]));
    }

    [Fact]
    public void MissingNamedEndpointAndDuplicateKeysFail()
    {
        Assert.Throws<InvalidOperationException>(() => StudioBrowserContractsGenerator.ReadRoutes([], "Shared"));
        Assert.Throws<InvalidOperationException>(() => StudioBrowserContractsGenerator.ReadRoutes([
            Endpoint("/one", "GET", "Studio.Shared.example"),
            Endpoint("/two", "GET", "Studio.Shared.example")], "Shared"));
    }

    private static RouteEndpoint Endpoint(string path, string method, string? name = null)
    {
        var builder = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse(path), 0);
        builder.Metadata.Add(new HttpMethodMetadata([method]));
        if (name is not null) builder.Metadata.Add(new RouteNameMetadata(name));
        return (RouteEndpoint)builder.Build();
    }
}
