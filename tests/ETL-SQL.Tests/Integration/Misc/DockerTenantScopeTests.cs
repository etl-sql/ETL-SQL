using Docker.DotNet;
using Docker.DotNet.Models;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Integration;

[Trait("Category", "Integration")]
[Trait("CompatBreak", "0.22")]
public class DockerTenantScopeTests
{
    [Fact]
    public async Task SameAliasInTwoTenantsKeepsRowsLastConnectionsAndLifecycleSeparate()
    {
        await using var services = (ServiceProvider)DependencyInjectionSetup.BuildServiceProvider();
        var first = services.GetRequiredService<Evaluator>();
        var second = services.GetRequiredService<Evaluator>();
        Assert.NotSame(first, second);
        var run = Guid.NewGuid().ToString("N");
        var firstTenant = "tenant-a-" + run;
        var secondTenant = "tenant-b-" + run;
        var alias = "scope_" + run[..12];
        first.ExecutionIdentity = Identity(firstTenant);
        second.ExecutionIdentity = Identity(secondTenant);
        var manager = services.GetRequiredService<IDockerManager>();
        Assert.Same(manager, first.DockerManager);
        Assert.Same(manager, second.DockerManager);
        try
        {
            await StartAndWriteAsync(first, alias, "tenant-a");
            await StartAndWriteAsync(second, alias, "tenant-b");

            // Rebind after the other tenant ran: a process-global "last" would read the wrong rows.
            await ExecuteAsync(first, """
                DECLARE @last_connection VARCHAR(1000) = DOCKER.CONNECTION_STRING;
                CREATE CONNECTION rebound AS POSTGRES(@last_connection);
                SELECT marker FROM rebound.tenant_marker;
                """);
            AssertMarker(first, "tenant-a");
            await ExecuteAsync(second, $"""
                DECLARE @named_connection VARCHAR(1000) = {alias}.CONNECTION_STRING;
                CREATE CONNECTION rebound AS POSTGRES(@named_connection);
                SELECT marker FROM rebound.tenant_marker;
                """);
            AssertMarker(second, "tenant-b");

            await ExecuteAsync(first, "SELECT marker FROM rebound.tenant_marker;");
            AssertMarker(first, "tenant-a");

            var endpoint = Environment.GetEnvironmentVariable("DOCKER_HOST")
                ?? (OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock");
            using var client = new DockerClientBuilder().WithEndpoint(new Uri(endpoint)).Build();
            var containers = await client.Containers.ListContainersAsync(new ContainersListParameters { All = true }, CancellationToken.None);
            var names = containers.SelectMany(container => container.Names)
                .Select(name => name.TrimStart('/'))
                .Where(name => name.EndsWith("_" + alias, StringComparison.Ordinal)).ToArray();
            Assert.Equal(2, names.Length);
            Assert.All(names, name =>
            {
                Assert.StartsWith("etlsql_", name, StringComparison.Ordinal);
                Assert.NotEqual("etlsql_" + alias, name);
            });

            await ExecuteAsync(first, $"CLOSE DOCKER {alias};");
            await ExecuteAsync(second, "SELECT marker FROM rebound.tenant_marker;");
            AssertMarker(second, "tenant-b");
        }
        finally
        {
            await manager.CloseContainers(firstTenant, cancellationToken: CancellationToken.None);
            await manager.CloseContainers(secondTenant, cancellationToken: CancellationToken.None);
        }
    }

    private static ExecutionIdentity Identity(string tenant) => new()
    {
        EffectiveUser = "scope-test",
        RealUser = "scope-test",
        IsAdmin = true,
        TenantId = tenant
    };

    private static async Task StartAndWriteAsync(Evaluator evaluator, string alias, string marker)
    {
        await ExecuteAsync(evaluator, $"""
            USE DOCKER('postgres:15-alpine') AS {alias};
            DECLARE @connection VARCHAR(1000) = {alias}.CONNECTION_STRING;
            CREATE CONNECTION isolated AS POSTGRES(@connection);
            CREATE TABLE isolated.tenant_marker (marker VARCHAR(30));
            INSERT INTO isolated.tenant_marker VALUES ('{marker}');
            SELECT marker FROM isolated.tenant_marker;
            """);
        AssertMarker(evaluator, marker);
    }

    private static Task ExecuteAsync(Evaluator evaluator, string source) =>
        evaluator.Evaluate(new Lexer(source).TokenizeToScript());

    private static void AssertMarker(Evaluator evaluator, string expected)
    {
        Assert.NotNull(evaluator.LastResult);
        Assert.Equal(expected, Assert.Single(evaluator.LastResult.Rows)["marker"]);
    }
}
