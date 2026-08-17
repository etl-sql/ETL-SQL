using ETL_SQL.Core.Data;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Execution;

namespace ETL_SQL.Tests.Orchestration;

public sealed class SandboxWorkloadPolicyResolverTests
{
    [Fact]
    public void ResolvesDefaultProfileAndTenantOwnedAdmissionLimits()
    {
        var resolved = Resolver().Resolve(Job(), Tenant("tenant-a"));

        Assert.Equal("shared", resolved.ProfileName);
        Assert.Equal(SandboxIsolationTier.Hardened, resolved.RequiredIsolationTier);
        Assert.Equal("shared-hardened", resolved.AdmissionPolicy.PoolId);
        Assert.Equal(3, resolved.AdmissionPolicy.TenantWeight);
        Assert.Equal(2, resolved.AdmissionPolicy.MaxConcurrentAttempts);
        Assert.Equal(7, resolved.AdmissionPolicy.MaxQueuedAttempts);
        Assert.Equal(1024, resolved.Limits.MaxMemoryBytes);
    }

    [Fact]
    public async Task CongestionNeverDowngradesPlacementOrIsolation()
    {
        // The Shared HA clause: a busy fleet must not quietly serve Hardened work from somewhere
        // cheaper. Capacity is not an input to resolution, and a full pool queues rather than
        // spilling into another one — so the only way to change a tenant's tier is to change the
        // server-owned catalog.
        var resolver = Resolver();
        var admission = new FairShareSandboxAdmissionController(new SandboxAdmissionControllerOptions
        {
            PoolCapacities = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["shared-hardened"] = 1,
                ["shared-standard"] = 8
            }
        });

        var resolved = resolver.Resolve(Job(), Tenant("tenant-a"));
        var held = await admission.AcquireAsync(Tenant("tenant-a"), resolved.AdmissionPolicy);

        // The hardened pool is now full while a roomy standard pool sits next to it.
        var underPressure = resolver.Resolve(Job(), Tenant("tenant-a"));
        Assert.Equal(resolved.ProfileName, underPressure.ProfileName);
        Assert.Equal(SandboxIsolationTier.Hardened, underPressure.RequiredIsolationTier);
        Assert.Equal("shared-hardened", underPressure.AdmissionPolicy.PoolId);

        var queued = admission.AcquireAsync(Tenant("tenant-a"), underPressure.AdmissionPolicy).AsTask();
        await Task.Delay(100); // flaky-delay-ok: proves the attempt waits for its own tier's capacity
        Assert.False(queued.IsCompleted, "Hardened work must queue rather than take a lesser pool.");

        await held.ReleaseAsync();
        var admitted = await queued.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("shared-hardened", admitted.PoolId);
        await admitted.ReleaseAsync();
    }

    [Fact]
    public void JobMaySelectOnlyAnEntitledNamedProfile()
    {
        var resolver = Resolver();
        var dedicated = resolver.Resolve(
            Job("{\"SandboxProfile\":\"dedicated\",\"MaxProcesses\":999}", "tenant-b"),
            Tenant("tenant-b"));

        Assert.Equal("dedicated", dedicated.ProfileName);
        Assert.Equal(SandboxIsolationTier.Dedicated, dedicated.RequiredIsolationTier);
        Assert.Equal("tenant-b-dedicated", dedicated.AdmissionPolicy.PoolId);
        Assert.Equal(4, dedicated.Limits.MaxProcesses);

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(
            Job("{\"SandboxProfile\":\"dedicated\"}", "tenant-a"), Tenant("tenant-a")));
    }

    [Fact]
    public void UnknownTenantAndProfileFailClosed()
    {
        var resolver = Resolver();

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(Job(tenantId: "tenant-c"), Tenant("tenant-c")));
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(
            Job("{\"SandboxProfile\":\"missing\"}", "tenant-b"), Tenant("tenant-b")));
    }

    [Fact]
    public void UnboundOrCrossTenantJobCannotEnterSandboxResolution()
    {
        var resolver = Resolver();
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(
            Job() with { TenantId = null }, Tenant("tenant-a")));
        Assert.Throws<UnauthorizedAccessException>(() => resolver.Resolve(
            Job(tenantId: "tenant-a"), Tenant("tenant-b")));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("not-json")]
    [InlineData("{\"SandboxProfile\":1}")]
    [InlineData("{\"SandboxProfile\":\"shared\",\"sandboxprofile\":\"dedicated\"}")]
    public void MalformedOrAmbiguousSchedulerMetadataFailsClosed(string options)
    {
        Assert.Throws<InvalidOperationException>(() => Resolver().Resolve(Job(options), Tenant("tenant-a")));
    }

    private static SandboxWorkloadPolicyResolver Resolver() => new(new SandboxWorkloadPolicyCatalog
    {
        Profiles = new Dictionary<string, SandboxExecutionProfile>
        {
            ["shared"] = Profile("shared-hardened", SandboxIsolationTier.Hardened, memory: 1024, processes: 2),
            ["dedicated"] = Profile("tenant-b-dedicated", SandboxIsolationTier.Dedicated, memory: 2048, processes: 4)
        },
        Tenants = new Dictionary<string, SandboxTenantAdmissionPolicy>
        {
            ["tenant-a"] = TenantPolicy("shared", ["shared"], 3, 2, 7),
            ["tenant-b"] = TenantPolicy("shared", ["shared", "dedicated"], 1, 1, 3)
        }
    });

    private static SandboxExecutionProfile Profile(
        string pool,
        SandboxIsolationTier tier,
        long memory,
        int processes) => new()
        {
            PoolId = pool,
            IsolationTier = tier,
            Limits = new SandboxResourceLimits
            {
                MaxDuration = TimeSpan.FromMinutes(5),
                MaxMemoryBytes = memory,
                MaxScratchBytes = 4096,
                MaxProcesses = processes,
                MaxCpuCores = 1,
                MaxConnectorConcurrency = 4
            }
        };

    private static SandboxTenantAdmissionPolicy TenantPolicy(
        string defaultProfile,
        string[] profiles,
        int weight,
        int concurrent,
        int queued) => new()
        {
            DefaultProfile = defaultProfile,
            AllowedProfiles = profiles,
            Weight = weight,
            MaxConcurrentAttempts = concurrent,
            MaxQueuedAttempts = queued
        };

    private static TenantContext Tenant(string tenantId) => TenantContext.FromVerifiedCredential(tenantId);

    private static JobDefinition Job(string? options = null, string tenantId = "tenant-a") =>
        new("job", "SELECT 1;", 1, "HOUR", null, null, null, Options: options, TenantId: tenantId);
}
