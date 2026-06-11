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
        var secrets = new[] { config.Jwt.Secret }.Concat(config.Jwt.PreviousSecrets ?? []).ToArray();
        var secret = secrets[0];

        if ((config.Jwt.PreviousSecrets?.Length ?? 0) > 1)
        {
            Console.Error.WriteLine(
                "FATAL: Portal:Jwt:PreviousSecrets supports exactly one temporary previous key.");
            lifetime.StopApplication();
        }
        else if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
        {
            Console.Error.WriteLine(
                "FATAL: Portal:Jwt:Secret is missing or fewer than 32 characters. " +
                "Generate a strong secret with: GENERATE JWT_SECRET " +
                "or set the Portal__Jwt__Secret environment variable.");
            lifetime.StopApplication();
        }
        else if (secrets.Any(candidate =>
                     !string.IsNullOrWhiteSpace(candidate)
                     && (candidate.Length < 32 || KnownInsecureSecrets.Contains(candidate))))
        {
            Console.Error.WriteLine(
                "FATAL: A current or previous Portal JWT secret is fewer than 32 characters " +
                "or is a known insecure default. Generate strong secrets with GENERATE JWT_SECRET.");
            lifetime.StopApplication();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
