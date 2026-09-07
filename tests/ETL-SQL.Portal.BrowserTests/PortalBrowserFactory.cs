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

    protected override void CustomizeConfiguration(Dictionary<string, string?> settings)
    {
        settings["Governance:ConnectionCatalog:Provider"] = "Portal";
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Every step is named, because of how this fails when it fails. If anything in here throws,
        // WebApplicationFactory leaves its server unset and every subsequent test in the assembly
        // dies on "The server has not been started or no web application was configured" — one real
        // cause reported 178 times, naming none of the conditions and pointing at the fixture rather
        // than at whatever actually went wrong. That message has already sent two investigations
        // down the wrong path. Rethrowing with the failed step and the original exception attached
        // costs nothing on the passing path and is the difference between a diagnosis and a guess.
        var step = "build the TestServer host";
        try
        {
            // Build the TestServer host first: once the builder is switched to Kestrel below, the
            // factory's own in-memory client would otherwise be pointed at a server it cannot use.
            var testHost = builder.Build();

            step = "switch the builder to Kestrel and build the loopback host";
            builder.ConfigureWebHost(webHost => webHost.UseKestrel().UseUrls("http://127.0.0.1:0"));
            kestrelHost = builder.Build();

            step = "start the Kestrel host on 127.0.0.1:0";
            kestrelHost.Start();

            step = "read the OS-assigned port from Kestrel";
            var addresses = kestrelHost.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()
                ?? throw new InvalidOperationException("Kestrel did not expose a server addresses feature.");
            ServerAddress = addresses.Addresses.Last();
            ClientOptions.BaseAddress = new Uri(ServerAddress);

            step = "start the TestServer host";
            testHost.Start();
            return testHost;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"The Portal browser host could not be created — failed while trying to {step}. "
                + "Every test in this assembly will report 'The server has not been started' after "
                + $"this; that message is the symptom, and this is the cause: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// Stops the Kestrel host and <b>waits for it</b> before anything else is torn down.
    ///
    /// <para><c>IHost.Dispose()</c> does not stop a running host — it signals shutdown and returns.
    /// Disposing without stopping left Kestrel still unbinding its loopback port and still holding
    /// the shared SQLite files while <c>base.DisposeAsync</c> deleted the temp directory underneath
    /// it. The next run in the same process then failed to start at all, and every test in the class
    /// reported <c>The server has not been started</c> in about a millisecond. Stopping first makes
    /// teardown ordered instead of racing.</para>
    /// </summary>
    public override async ValueTask DisposeAsync()
    {
        if (kestrelHost is not null)
        {
            // Bounded, so a wedged host fails the run rather than hanging it.
            try { await kestrelHost.StopAsync(TimeSpan.FromSeconds(15)); }
            catch (OperationCanceledException) { }
            kestrelHost.Dispose();
            kestrelHost = null;
        }

        // Deletes the shared temp directory, so it must run after the Kestrel host has released it.
        await base.DisposeAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Synchronous disposal cannot await the stop, so callers should prefer DisposeAsync.
            // Kept working rather than throwing, because xunit disposes fixtures either way.
            kestrelHost?.StopAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
            kestrelHost?.Dispose();
            kestrelHost = null;
        }

        base.Dispose(disposing);
    }
}
