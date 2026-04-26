using Microsoft.Extensions.Hosting;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// Validates the JWT secret at startup. Runs after WebApplicationFactory can inject test config,
/// avoiding the "early return 1" problem that prevents WebApplicationFactory from capturing the host.
/// </summary>
public class JwtSecretValidationService(PortalConfig config, IHostApplicationLifetime lifetime)
    : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.Jwt.Secret) || config.Jwt.Secret.Length < 32)
        {
            Console.Error.WriteLine(
                "FATAL: Portal:Jwt:Secret is missing or fewer than 32 characters. " +
                "Set a strong secret in appsettings.json or via environment variable " +
                "Portal__Jwt__Secret before starting the portal.");
            lifetime.StopApplication();
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
