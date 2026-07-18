using ETL_SQL.Portal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Hosted-service integration lane (P2.1): a portal host that KEEPS the full
/// <c>IHostedService</c> pipeline — session cache, execution job service (instance locks +
/// interrupted-job recovery), Orchestrator poller, JWT/dataset-key startup validators, and
/// refresh-token maintenance — against the same isolated temp-directory databases as
/// <see cref="PortalWebFactory"/>.
///
/// Defaults make the lane test-friendly without changing semantics: a valid dataset at-rest key
/// (the validator is fatal without one), and one-second poll/purge intervals so loop behavior is
/// observable within a test timeout. Configuration and the clock are injectable so tests can
/// drive startup validation and maintenance decisions deterministically.
/// </summary>
public sealed class HostedPortalFactory(
    Action<Dictionary<string, string?>>? settings = null,
    Action<PortalConfig>? portalConfig = null,
    TimeProvider? clock = null) : PortalWebFactory
{
    /// <summary>Valid 32-byte base64 at-rest key used as the hosted-lane default.</summary>
    internal static string DefaultAtRestKey { get; } =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());

    protected override void CustomizeConfiguration(Dictionary<string, string?> settingsDict)
    {
        settingsDict["Portal:Dataset:AtRestKey"] = DefaultAtRestKey;
        settingsDict["Portal:Orchestrator:PollIntervalSeconds"] = "1";
        settingsDict["Portal:Jwt:RefreshTokenPurgeIntervalSeconds"] = "1";
        settings?.Invoke(settingsDict);
    }

    protected override void CustomizePortalConfig(PortalConfig config)
    {
        config.Dataset.AtRestKey = DefaultAtRestKey;
        config.Orchestrator.PollIntervalSeconds = 1;
        config.Jwt.RefreshTokenPurgeIntervalSeconds = 1;
        portalConfig?.Invoke(config);
    }

    protected override void ConfigureHostedServices(IServiceCollection services)
    {
        // Keep every hosted service registered by Program.cs. Only the clock is replaceable.
        if (clock is not null)
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(clock);
        }
    }
}

/// <summary>
/// Minimal controlled clock: a fixed "now" with real timers, so loop pacing stays real
/// (fast, via the shortened lane intervals) while time-based decisions are deterministic.
/// </summary>
internal sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
