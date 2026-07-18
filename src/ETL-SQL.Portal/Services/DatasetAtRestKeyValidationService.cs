using ETL_SQL.Core.Observability;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Enforces the portal at-rest key configuration at startup (mirrors <see cref="JwtSecretValidationService"/>):
/// a missing/weak key shuts the app down before serving requests, unless Portal:Dataset:AllowMachineFallback
/// is explicitly set for dev/standalone. The rules live in the pure <see cref="DatasetAtRestKeyValidator"/>;
/// the hosted-service lane (HostedPortalFactory) runs this service in-host and asserts both outcomes.
/// </summary>
public class DatasetAtRestKeyValidationService(
    PortalConfig config,
    IHostApplicationLifetime lifetime,
    ILogger<DatasetAtRestKeyValidationService> log) : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = BackgroundServiceObservability.StartRun(
            "portal", "dataset-key-validation", "startup_validation");
        var result = DatasetAtRestKeyValidator.Validate(config.Dataset);
        var status = result.Severity switch
        {
            DatasetAtRestKeyValidator.Severity.Fatal => "failure",
            DatasetAtRestKeyValidator.Severity.Warn => "warning",
            _ => "success"
        };

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

        sw.Stop();
        BackgroundServiceObservability.CompleteRun(
            activity,
            "portal",
            "dataset-key-validation",
            "startup_validation",
            status,
            sw.ElapsedMilliseconds);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
