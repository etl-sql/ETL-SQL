using ETL_SQL.Orchestrator.Execution;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public class JobThrottleBackoffTests
{
    [Fact]
    public async Task AsyncSlotDisposal_CompletesReleaseBeforeReturningAndIsIdempotent()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"etlsql-throttle-disposal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "throttle.db");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Orchestrator:DatabasePath"] = databasePath
        }).Build();
        using var throttle = new JobThrottle(Options.Create(new JobThrottleOptions { MaxConcurrentJobs = 1 }),
            NullLogger<JobThrottle>.Instance, configuration);
        IDisposable? slot = null;
        try
        {
            slot = await throttle.AcquireAsync("shutdown");
            Assert.Equal(1, throttle.GetMetrics().ActiveJobs);
            var asyncSlot = Assert.IsAssignableFrom<IAsyncDisposable>(slot);
            await asyncSlot.DisposeAsync();
            await asyncSlot.DisposeAsync();
            slot.Dispose();
            Assert.Equal(0, throttle.GetMetrics().ActiveJobs);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Pooling = false
            }.ToString());
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM ThrottleSlots;";
            Assert.Equal(0L, await command.ExecuteScalarAsync());
        }
        finally
        {
            slot?.Dispose();
            using var connection = new SqliteConnection($"Data Source={databasePath}");
            SqliteConnection.ClearPool(connection);
            Directory.Delete(directory, recursive: true);
        }
    }

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
