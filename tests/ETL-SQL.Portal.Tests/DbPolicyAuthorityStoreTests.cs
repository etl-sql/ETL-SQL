using System.Security.Cryptography;
using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class DbPolicyAuthorityStoreTests : IDisposable
{
    private readonly string _scratch =
        Path.Combine(Path.GetTempPath(), "policy_authority_" + Guid.NewGuid().ToString("N")[..8]);

    public DbPolicyAuthorityStoreTests() => Directory.CreateDirectory(_scratch);
    public void Dispose() { try { Directory.Delete(_scratch, recursive: true); } catch { } }

    private async Task<PortalDbContext> NewDbAsync()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_scratch, "portal.db")}")
            .Options;
        var db = new PortalDbContext(options);
        await db.Database.MigrateAsync();
        return db;
    }

    private static OrganizationPolicyDocument Doc() => new()
    {
        Filesystem = new FilesystemPolicySection { ApprovedRoots = [Path.GetTempPath().TrimEnd('\\', '/')] }
    };

    [Fact]
    public async Task Publish_Supersede_Retrieve_RoundTripsThroughTheDatabase()
    {
        await using var db = await NewDbAsync();
        using var signer = new RsaPolicyEnvelopeSigner(RSA.Create(2048));
        var svc = new PolicyAuthorityService(new DbPolicyAuthorityStore(db), signer);

        await svc.PublishAsync(Doc(), "acme", "prod", "1.0.0", "alice", null, DateTimeOffset.UtcNow.AddDays(30));
        await svc.PublishAsync(Doc(), "acme", "prod", "1.1.0", "alice", "bob", DateTimeOffset.UtcNow.AddDays(30));

        // Active is the latest; history is retained immutably; and the served envelope verifies.
        var active = await svc.RetrieveActiveEnvelopeAsync("acme", "prod");
        Assert.Equal("1.1.0", active!.PolicyVersion);

        var enrollment = new EnterpriseEnrollmentDocument
        {
            Tenant = "acme",
            PolicyEndpoint = "https://policy.example.test/etl-sql",
            PolicySigningPublicKey = signer.PublicKeyPem
        };
        var parsed = EnterprisePolicySignature.VerifyAndParse(active, enrollment, DateTimeOffset.UtcNow);
        Assert.NotEmpty(parsed.Filesystem.ApprovedRoots);

        var rows = await db.PolicyVersions.Where(x => x.Tenant == "acme").ToListAsync();
        Assert.Equal(2, rows.Count);                                                  // append-only history
        Assert.Single(rows, r => r.RolloutState == nameof(PolicyRolloutState.Active)); // one active
        Assert.Single(rows, r => r.RolloutState == nameof(PolicyRolloutState.Superseded));
    }

    [Fact]
    public async Task Canary_CohortPersists_AndPromoteHaltRoundTripThroughTheDatabase()
    {
        await using var db = await NewDbAsync();
        using var signer = new RsaPolicyEnvelopeSigner(RSA.Create(2048));
        var svc = new PolicyAuthorityService(new DbPolicyAuthorityStore(db), signer);

        await svc.PublishAsync(Doc(), "acme", "prod", "1.0.0", "alice", null, DateTimeOffset.UtcNow.AddDays(30));
        await svc.PublishCanaryAsync(Doc(), "acme", "prod", "1.1.0-canary", "alice", null,
            DateTimeOffset.UtcNow.AddDays(30), CanaryCohort.ForPercentage(25));

        // The cohort survives the EF round-trip, and the canary does not become the fleet active.
        var canary = await svc.GetCanaryVersionAsync("acme", "prod");
        Assert.Equal(25, canary!.Canary!.Percentage);
        Assert.Equal("1.0.0", (await svc.GetActiveVersionAsync("acme", "prod"))!.PolicyVersion);
        var canaryRow = await db.PolicyVersions.SingleAsync(x => x.PolicyVersion == "1.1.0-canary");
        Assert.Equal(25, canaryRow.CanaryPercentage);
        Assert.Null(canaryRow.CanaryGroup);

        // Promote makes it fleet-wide; a fresh canary can then be halted back through the DB store.
        await svc.PromoteCanaryAsync("acme", "prod", "1.1.0-canary");
        Assert.Equal("1.1.0-canary", (await svc.GetActiveVersionAsync("acme", "prod"))!.PolicyVersion);
        Assert.Null(await svc.GetCanaryVersionAsync("acme", "prod"));

        await svc.PublishCanaryAsync(Doc(), "acme", "prod", "1.2.0-canary", "alice", null,
            DateTimeOffset.UtcNow.AddDays(30), CanaryCohort.ForGroup("ring0"));
        await svc.HaltCanaryAsync("acme", "prod", "1.2.0-canary", "carol", null);
        Assert.Null(await svc.GetCanaryVersionAsync("acme", "prod"));
        var halted = await db.PolicyVersions.SingleAsync(x => x.PolicyVersion == "1.2.0-canary");
        Assert.Equal(nameof(PolicyRolloutState.RolledBack), halted.RolloutState);
        Assert.Equal("ring0", halted.CanaryGroup);
    }
}
