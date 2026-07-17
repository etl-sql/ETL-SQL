using ETL_SQL.Core.Observability;
using Microsoft.Extensions.Hosting;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Validates the JWT secret at startup. Runs after WebApplicationFactory can inject test config,
/// avoiding the "early return 1" problem that prevents WebApplicationFactory from capturing the host.
/// Note: PortalWebFactory removes all IHostedService registrations for ordinary API tests; the
/// hosted-service lane (HostedPortalFactory) keeps this service and asserts both outcomes.
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
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = BackgroundServiceObservability.StartRun(
            "portal", "jwt-secret-validation", "startup_validation");
        var secrets = new[] { config.Jwt.Secret }.Concat(config.Jwt.PreviousSecrets ?? []).ToArray();
        var secret = secrets[0];
        var status = "success";

        if ((config.Jwt.PreviousSecrets?.Length ?? 0) > 1)
        {
            status = "failure";
            Console.Error.WriteLine(
                "FATAL: Portal:Jwt:PreviousSecrets supports exactly one temporary previous key.");
            lifetime.StopApplication();
        }
        else if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
        {
            status = "failure";
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
            status = "failure";
            Console.Error.WriteLine(
                "FATAL: A current or previous Portal JWT secret is fewer than 32 characters " +
                "or is a known insecure default. Generate strong secrets with GENERATE JWT_SECRET.");
            lifetime.StopApplication();
        }

        sw.Stop();
        BackgroundServiceObservability.CompleteRun(
            activity,
            "portal",
            "jwt-secret-validation",
            "startup_validation",
            status,
            sw.ElapsedMilliseconds);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
