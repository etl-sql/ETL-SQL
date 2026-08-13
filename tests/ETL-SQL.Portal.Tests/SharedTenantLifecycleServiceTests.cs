using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Portal;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Middleware;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ETL_SQL.Portal.Tests;

public sealed class SharedTenantLifecycleServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"etlsql-portal-lifecycle-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task ProvisionPersistsActiveAssignmentNamespacesAndIdempotentReceipt()
    {
        await using var db = await DatabaseAsync();
        var remote = new FakeRemote();
        var service = new SharedTenantLifecycleService(db, Config(), remote);
        var now = DateTimeOffset.UtcNow;
        var authority = Authority(SharedTenantLifecycleKind.Provision, "tenant-alpha", "change-p", now);

        var first = await service.ApplyAsync(authority, true, now);
        var replay = await service.ApplyAsync(authority, true, now);

        Assert.Equal("Completed", first.Status);
        Assert.Equal(first.OperationId, replay.OperationId);
        Assert.Equal(1, remote.Calls);
        Assert.Equal("Active", (await db.SharedTenantLifecycles.SingleAsync()).State);
        Assert.Equal(new[] { "index", "queue", "storage" },
            await db.SharedTenantResources.OrderBy(x => x.Kind).Select(x => x.Kind).ToArrayAsync());
        Assert.All(await db.SharedTenantResources.ToArrayAsync(),
            value => Assert.StartsWith("tenant-alpha/", value.ScopedId, StringComparison.Ordinal));
        var identity = Assert.Single(await db.SharedIdentityAuthorities.ToArrayAsync());
        Assert.Equal("alpha.example.test", identity.PortalHost);
        Assert.Equal("SECRET:alpha-oidc", identity.ClientSecretReference);
        Assert.Single(await db.SharedTenantLifecycleOperations.ToArrayAsync());
    }

    [Fact]
    public async Task UncertainOrchestratorOutcomeRemainsFencedAndReplaysToCompletion()
    {
        await using var db = await DatabaseAsync();
        var remote = new FakeRemote { UnavailableCalls = 1 };
        var service = new SharedTenantLifecycleService(db, Config(), remote);
        var now = DateTimeOffset.UtcNow;
        var authority = Authority(SharedTenantLifecycleKind.Provision, "tenant-alpha", "change-p", now);

        var uncertain = await service.ApplyAsync(authority, true, now);
        Assert.Equal("Pending", uncertain.Status);
        Assert.Equal("Provisioning", (await db.SharedTenantLifecycles.SingleAsync()).State);

        var completed = await service.ApplyAsync(authority, true, now.AddSeconds(1));
        Assert.Equal("Completed", completed.Status);
        Assert.Equal("Active", (await db.SharedTenantLifecycles.SingleAsync()).State);
        Assert.Equal(2, remote.Calls);
    }

    [Fact]
    public async Task DeletePurgesOnlyAuthorizedPortalPartitionAndKeepsExternalTombstone()
    {
        await using var db = await DatabaseAsync();
        db.SharedTenantLifecycles.AddRange(
            Lifecycle("tenant-alpha"), Lifecycle("tenant-beta"));
        db.SharedTenantResources.AddRange(
            Resource("tenant-alpha", "same"), Resource("tenant-beta", "same"));
        db.PortalSecrets.AddRange(
            new PortalSecret { TenantId = "tenant-alpha", Name = "same", EncryptedValue = "a" },
            new PortalSecret { TenantId = "tenant-beta", Name = "same", EncryptedValue = "b" });
        await db.SaveChangesAsync();
        var now = DateTimeOffset.UtcNow;
        var service = new SharedTenantLifecycleService(db, Config(), new FakeRemote());

        var result = await service.ApplyAsync(
            Authority(SharedTenantLifecycleKind.Delete, "tenant-alpha", "change-d", now), true, now);

        Assert.Equal("Deleted", result.Status == "Completed" ? result.Phase : result.Status);
        Assert.Empty(await db.SharedTenantResources.Where(x => x.TenantId == "tenant-alpha").ToArrayAsync());
        Assert.Empty(await db.PortalSecrets.Where(x => x.TenantId == "tenant-alpha").ToArrayAsync());
        Assert.Single(await db.SharedTenantResources.Where(x => x.TenantId == "tenant-beta").ToArrayAsync());
        Assert.Single(await db.PortalSecrets.Where(x => x.TenantId == "tenant-beta").ToArrayAsync());
        Assert.Equal("Deleted", (await db.SharedTenantLifecycles.FindAsync("tenant-alpha"))!.State);
        Assert.Equal("Active", (await db.SharedTenantLifecycles.FindAsync("tenant-beta"))!.State);
        Assert.Equal("Completed", Assert.Single(await db.SharedTenantLifecycleOperations.ToArrayAsync()).Status);
    }

    [Fact]
    public async Task UpgradeFencesNewAccessAndWaitsForPortalWorkBeforeCallingOrchestrator()
    {
        await using var db = await DatabaseAsync();
        db.SharedTenantLifecycles.Add(Lifecycle("tenant-alpha"));
        db.Users.Add(new PortalUser
        {
            Id = 101,
            TenantId = "tenant-alpha",
            UserName = "alpha",
            NormalizedUserName = "ALPHA"
        });
        db.Folders.Add(new Folder { Id = 201, OwnerId = 101, Name = "root", Path = "/alpha" });
        db.Reports.Add(new Report
        {
            Id = 301,
            FolderId = 201,
            CreatedBy = 101,
            Name = "report",
            ScriptPath = "report.rptsql"
        });
        db.PortalExecutionJobs.Add(new PortalExecutionJob
        {
            Id = "run-alpha",
            ReportId = 301,
            UserId = 101,
            Status = "Running"
        });
        await db.SaveChangesAsync();
        var remote = new FakeRemote();
        var service = new SharedTenantLifecycleService(db, Config(), remote);
        var now = DateTimeOffset.UtcNow;
        var authority = Authority(
            SharedTenantLifecycleKind.Upgrade, "tenant-alpha", "change-u", now) with
        {
            TargetRelease = "release-2"
        };

        var draining = await service.ApplyAsync(authority, true, now);
        Assert.Equal("PortalDrain", draining.Phase);
        Assert.Equal(0, remote.Calls);
        Assert.Equal("Upgrading", (await db.SharedTenantLifecycles.SingleAsync()).State);

        (await db.PortalExecutionJobs.SingleAsync()).Status = "Completed";
        await db.SaveChangesAsync();
        var completed = await service.ApplyAsync(authority, true, now.AddSeconds(1));
        Assert.Equal("Completed", completed.Status);
        Assert.Equal("release-2", completed.ActiveRelease);
        Assert.Equal(1, remote.Calls);
    }

    [Fact]
    public void SignedAuthorityTenantExpiryRetentionAndServerOwnedProvisioningAreEnforced()
    {
        var now = DateTimeOffset.UtcNow;
        var config = Config();
        var onboarding = Policy(onboarding: true, deletion: false, now);
        var resolved = SharedTenantLifecycleService.ResolveAuthority(
            SharedTenantLifecycleKind.Provision, "tenant-alpha", onboarding, config, now);
        Assert.Equal(config.SharedTenancy.DefaultRelease, resolved.TargetRelease);
        Assert.Equal(config.SharedTenancy.DefaultMaxConcurrentJobs, resolved.MaxConcurrentJobs);

        Assert.Throws<UnauthorizedAccessException>(() =>
            SharedTenantLifecycleService.ResolveAuthority(
                SharedTenantLifecycleKind.Provision, "tenant-beta", onboarding, config, now));
        Assert.Throws<UnauthorizedAccessException>(() =>
            SharedTenantLifecycleService.ResolveAuthority(
                SharedTenantLifecycleKind.Provision, "tenant-alpha", onboarding, config, now.AddHours(2)));
        Assert.Throws<UnauthorizedAccessException>(() =>
            SharedTenantLifecycleService.ResolveAuthority(
                SharedTenantLifecycleKind.Delete, "tenant-alpha",
                Policy(onboarding: false, deletion: true, now, retention: now.AddMinutes(1)),
                config, now));
    }

    [Fact]
    public async Task AuthenticatedTenantRequestsFailClosedUnlessLifecycleIsActive()
    {
        await using var db = await DatabaseAsync();
        var config = Config();
        var accessor = new RequestTenantContextAccessor(config);
        accessor.SetVerifiedCredential(TenantContext.FromVerifiedCredential("tenant-alpha"));
        var nextCalled = false;
        var middleware = new SharedTenantLifecycleMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var request = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", "alpha")], "test"))
        };

        await middleware.InvokeAsync(request, config, accessor, db);
        Assert.Equal(StatusCodes.Status423Locked, request.Response.StatusCode);
        Assert.False(nextCalled);

        db.SharedTenantLifecycles.Add(Lifecycle("tenant-alpha"));
        await db.SaveChangesAsync();
        request = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", "alpha")], "test"))
        };
        await middleware.InvokeAsync(request, config, accessor, db);
        Assert.True(nextCalled);
    }

    [Fact]
    public void LifecycleManagementBindingFailsStartupUnlessEveryAuthorityDependencyIsExplicit()
    {
        var valid = Config();
        valid.Orchestrator.ApiUrl = "https://orchestrator.example.test";
        valid.Orchestrator.ApiKey = new string('a', 32);
        valid.Orchestrator.IdentitySigningSecret = new string('s', 32);
        SharedTenantLifecycleConfiguration.Validate(valid);

        Assert.Throws<InvalidOperationException>(() =>
            SharedTenantLifecycleConfiguration.Validate(new PortalConfig
            {
                SharedTenancy = new SharedTenancyConfig
                {
                    Enabled = true,
                    LifecycleManagementKey = "too-short"
                }
            }));
        valid.Orchestrator.IdentitySigningSecret = null;
        Assert.Throws<InvalidOperationException>(() =>
            SharedTenantLifecycleConfiguration.Validate(valid));
    }

    private async Task<PortalDbContext> DatabaseAsync()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={_path}").Options;
        var db = new PortalDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static PortalConfig Config() => new()
    {
        SharedTenancy = new SharedTenancyConfig
        {
            Enabled = true,
            LifecycleManagementKey = new string('k', 40),
            DefaultRelease = "release-1",
            DefaultMaxConcurrentJobs = 3,
            DefaultMaxStorageMb = 2048,
            DefaultMaxReportSessions = 4
        }
    };

    private static SharedTenantLifecycleAuthority Authority(
        SharedTenantLifecycleKind kind, string tenant, string reference, DateTimeOffset now)
    {
        var grant = PlatformAccessGrant.Issue(
            tenant, "platform-operator", reference, "lifecycle test", now.AddHours(1), now);
        return new(kind, TenantContext.FromPlatformGrant(grant, now), grant.OperatorPrincipal,
            grant.AuthorizationReference, grant.Reason, grant.ExpiresUtc,
            kind == SharedTenantLifecycleKind.Delete ? null : "release-1",
            kind == SharedTenantLifecycleKind.Delete ? null : 3,
            kind == SharedTenantLifecycleKind.Delete ? null : 2048,
            kind == SharedTenantLifecycleKind.Delete ? null : 4,
            kind == SharedTenantLifecycleKind.Delete ? now.AddMinutes(-1) : null,
            kind == SharedTenantLifecycleKind.Delete,
            kind == SharedTenantLifecycleKind.Provision
                ? new SharedIdentityAuthorityDefinition(
                    "alpha.example.test", "example.test", "https://id.example.test/alpha",
                    "alpha-client", "SECRET:alpha-oidc")
                : null);
    }

    private static EffectiveEnterprisePolicy Policy(
        bool onboarding, bool deletion, DateTimeOffset now, DateTimeOffset? retention = null)
    {
        var document = new OrganizationPolicyDocument
        {
            SaasOnboarding = new SaasOnboardingAuthorizationPolicySection
            {
                Enabled = onboarding,
                TenantId = onboarding ? "tenant-alpha" : null,
                OperatorPrincipal = onboarding ? "operator" : null,
                AuthorizationReference = onboarding ? "change-p" : null,
                Reason = onboarding ? "provision" : null,
                ExpiresUtc = onboarding ? now.AddHours(1) : null,
                PortalHost = onboarding ? "alpha.example.test" : null,
                LoginDomain = onboarding ? "example.test" : null,
                Issuer = onboarding ? "https://id.example.test/alpha" : null,
                ClientId = onboarding ? "alpha-client" : null,
                ClientSecretReference = onboarding ? "SECRET:alpha-oidc" : null
            },
            SaasDeletion = new SaasDeletionAuthorizationPolicySection
            {
                Enabled = deletion,
                TenantId = deletion ? "tenant-alpha" : null,
                OperatorPrincipal = deletion ? "operator" : null,
                AuthorizationReference = deletion ? "change-d" : null,
                Reason = deletion ? "delete" : null,
                RetentionUntilUtc = deletion ? retention ?? now.AddMinutes(-1) : null,
                LegalHoldCleared = deletion,
                ExpiresUtc = deletion ? now.AddHours(1) : null
            }
        };
        return new EffectiveEnterprisePolicy(
            true, true, "Active", "test", "test", now, now.AddHours(1), now,
            document, new Dictionary<string, string?>());
    }

    private static SharedTenantLifecycle Lifecycle(string tenant) => new()
    {
        TenantId = tenant,
        State = "Active",
        ActiveRelease = "release-1",
        MaxConcurrentJobs = 3,
        MaxStorageMb = 2048,
        MaxReportSessions = 4
    };

    private static SharedTenantResource Resource(string tenant, string logical) => new()
    {
        TenantId = tenant,
        Kind = "storage",
        LogicalId = logical,
        ScopedId = $"{tenant}/storage/{logical}"
    };

    private sealed class FakeRemote : ISharedTenantLifecycleOrchestratorClient
    {
        public int Calls { get; private set; }
        public int UnavailableCalls { get; init; }

        public Task<HttpResponseMessage?> ApplySharedTenantLifecycleAsync(
            TenantContext platformTenant,
            SharedTenantLifecycleCommand command,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Calls <= UnavailableCalls) return Task.FromResult<HttpResponseMessage?>(null);
            var state = new SharedTenantControlPlaneState(
                platformTenant.Tenant.Value,
                command.Kind == SharedTenantLifecycleKind.Delete ? "Deleted" : "Active",
                command.TargetRelease, command.MaxConcurrentJobs, command.MaxStorageMb,
                command.MaxReportSessions, 2, command.NowUtc,
                command.Kind == SharedTenantLifecycleKind.Delete ? command.NowUtc : null, 2);
            var result = new SharedTenantLifecycleResult(
                command.OperationId, platformTenant.Tenant.Value, command.Kind,
                "Completed", state.State, 0, state);
            return Task.FromResult<HttpResponseMessage?>(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(result)
            });
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
    }
}
