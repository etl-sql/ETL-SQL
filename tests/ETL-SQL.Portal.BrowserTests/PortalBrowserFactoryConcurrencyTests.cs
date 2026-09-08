using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// Guards the failure that reported itself as 178 tests dying on
/// <c>The server has not been started or no web application was configured</c>.
///
/// <para><see cref="PortalBrowserFactory.CreateHost"/> builds one <c>IHostBuilder</c> twice, because
/// the lane needs both the factory's in-memory <c>TestServer</c> and a real loopback socket. That
/// builder is a <c>DeferredHostBuilder</c>: each <c>Build()</c> re-enters the Portal entry point and
/// captures the resulting host through process-global diagnostic-listener state. Two factories
/// building at the same time interleave four such captures and some of them come back with a
/// <c>TestServer</c> that was never started — which is the message above, thrown later from
/// <c>Services</c> or <c>CreateClient</c> rather than from <c>CreateHost</c>, so the step-naming
/// wrapper reports nothing.</para>
///
/// <para>Measured before the fix: single-build factories ran 6/6 clean in parallel and the
/// double-build factory ran 4/4 clean serially, but the double-build factory in parallel failed 3
/// of 6. The lane creates five collection Portals concurrently, so this is that shape exactly.</para>
/// </summary>
public class PortalBrowserFactoryConcurrencyTests
{
    [Fact]
    public async Task Concurrently_created_factories_each_get_a_started_host()
    {
        const int count = 6;
        var failures = new string?[count];

        await Task.WhenAll(Enumerable.Range(0, count).Select(i => Task.Run(async () =>
        {
            PortalBrowserFactory? factory = null;
            try
            {
                factory = new PortalBrowserFactory();
                _ = factory.CreateClient();

                // Asserts the mechanism, not the noun: the resolved IServer must be the TestServer
                // the factory hands to CreateClient, and it must have been started — CreateHandler()
                // is what throws "The server has not been started" when it was not.
                var server = Assert.IsType<TestServer>(factory.Services.GetRequiredService<IServer>());
                Assert.NotNull(server.CreateHandler());
                Assert.StartsWith("http://127.0.0.1:", factory.ServerAddress);
            }
            catch (Exception ex)
            {
                failures[i] = $"[{i}] {ex.GetType().Name}: {ex.Message.Split('\n')[0]}";
            }
            finally
            {
                if (factory is not null) await factory.DisposeAsync();
            }
        })));

        var reported = failures.Where(f => f is not null).ToArray();
        Assert.True(
            reported.Length == 0,
            $"{reported.Length} of {count} concurrently created Portal hosts failed:\n" + string.Join("\n", reported));
    }
}
