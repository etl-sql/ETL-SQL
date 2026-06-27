using ETL_SQL.Orchestrator.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public class JobThrottleBackoffTests
{
    [Fact]
    public void PostgresWithoutConnectionString_FailsClosed()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orchestrator:Database:Provider"] = "Postgres"
            })
            .Build();

        var error = Assert.Throws<InvalidOperationException>(() => new JobThrottle(
            Options.Create(new JobThrottleOptions()),
            NullLogger<JobThrottle>.Instance,
            configuration));

        Assert.Contains("ConnectionString", error.Message);
    }

    [Fact]
    public void CalculatePollDelay_UsesExponentialBackoffAndCap()
    {
        using var throttle = new JobThrottle(
            Options.Create(new JobThrottleOptions
            {
                MaxConcurrentJobs = 1,
                PollInitialDelayMs = 100,
                PollMaxDelayMs = 800,
                PollJitterRatio = 0
            }),
            NullLogger<JobThrottle>.Instance);

        Assert.Equal(100, throttle.CalculatePollDelay(0).TotalMilliseconds);
        Assert.Equal(200, throttle.CalculatePollDelay(1).TotalMilliseconds);
        Assert.Equal(400, throttle.CalculatePollDelay(2).TotalMilliseconds);
        Assert.Equal(800, throttle.CalculatePollDelay(3).TotalMilliseconds);
        Assert.Equal(800, throttle.CalculatePollDelay(20).TotalMilliseconds);
    }

    [Fact]
    public void CalculatePollDelay_AppliesBoundedJitter()
    {
        using var throttle = new JobThrottle(
            Options.Create(new JobThrottleOptions
            {
                MaxConcurrentJobs = 1,
                PollInitialDelayMs = 100,
                PollMaxDelayMs = 1000,
                PollJitterRatio = 0.2
            }),
            NullLogger<JobThrottle>.Instance);

        for (var i = 0; i < 100; i++)
        {
            var delay = throttle.CalculatePollDelay(0).TotalMilliseconds;
            Assert.InRange(delay, 80, 120);
        }
    }
}
