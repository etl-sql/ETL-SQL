using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class PortalWebFactoryDisposalTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task StartedFactory_RemovesItsFilesOnBothDisposalPaths(bool asynchronous, bool hostedServices, bool createClient)
    {
        // Repeat startup and shutdown so a passing first host cannot hide a retained database pool.
        for (var iteration = 0; iteration < 3; iteration++)
        {
            PortalWebFactory factory = hostedServices ? new HostedPortalFactory() : new PortalWebFactory();
            var tempDirectory = factory.TempDir;
            try
            {
                using var client = createClient ? factory.CreateClient() : null;
                using var scope = factory.Services.CreateScope();
                var database = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
                Assert.True(await database.Users.AnyAsync());
                Assert.True(File.Exists(Path.Combine(tempDirectory, "portal.db")));
                // Route discovery can keep a host alive long enough for the poller to open the
                // normalized Orchestrator path. Exercise that pool without relying on timer timing.
                var orchestratorPath = factory.Services.GetRequiredService<ETL_SQL.Portal.Services.OrchestratorDbLocator>().Resolve();
                var store = factory.Services.GetRequiredService<ETL_SQL.Orchestrator.Storage.IOrchestratorStoreFactory>().Create(orchestratorPath);
                await store.InitializeAsync();
            }
            finally
            {
                if (asynchronous)
                    await factory.DisposeAsync();
                else
                    factory.Dispose();
            }
            Assert.False(Directory.Exists(tempDirectory), $"Disposal left factory files at {tempDirectory}.");
        }
    }
}
