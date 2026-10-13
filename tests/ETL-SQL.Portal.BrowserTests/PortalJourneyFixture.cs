using ETL_SQL.Portal.Services;
using ETL_SQL.Portal.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>Portal page journeys with a real, isolated Orchestrator behind the authenticated proxy.</summary>
public sealed class PortalJourneyFixture : PortalBrowserFixture
{
    private readonly OrchestratorWebFactory orchestrator;

    public PortalJourneyFixture() : this(new JourneyOrchestratorFactory()) { }

    internal PortalJourneyFixture(OrchestratorWebFactory orchestrator)
        : base(new JourneyPortalFactory(orchestrator)) => this.orchestrator = orchestrator;

    /// <summary>The Orchestrator behind this Portal, for acting as an operator and reading its store.</summary>
    public OrchestratorWebFactory Orchestrator => orchestrator;

    public override async Task InitializeAsync()
    {
        orchestrator.CreateClient().Dispose();
        await base.InitializeAsync();
    }

    public override async Task DisposeAsync()
    {
        try { await base.DisposeAsync(); }
        finally { await orchestrator.DisposeAsync(); }
    }

    private sealed class JourneyOrchestratorFactory() : OrchestratorWebFactory(requireFederatedIdentity: true)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            // Exercise service endpoints without scheduler loops changing shared journey state.
            builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
        }
    }

    private sealed class JourneyPortalFactory(OrchestratorWebFactory orchestrator) : PortalBrowserFactory
    {
        protected override void CustomizePortalConfig(PortalConfig config)
        {
            config.Orchestrator.ApiUrl = "http://orchestrator.test";
            config.Orchestrator.ApiKey = "test-orch-key-12345";
            config.Orchestrator.IdentitySigningSecret = OrchestratorWebFactory.IdentitySecret;
        }

        protected override void CustomizeServices(IServiceCollection services) =>
            services.AddHttpClient<OrchestratorProxyService>()
                .ConfigurePrimaryHttpMessageHandler(() => orchestrator.Server.CreateHandler());
    }
}
