using System.Net;
using System.Net.Http.Json;
using ETL_SQL.Orchestrator.Channels;
using ETL_SQL.Reporting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// The Portal's ad-hoc job channel against a <b>federated</b> Orchestrator.
///
/// <para>This is the coverage whose absence hid a live break. The channel carries report execution
/// and data-quality submissions, and it sent the shared API key with no caller assertion — which a
/// federated Orchestrator refuses, because it requires both. Every existing <c>/jobs</c> auth test
/// constructed <c>new OrchestratorWebFactory()</c>, which defaults to <b>legacy</b> mode, so the
/// suite only ever proved the path where no assertion is required.</para>
///
/// <para>The Portal factory here re-registers the typed client to swap the transport onto the
/// Orchestrator host in this process, and deliberately does <b>not</b> re-add the identity handler:
/// it has to come from the production wiring in <c>Program.cs</c>, or the test proves a chain no
/// deployment uses.</para>
/// </summary>
[Trait("Category", "Portal")]
public sealed class OrchestratorJobChannelIdentityTests
{
    [Fact]
    public async Task ApiKeyWithoutAnAssertion_IsRefusedByAFederatedOrchestrator()
    {
        // Pins the requirement the channel has to satisfy. If this ever starts passing, the
        // Orchestrator has stopped requiring a caller and the test below proves much less.
        using var orchestrator = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var client = orchestrator.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/jobs")
        {
            Content = JsonContent.Create(new { ScriptText = "PRINT 'hi';" })
        };
        request.Headers.Add("X-Orchestrator-Key", "test-orch-key-12345");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task TheJobChannelSubmitsAndTheScriptRuns()
    {
        using var orchestrator = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var portal = new FederatedJobChannelPortalFactory(orchestrator);
        _ = portal.CreateClient(); // Realises the host so its services can be resolved.

        var channel = portal.Services.GetRequiredService<IJobChannel>();

        // Resolved outside a request, which is the report-execution-from-a-queue case: no HTTP
        // context, so the Portal signs as its own background identity. Before the handler existed
        // this threw on EnsureSuccessStatusCode with 401.
        var jobId = await channel.SubmitJobAsync(new JobSubmitRequest
        {
            ScriptText = "PRINT 'hi';",
            Label = "channel identity test"
        });
        Assert.False(string.IsNullOrWhiteSpace(jobId));

        JobStatusResponse? status = null;
        for (var attempt = 0; attempt < 80; attempt++)
        {
            status = await channel.GetStatusAsync(jobId);
            if (status.Status is JobRunStatus.Completed or JobRunStatus.Failed) break;
            await Task.Delay(25);
        }

        Assert.NotNull(status);
        Assert.Equal(JobRunStatus.Completed, status.Status);
    }

    private sealed class FederatedJobChannelPortalFactory(OrchestratorWebFactory orchestrator)
        : PortalWebFactory
    {
        // Environment variables, not the factory's in-memory settings, and the distinction is the
        // whole reason this wiring went untested. Program.cs binds its PortalConfig at its very
        // first lines and decides *there* whether to register the HTTP channel or the in-process
        // one. The factory's in-memory source is applied later, when the host is built, so a URL set
        // that way arrives after the decision — the remote path would silently never be exercised
        // and this test would prove a chain no deployment uses. An environment variable is in
        // configuration before the entry point runs.
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // The host name is never resolved: the test server's handler answers whatever it is sent.
            Environment.SetEnvironmentVariable("Portal__Orchestrator__ApiUrl", "http://orchestrator.test");
            Environment.SetEnvironmentVariable("Portal__Orchestrator__ApiKey", "test-orch-key-12345");
            Environment.SetEnvironmentVariable(
                "Portal__Orchestrator__IdentitySigningSecret", OrchestratorWebFactory.IdentitySecret);
            base.ConfigureWebHost(builder);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            // Process-wide state, so it is cleared even though the suite runs single-threaded:
            // leaving it set would silently point another test's Portal at an orchestrator.
            Environment.SetEnvironmentVariable("Portal__Orchestrator__ApiUrl", null);
            Environment.SetEnvironmentVariable("Portal__Orchestrator__ApiKey", null);
            Environment.SetEnvironmentVariable("Portal__Orchestrator__IdentitySigningSecret", null);
        }

        // The PortalConfig *singleton* is built as an object literal by the factory rather than
        // bound, and it is the one OrchestratorAssertionIssuer reads to sign with.
        protected override void CustomizePortalConfig(PortalConfig config)
        {
            config.Orchestrator.ApiUrl = "http://orchestrator.test";
            config.Orchestrator.ApiKey = "test-orch-key-12345";
            config.Orchestrator.IdentitySigningSecret = OrchestratorWebFactory.IdentitySecret;
        }

        protected override void CustomizeServices(IServiceCollection services)
        {
            // Re-configures the same named handler chain, so only the transport changes: the
            // identity handler registered by Program.cs stays in front of it.
            services.AddHttpClient<IJobChannel, HttpJobChannelClient>()
                .ConfigurePrimaryHttpMessageHandler(() => orchestrator.Server.CreateHandler());
        }
    }
}
