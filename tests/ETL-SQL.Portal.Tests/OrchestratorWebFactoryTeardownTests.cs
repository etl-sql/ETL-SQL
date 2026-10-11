using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Scheduling;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// A manually triggered run is a detached task. The hosted service waits for it at shutdown, but
/// fixtures that remove hosted services to keep the scheduling loop quiet remove that wait too, and
/// the run was still writing to <c>etlsql.db</c> when the factory deleted its directory. The file
/// is only locked while the run is inside a store call, so the lock itself cannot be staged; the
/// test asserts what prevents it, that no run is still active once disposal returns.
/// </summary>
public class OrchestratorWebFactoryTeardownTests
{
    [Fact]
    public async Task Dispose_WaitsForATriggeredRunBeforeDeletingItsDirectory()
    {
        var factory = new QuietOrchestratorFactory();
        var directory = factory.TempDir;
        SchedulerService scheduler;
        try
        {
            using var client = factory.CreateClient();
            using var create = Authorized(HttpMethod.Post, "/api/scheduled-jobs", new
            {
                Name = "teardown-run",
                ScriptText = "WAITFOR DELAY '00:00:02';",
                Interval = 10,
                Unit = "MINUTE",
                MaxRetries = 0,
                RetryDelaySeconds = 30,
                HashPolicy = "Warn"
            });
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(create)).StatusCode);
            using var trigger = Authorized(HttpMethod.Post, "/api/scheduled-jobs/teardown-run/trigger");
            Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(trigger)).StatusCode);

            scheduler = factory.Services.GetRequiredService<SchedulerService>();
            var store = factory.Services.GetRequiredService<IJobHistoryStore>();
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!(await store.GetHistoryAsync("teardown-run", 5)).Any(entry => entry.Status == "RUNNING"))
            {
                Assert.True(DateTime.UtcNow < deadline, "The triggered run never started.");
                await Task.Delay(25);
            }
        }
        finally
        {
            // The run is mid-script here. Disposal must wait for it, then leave nothing behind.
            await factory.DisposeAsync();
        }

        Assert.Equal(0, scheduler.GetMetrics().ActiveJobs);
        Assert.False(Directory.Exists(directory), "The factory must remove its directory once the run has finished.");
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Orchestrator-Key", "test-orch-key-12345");
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private sealed class QuietOrchestratorFactory : OrchestratorWebFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
        }
    }
}
