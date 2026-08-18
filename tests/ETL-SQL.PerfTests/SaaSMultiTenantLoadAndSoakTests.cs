using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Portal;
using ETL_SQL.Portal.Controllers;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using ETL_SQL.Portal.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.PerfTests;

public sealed class SaaSPortalWebFactory : PortalWebFactory
{
    public const string ManagementKey = "platform-secret-management-key-at-least-32-chars-long";

    protected override void CustomizePortalConfig(PortalConfig config)
    {
        config.SharedTenancy = new SharedTenancyConfig
        {
            Enabled = true,
            LifecycleManagementKey = ManagementKey,
            DefaultRelease = "v0.18.0",
            DefaultMaxConcurrentJobs = 10,
            DefaultMaxStorageMb = 20480,
            DefaultMaxReportSessions = 50
        };
    }
}

[Trait("Category", "Performance")]
public sealed class SaaSMultiTenantLoadAndSoakTests : IDisposable
{
    private readonly SaaSPortalWebFactory _factory = new();
    private const string ManagementKey = SaaSPortalWebFactory.ManagementKey;

    [Fact(Timeout = 60_000)]
    public async Task SustainedMultiTenantConcurrency_FairShareThroughputAndFairness()
    {
        // Arrange 10 concurrent tenants
        const int tenantCount = 10;
        const int opsPerTenant = 20;
        var tenants = Enumerable.Range(1, tenantCount).Select(i => $"tenant-fairness-{i:D2}").ToList();

        using var client = _factory.CreateClient();

        // Seed tenants into DB
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            foreach (var t in tenants)
            {
                db.SharedTenantLifecycles.Add(new SharedTenantLifecycle
                {
                    TenantId = t,
                    State = "Active",
                    ActiveRelease = "v0.18.0",
                    MaxConcurrentJobs = 10,
                    MaxStorageMb = 10240,
                    MaxReportSessions = 20
                });
            }
            await db.SaveChangesAsync();
        }

        var tenantProgress = new ConcurrentDictionary<string, int>();
        var latencies = new ConcurrentBag<double>();
        var errors = new ConcurrentBag<string>();

        var sw = Stopwatch.StartNew();

        // Act: Run concurrent tenant operations across all 10 tenants
        await Parallel.ForEachAsync(tenants, new ParallelOptions { MaxDegreeOfParallelism = 10 }, async (tenantId, ct) =>
        {
            for (int i = 0; i < opsPerTenant; i++)
            {
                var opSw = Stopwatch.StartNew();
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/platform/control-plane/tenants/{tenantId}/health");
                    req.Headers.Add(SharedTenantLifecycleController.ManagementKeyHeader, ManagementKey);

                    using var res = await client.SendAsync(req, ct);
                    opSw.Stop();
                    latencies.Add(opSw.Elapsed.TotalMilliseconds);

                    if (res.IsSuccessStatusCode)
                    {
                        tenantProgress.AddOrUpdate(tenantId, 1, (_, current) => current + 1);
                    }
                    else
                    {
                        errors.Add($"{tenantId}: status {res.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{tenantId}: {ex.Message}");
                }
            }
        });

        sw.Stop();

        // Assert: 100% success rate
        Assert.Empty(errors);
        Assert.Equal(tenantCount, tenantProgress.Count);
        Assert.All(tenantProgress.Values, count => Assert.Equal(opsPerTenant, count));

        // Compute Jain's Fairness Index: J = (sum(x_i))^2 / (n * sum(x_i^2))
        var values = tenantProgress.Values.Select(v => (double)v).ToArray();
        double sum = values.Sum();
        double sumSq = values.Select(v => v * v).Sum();
        double jainsIndex = (sum * sum) / (tenantCount * sumSq);

        // Under fair-share scheduling, Jain's index must be >= 0.95
        Assert.True(jainsIndex >= 0.95, $"Fairness index was {jainsIndex:F3}, expected >= 0.95");

        // Latency assertions
        var sorted = latencies.OrderBy(x => x).ToList();
        var p50 = sorted[(int)(sorted.Count * 0.50)];
        var p95 = sorted[(int)(sorted.Count * 0.95)];
        var p99 = sorted[(int)(sorted.Count * 0.99)];

        Assert.True(p50 < 100, $"p50 latency was {p50:F1}ms, expected < 100ms");
        Assert.True(p99 < 500, $"p99 latency was {p99:F1}ms, expected < 500ms");
    }

    [Fact(Timeout = 60_000)]
    public async Task NoisyNeighborContainment_FloodedTenantDoesNotStarveQuietTenants()
    {
        const string aggressiveTenant = "tenant-noisy-aggressor";
        var quietTenants = new[] { "tenant-quiet-1", "tenant-quiet-2", "tenant-quiet-3" };

        using var client = _factory.CreateClient();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.SharedTenantLifecycles.Add(new SharedTenantLifecycle
            {
                TenantId = aggressiveTenant,
                State = "Active",
                ActiveRelease = "v0.18.0",
                MaxConcurrentJobs = 5,
                MaxStorageMb = 5120,
                MaxReportSessions = 10
            });
            foreach (var t in quietTenants)
            {
                db.SharedTenantLifecycles.Add(new SharedTenantLifecycle
                {
                    TenantId = t,
                    State = "Active",
                    ActiveRelease = "v0.18.0",
                    MaxConcurrentJobs = 10,
                    MaxStorageMb = 10240,
                    MaxReportSessions = 20
                });
            }
            await db.SaveChangesAsync();
        }

        var quietLatencies = new ConcurrentBag<double>();
        var aggressiveSuccessCount = 0;
        var quietSuccessCount = 0;
        var errors = new ConcurrentBag<string>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // Background aggressor flooding 100 rapid requests
        var aggressorTask = Task.Run(async () =>
        {
            for (int i = 0; i < 100 && !cts.IsCancellationRequested; i++)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/platform/control-plane/tenants/{aggressiveTenant}/health");
                req.Headers.Add(SharedTenantLifecycleController.ManagementKeyHeader, ManagementKey);
                using var res = await client.SendAsync(req, cts.Token);
                if (res.IsSuccessStatusCode) Interlocked.Increment(ref aggressiveSuccessCount);
            }
        });

        // Concurrent quiet tenants issuing requests
        var quietTask = Parallel.ForEachAsync(quietTenants, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = cts.Token }, async (t, ct) =>
        {
            for (int i = 0; i < 15; i++)
            {
                var sw = Stopwatch.StartNew();
                using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/platform/control-plane/tenants/{t}/health");
                req.Headers.Add(SharedTenantLifecycleController.ManagementKeyHeader, ManagementKey);
                using var res = await client.SendAsync(req, ct);
                sw.Stop();

                quietLatencies.Add(sw.Elapsed.TotalMilliseconds);
                if (res.IsSuccessStatusCode) Interlocked.Increment(ref quietSuccessCount);
                else errors.Add($"{t}: {res.StatusCode}");

                await Task.Delay(20, ct);
            }
        });

        await Task.WhenAll(aggressorTask, quietTask);

        // Assert: quiet tenants experienced zero errors and low latency
        Assert.Empty(errors);
        Assert.Equal(quietTenants.Length * 15, quietSuccessCount);

        var quietMaxLatency = quietLatencies.Max();
        Assert.True(quietMaxLatency < 500, $"Quiet tenant max latency was {quietMaxLatency:F1}ms during flood, expected < 500ms");
    }

    [Fact(Timeout = 90_000)]
    public async Task SustainedSoak_MemoryAndResourceStability()
    {
        using var client = _factory.CreateClient();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            for (int i = 1; i <= 5; i++)
            {
                db.SharedTenantLifecycles.Add(new SharedTenantLifecycle
                {
                    TenantId = $"tenant-soak-{i}",
                    State = "Active",
                    ActiveRelease = "v0.18.0",
                    MaxConcurrentJobs = 10,
                    MaxStorageMb = 10240,
                    MaxReportSessions = 20
                });
            }
            await db.SaveChangesAsync();
        }

        // Baseline memory
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long initialMemory = GC.GetTotalMemory(forceFullCollection: true);

        const int totalSoakOps = 500;
        int completedOps = 0;

        await Parallel.ForEachAsync(Enumerable.Range(1, totalSoakOps), new ParallelOptions { MaxDegreeOfParallelism = 10 }, async (i, ct) =>
        {
            var tenantId = $"tenant-soak-{(i % 5) + 1}";
            using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/platform/control-plane/tenants/{tenantId}/health");
            req.Headers.Add(SharedTenantLifecycleController.ManagementKeyHeader, ManagementKey);
            using var res = await client.SendAsync(req, ct);
            if (res.IsSuccessStatusCode) Interlocked.Increment(ref completedOps);
        });

        Assert.Equal(totalSoakOps, completedOps);

        // Post-soak memory check
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long postSoakMemory = GC.GetTotalMemory(forceFullCollection: true);

        long growthBytes = postSoakMemory - initialMemory;
        long growthMb = growthBytes / (1024 * 1024);

        // Managed memory growth after 500 ops should be well bounded (< 30 MB)
        Assert.True(growthMb < 30, $"Memory grew by {growthMb} MB ({growthBytes} bytes) after soak test, expected < 30 MB");
    }

    [Fact(Timeout = 60_000)]
    public async Task ControlPlaneFleetObservability_HighTenantDensity()
    {
        using var client = _factory.CreateClient();

        const int tenantDensity = 50;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            for (int i = 1; i <= tenantDensity; i++)
            {
                db.SharedTenantLifecycles.Add(new SharedTenantLifecycle
                {
                    TenantId = $"tenant-density-{i:D3}",
                    State = (i % 5 == 0) ? "Maintenance" : "Active",
                    ActiveRelease = "v0.18.0",
                    MaxConcurrentJobs = 10,
                    MaxStorageMb = 20480,
                    MaxReportSessions = 50
                });
            }
            await db.SaveChangesAsync();
        }

        // Measure overview and tenant list endpoints under density
        var overviewSw = Stopwatch.StartNew();
        using var overviewReq = new HttpRequestMessage(HttpMethod.Get, "/api/platform/control-plane/overview");
        overviewReq.Headers.Add(SharedTenantLifecycleController.ManagementKeyHeader, ManagementKey);
        using var overviewRes = await client.SendAsync(overviewReq);
        overviewSw.Stop();

        Assert.True(overviewRes.IsSuccessStatusCode);
        Assert.True(overviewSw.ElapsedMilliseconds < 200, $"Overview latency was {overviewSw.ElapsedMilliseconds}ms, expected < 200ms");

        var tenantsSw = Stopwatch.StartNew();
        using var tenantsReq = new HttpRequestMessage(HttpMethod.Get, "/api/platform/control-plane/tenants");
        tenantsReq.Headers.Add(SharedTenantLifecycleController.ManagementKeyHeader, ManagementKey);
        using var tenantsRes = await client.SendAsync(tenantsReq);
        tenantsSw.Stop();

        Assert.True(tenantsRes.IsSuccessStatusCode);
        var tenantList = await tenantsRes.Content.ReadFromJsonAsync<List<ControlPlaneTenantDto>>();
        Assert.NotNull(tenantList);
        Assert.Equal(tenantDensity, tenantList.Count);
        Assert.True(tenantsSw.ElapsedMilliseconds < 300, $"Tenants query latency was {tenantsSw.ElapsedMilliseconds}ms, expected < 300ms");
    }

    public void Dispose()
    {
        _factory.Dispose();
    }
}
