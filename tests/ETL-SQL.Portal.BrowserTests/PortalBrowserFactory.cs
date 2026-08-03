using ETL_SQL.Portal.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// A <see cref="PortalWebFactory"/> that additionally listens on a real loopback TCP port.
///
/// Why this exists: <c>WebApplicationFactory</c> serves requests through an in-memory
/// <c>TestServer</c>, which a browser cannot connect to. A browser lane needs a real socket, so this
/// builds the host twice — once as the TestServer host the factory internals require, once as a
/// Kestrel host bound to <c>127.0.0.1:0</c> (an OS-assigned free port, so parallel runs and
/// developer machines never collide). Both hosts share the same temp-directory SQLite databases and
/// script root, so anything seeded through <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}.CreateClient"/>
/// or through <c>Services</c> is visible to the browser.
///
/// Hosted services stay stripped (the <see cref="PortalWebFactory"/> default): report execution is
/// started directly by <c>ExecutionJobService.EnqueueExecutionAsync</c>, not by a background loop,
/// so the journey runs a report for real without two hosts racing over the same node leases,
/// instance locks and Orchestrator poller.
/// </summary>
public sealed class PortalBrowserFactory : PortalWebFactory
{
    private IHost? kestrelHost;

    /// <summary>Absolute base URL the browser should navigate to, e.g. <c>http://127.0.0.1:53124</c>.</summary>
    public string ServerAddress { get; private set; } = string.Empty;

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Build the TestServer host first: once the builder is switched to Kestrel below, the
        // factory's own in-memory client would otherwise be pointed at a server it cannot use.
        var testHost = builder.Build();

        builder.ConfigureWebHost(webHost => webHost.UseKestrel().UseUrls("http://127.0.0.1:0"));
        kestrelHost = builder.Build();
        kestrelHost.Start();

        var addresses = kestrelHost.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Kestrel did not expose a server addresses feature.");
        ServerAddress = addresses.Addresses.Last();
        ClientOptions.BaseAddress = new Uri(ServerAddress);

        testHost.Start();
        return testHost;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            kestrelHost?.Dispose();
            kestrelHost = null;
        }

        // Deletes the shared temp directory, so it must run after the Kestrel host has released it.
        base.Dispose(disposing);
    }
}
