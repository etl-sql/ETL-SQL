using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class PortalWebFactoryDisposalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PairedOrchestrator_ShutdownLeavesThePortalDirectoryUntilItsOwnerDisposes(bool asynchronous)
    {
        var portal = new PortalWebFactory();
        var tempDirectory = portal.TempDir;
        try
        {
            using var portalClient = portal.CreateClient();
            var orchestrator = new OrchestratorWebFactory(tempDirectory);
            using var orchestratorClient = orchestrator.CreateClient();
            if (asynchronous) await orchestrator.DisposeAsync();
            else orchestrator.Dispose();
            Assert.True(Directory.Exists(tempDirectory));
            Assert.True(File.Exists(Path.Combine(tempDirectory, "portal.db")));
            Assert.True((await portalClient.GetAsync("/healthz")).IsSuccessStatusCode);
        }
        finally
        {
            await portal.DisposeAsync();
        }
        Assert.False(Directory.Exists(tempDirectory));
    }

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

    [Fact]
    public async Task InFlightExecutionJob_CancelsAndCleansUpTempDirOnDisposal()
    {
        var factory = new PortalWebFactory();
        var tempDirectory = factory.TempDir;
        try
        {
            var scriptPath = Path.Combine(tempDirectory, "scripts", "long_running.rptsql");
            Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
            await File.WriteAllTextAsync(scriptPath, """
                WAITFOR DELAY '00:00:10';
                CREATE VISUAL V AS CARD (
                    SOURCE = (SELECT 1 AS X),
                    MAPPINGS (VALUE = X)
                );
                """);

            var jobs = factory.Services.GetRequiredService<ETL_SQL.Portal.Services.ExecutionJobService>();
            var jobId = await jobs.EnqueueExecutionAsync(
                reportId: 1,
                userId: 1,
                scriptPath: scriptPath,
                isAdministrator: true);

            Assert.False(string.IsNullOrWhiteSpace(jobId));

            // Wait until the job starts running before disposing the factory
            await ETL_SQL.TestSupport.LoadAwareWait.UntilAsync(
                $"Execution job '{jobId}' to start",
                async _ => await jobs.GetAsync(jobId),
                job => job?.Status == ETL_SQL.Portal.Services.JobStatus.Running,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(50));
        }
        finally
        {
            await factory.DisposeAsync();
        }

        Assert.False(Directory.Exists(tempDirectory), $"Disposal left factory files at {tempDirectory}.");
    }
}
