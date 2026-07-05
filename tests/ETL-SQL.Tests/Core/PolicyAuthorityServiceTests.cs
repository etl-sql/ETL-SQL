using System.Security.Cryptography;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// Phase 3.1 policy-authority core: validate, version, sign, publish, supersede, retrieve, and roll
/// back organization policies. The critical proof is round-trip — an envelope the authority publishes
/// must verify and parse through the client-side <see cref="EnterprisePolicySignature"/>.
/// </summary>
public sealed class PolicyAuthorityServiceTests
{
    private static (PolicyAuthorityService svc, RsaPolicyEnvelopeSigner signer) NewAuthority()
    {
        var signer = new RsaPolicyEnvelopeSigner(RSA.Create(2048));
        return (new PolicyAuthorityService(new InMemoryPolicyAuthorityStore(), signer), signer);
    }

    private static OrganizationPolicyDocument SampleDoc(string root = null!) => new()
    {
        Filesystem = new FilesystemPolicySection
        {
            ApprovedRoots = [root ?? System.IO.Path.GetTempPath().TrimEnd('\\', '/')]
        }
    };

    private static EnterpriseEnrollmentDocument Enrollment(string tenant, string signingPublicKeyPem) => new()
    {
        Tenant = tenant,
        PolicyEndpoint = "https://policy.example.test/etl-sql",
        PolicySigningPublicKey = signingPublicKeyPem
    };

    [Fact]
    public async Task Published_Envelope_VerifiesAndParsesThroughTheClient()
    {
        var (svc, signer) = NewAuthority();

        await svc.PublishAsync(SampleDoc(), "acme", "prod", "1.0.0", "alice", "bob",
            DateTimeOffset.UtcNow.AddDays(30));

        var envelope = await svc.RetrieveActiveEnvelopeAsync("acme", "prod");
        Assert.NotNull(envelope);

        // The client accepts it: signature verifies against the enrollment's public key and parses.
        var document = EnterprisePolicySignature.VerifyAndParse(
            envelope!, Enrollment("acme", signer.PublicKeyPem), DateTimeOffset.UtcNow);
        Assert.NotEmpty(document.Filesystem.ApprovedRoots);
    }

    [Fact]
    public async Task Publishing_NewVersion_SupersedesPriorAndIssuesLater()
    {
        var (svc, _) = NewAuthority();

        var v1 = await svc.PublishAsync(SampleDoc(), "acme", "prod", "1.0.0", "alice", null,
            DateTimeOffset.UtcNow.AddDays(30));
        var v2 = await svc.PublishAsync(SampleDoc(), "acme", "prod", "1.1.0", "alice", "bob",
            DateTimeOffset.UtcNow.AddDays(30));

        Assert.True(v2.IssuedAtUtc > v1.IssuedAtUtc, "A superseding version must issue strictly later.");
        Assert.Equal("1.0.0", v2.SupersededVersion);

        var active = await svc.RetrieveActiveEnvelopeAsync("acme", "prod");
        Assert.Equal("1.1.0", active!.PolicyVersion);
    }

    [Fact]
    public async Task Rollback_RepublishesTargetWithFreshIssuance()
    {
        var (svc, _) = NewAuthority();

        await svc.PublishAsync(SampleDoc("/only/v1/root".Replace('/', System.IO.Path.DirectorySeparatorChar)),
            "acme", "prod", "1.0.0", "alice", null, DateTimeOffset.UtcNow.AddDays(30));
        var v2 = await svc.PublishAsync(SampleDoc(), "acme", "prod", "2.0.0", "alice", null,
            DateTimeOffset.UtcNow.AddDays(30));

        var rolled = await svc.RollbackToAsync("acme", "prod", "1.0.0", "1.0.1-rollback",
            "carol", "dave", DateTimeOffset.UtcNow.AddDays(30));

        Assert.Equal(PolicyRolloutState.Active, rolled.RolloutState);
        Assert.True(rolled.IssuedAtUtc > v2.IssuedAtUtc, "Rollback issues later than the version it replaces.");
        var active = await svc.RetrieveActiveEnvelopeAsync("acme", "prod");
        Assert.Equal("1.0.1-rollback", active!.PolicyVersion);
    }

    [Fact]
    public async Task Publish_RejectsDuplicateVersion_InvalidDocument_AndPastExpiry()
    {
        var (svc, _) = NewAuthority();
        await svc.PublishAsync(SampleDoc(), "acme", "prod", "1.0.0", "alice", null,
            DateTimeOffset.UtcNow.AddDays(30));

        await Assert.ThrowsAsync<PolicyAuthorityException>(() => svc.PublishAsync(
            SampleDoc(), "acme", "prod", "1.0.0", "alice", null, DateTimeOffset.UtcNow.AddDays(30)));

        var invalid = new OrganizationPolicyDocument
        {
            Filesystem = new FilesystemPolicySection { ApprovedRoots = ["relative\\not\\absolute"] }
        };
        await Assert.ThrowsAsync<PolicyAuthorityException>(() => svc.PublishAsync(
            invalid, "acme", "prod", "2.0.0", "alice", null, DateTimeOffset.UtcNow.AddDays(30)));

        await Assert.ThrowsAsync<PolicyAuthorityException>(() => svc.PublishAsync(
            SampleDoc(), "acme", "prod", "3.0.0", "alice", null, DateTimeOffset.UtcNow.AddDays(-1)));
    }

    [Fact]
    public async Task History_IsImmutableAndBoundToTenantEnvironment()
    {
        var store = new InMemoryPolicyAuthorityStore();
        var scoped = new PolicyAuthorityService(store, new RsaPolicyEnvelopeSigner(RSA.Create(2048)));

        await scoped.PublishAsync(SampleDoc(), "acme", "prod", "1.0.0", "alice", null, DateTimeOffset.UtcNow.AddDays(30));
        await scoped.PublishAsync(SampleDoc(), "acme", "prod", "1.1.0", "alice", null, DateTimeOffset.UtcNow.AddDays(30));
        await scoped.PublishAsync(SampleDoc(), "acme", "dev", "9.9.9", "alice", null, DateTimeOffset.UtcNow.AddDays(30));

        var prod = await store.ListAsync("acme", "prod");
        Assert.Equal(2, prod.Count);                          // both versions retained
        Assert.Single(prod, v => v.RolloutState == PolicyRolloutState.Active); // exactly one active
        var dev = await store.ListAsync("acme", "dev");
        Assert.Single(dev);                                   // separate environment, isolated
    }
}
