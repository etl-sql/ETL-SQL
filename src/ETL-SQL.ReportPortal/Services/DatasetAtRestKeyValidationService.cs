using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// Enforces the portal at-rest key configuration at startup (mirrors <see cref="JwtSecretValidationService"/>):
/// a missing/weak key shuts the app down before serving requests, unless Portal:Dataset:AllowMachineFallback
/// is explicitly set for dev/standalone. PortalWebFactory removes all IHostedService registrations, so this
/// never runs in tests — the rules live in the pure <see cref="DatasetAtRestKeyValidator"/>.
/// </summary>
public class DatasetAtRestKeyValidationService(
    PortalConfig config,
    IHostApplicationLifetime lifetime,
    ILogger<DatasetAtRestKeyValidationService> log) : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        var result = DatasetAtRestKeyValidator.Validate(config.Dataset);

        switch (result.Severity)
        {
            case DatasetAtRestKeyValidator.Severity.Fatal:
                Console.Error.WriteLine("FATAL: " + result.Message);
                lifetime.StopApplication();
                break;
            case DatasetAtRestKeyValidator.Severity.Warn:
                log.LogWarning("{Message}", result.Message);
                break;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
