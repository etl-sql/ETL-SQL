using ETL_SQL.WorkstationEditor;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
        BrowserContractsGeneratorStaging.EnsureHostRunning(portal.Services, "Portal");
        await desktop.StartAsync();
        BrowserContractsGeneratorStaging.EnsureHostRunning(desktop.Services, "WorkstationEditor");
        var portalEndpoints = portal.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>();
        var desktopEndpoints = ((IEndpointRouteBuilder)desktop).DataSources
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>();
        var expected = StudioBrowserContractsGenerator.Generate(portalEndpoints, desktopEndpoints);
        var path = Path.Combine(root.FullName, "src", "ETL-SQL.ReportRuntime", "Resources", "TypeScript",
            "designer", "studio-routes.generated.ts");
        if (Environment.GetEnvironmentVariable("ETLSQL_UPDATE_BROWSER_CONTRACTS") == "1")
        {
            path = BrowserContractsGeneratorStaging.OutputPath(root.FullName,
                "src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-routes.generated.ts");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, expected);
            return;
        }
        Assert.True(File.Exists(path), "Generate Studio routes with node scripts/generate-browser-contracts.mjs.");
        Assert.Equal(expected, (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData("jwt")]
    [InlineData("dataset")]
    public void FailedStartupValidationCannotSupplyRoutes(string validation)
    {
        using var portal = new HostedPortalFactory(portalConfig: config =>
        {
            if (validation == "jwt") config.Jwt.Secret = "short";
            else config.Dataset.AtRestKey = null;
        });
        var error = Assert.Throws<InvalidOperationException>(() =>
            BrowserContractsGeneratorStaging.EnsureHostRunning(portal.Services, "Portal"));
        Assert.Contains("host failed startup validation", error.Message);
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

internal static class BrowserContractsGeneratorStaging
{
    internal static void EnsureHostRunning(IServiceProvider services, string host)
    {
        var lifetime = services.GetRequiredService<IHostApplicationLifetime>();
        if (!lifetime.ApplicationStarted.IsCancellationRequested || lifetime.ApplicationStopping.IsCancellationRequested)
            throw new InvalidOperationException($"{host} host failed startup validation or began stopping. Browser contracts cannot be certified.");
    }

    internal static string OutputPath(string root, string relative)
    {
        var directory = Environment.GetEnvironmentVariable("ETLSQL_BROWSER_CONTRACTS_STAGING");
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Generate the complete contract set with node scripts/generate-browser-contracts.mjs.");
        var artifactRoot = Path.GetFullPath(Path.Combine(root, "artifacts")) + Path.DirectorySeparatorChar;
        directory = Path.GetFullPath(directory);
        if (!directory.StartsWith(artifactRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("Browser contract staging must be inside the repository artifacts directory.");
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException("Browser contract staging cannot use symlinks or junctions.");
        }
        return Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
    }
}
