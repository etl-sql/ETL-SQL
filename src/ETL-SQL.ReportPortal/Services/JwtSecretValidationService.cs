using Microsoft.Extensions.Hosting;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// Validates the JWT secret at startup. Runs after WebApplicationFactory can inject test config,
/// avoiding the "early return 1" problem that prevents WebApplicationFactory from capturing the host.
/// Note: PortalWebFactory removes all IHostedService registrations, so this never runs in tests.
/// </summary>
public class JwtSecretValidationService(PortalConfig config, IHostApplicationLifetime lifetime)
    : IHostedService
{
    // Secrets that must never be used in any environment — kept here as a safety net
    // in case an operator restores a known-bad value from an old config or commit.
    private static readonly HashSet<string> KnownInsecureSecrets =
    [
        "SuperSecretKeyThatIsAtLeast32CharactersLong!!"
    ];

    public Task StartAsync(CancellationToken ct)
    {
        var secret = config.Jwt.Secret;

        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
        {
            Console.Error.WriteLine(
                "FATAL: Portal:Jwt:Secret is missing or fewer than 32 characters. " +
                "Generate a strong secret with: GENERATE JWT_SECRET " +
                "or set the Portal__Jwt__Secret environment variable.");
            lifetime.StopApplication();
        }
        else if (KnownInsecureSecrets.Contains(secret))
        {
            Console.Error.WriteLine(
                "FATAL: Portal:Jwt:Secret is set to a known insecure default. " +
                "Generate a strong secret with: GENERATE JWT_SECRET " +
                "or set the Portal__Jwt__Secret environment variable.");
            lifetime.StopApplication();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
