using System.Security.Cryptography;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// Canary policy rollout: a version served only to its cohort while the fleet keeps the active version,
/// with promote (canary → fleet-wide) and halt (revert the cohort). The load-bearing guarantees are
/// that the fleet is untouched until promotion, and that halt re-issues the active document with a
/// fresh (later) issuance so cohort machines — which reject older issuance — actually revert.
/// </summary>
public sealed class PolicyCanaryRolloutTests
{
    private sealed class TestClock
    {
        public DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public DateTimeOffset Get() => Now;
    }

    private static (PolicyAuthorityService svc, RsaPolicyEnvelopeSigner signer, TestClock clock) NewAuthority()
    {
        var clock = new TestClock();
        var signer = new RsaPolicyEnvelopeSigner(RSA.Create(2048));
        return (new PolicyAuthorityService(new InMemoryPolicyAuthorityStore(), signer, clock.Get), signer, clock);
    }

    private static OrganizationPolicyDocument SampleDoc(string? root = null) => new()
    {
        Filesystem = new FilesystemPolicySection
        {
            ApprovedRoots = [root ?? Path.GetTempPath().TrimEnd('\\', '/')]
        }
    };

    private static EnterpriseEnrollmentDocument Enrollment(string tenant, string signingPublicKeyPem) => new()
    {
        Tenant = tenant,
        PolicyEndpoint = "https://policy.example.test/etl-sql",
        PolicySigningPublicKey = signingPublicKeyPem
    };

    private static Task PublishActive(PolicyAuthorityService svc, string version = "1.0.0", int expiryDays = 30) =>
        svc.PublishAsync(SampleDoc(), "acme", "prod", version, "alice", null,
            new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero).AddDays(expiryDays));

    // ── Cohort membership (pure) ────────────────────────────────────────────────

    [Fact]
    public void GroupCohort_MatchesLabelCaseInsensitively_AndExcludesOthers()
    {
        var cohort = CanaryCohort.ForGroup("Ring0");

        Assert.True(cohort.Includes("m1", "ring0"));   // case-insensitive
        Assert.True(cohort.Includes("m2", "RING0"));
        Assert.False(cohort.Includes("m3", "ring1"));  // different group
        Assert.False(cohort.Includes("m4", null));     // unlabelled machine is never in a group cohort
    }

    [Fact]
    public void PercentageCohort_IsDeterministic_Stable_AndMonotonicAsItRampsUp()
    {
        var ids = Enumerable.Range(0, 400).Select(i => Guid.NewGuid().ToString("N")).ToArray();

        // Deterministic + stable: the same machine resolves identically on repeated evaluation.
        foreach (var id in ids.Take(20))
            Assert.Equal(
                CanaryCohort.ForPercentage(37).Includes(id, null),
                CanaryCohort.ForPercentage(37).Includes(id, null));

        // Monotonic ramp: a machine in the cohort at N% stays in at any M% ≥ N%.
        int[] steps = [1, 10, 25, 50, 75, 100];
        foreach (var id in ids)
        {
            var included = false;
            foreach (var pct in steps)
            {
                var nowIn = CanaryCohort.ForPercentage(pct).Includes(id, null);
                if (included)
                    Assert.True(nowIn, $"Machine {id} left the cohort as the percentage grew — not monotonic.");
                included = nowIn;
            }
        }

        // 100% includes everyone; a low percentage splits the fleet (spread sanity check).
        Assert.All(ids, id => Assert.True(CanaryCohort.ForPercentage(100).Includes(id, null)));
        var atHalf = ids.Count(id => CanaryCohort.ForPercentage(50).Includes(id, null));
        Assert.InRange(atHalf, 1, ids.Length - 1);
    }

    [Fact]
    public void Cohort_Validate_RequiresExactlyOneSelector_AndInRangePercentage()
    {
        Assert.Throws<PolicyAuthorityException>(() => new CanaryCohort().Validate());                       // neither
        Assert.Throws<PolicyAuthorityException>(() => new CanaryCohort { Group = "g", Percentage = 5 }.Validate()); // both
        Assert.Throws<PolicyAuthorityException>(() => new CanaryCohort { Percentage = 0 }.Validate());      // out of range
        Assert.Throws<PolicyAuthorityException>(() => new CanaryCohort { Percentage = 101 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => CanaryCohort.ForPercentage(0));

        new CanaryCohort { Group = "g" }.Validate();          // valid — does not throw
        new CanaryCohort { Percentage = 100 }.Validate();
    }

    // ── Publish canary ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PublishCanary_LeavesFleetActive_AndIssuesLater()
    {
        var (svc, _, _) = NewAuthority();
        await PublishActive(svc, "1.0.0");

        var canary = await svc.PublishCanaryAsync(SampleDoc(), "acme", "prod", "1.1.0-canary",
            "alice", "bob", DateTimeOffset.UtcNow.AddDays(30), CanaryCohort.ForPercentage(10));

        Assert.Equal(PolicyRolloutState.Canary, canary.RolloutState);
        Assert.Equal(10, canary.Canary!.Percentage);

        // The fleet is untouched: the active version machines retrieve is still 1.0.0.
        var active = await svc.GetActiveVersionAsync("acme", "prod");
        Assert.Equal("1.0.0", active!.PolicyVersion);
        Assert.Equal(PolicyRolloutState.Active, active.RolloutState);

        // The canary is discoverable separately and issued strictly later than the active baseline.
        var discovered = await svc.GetCanaryVersionAsync("acme", "prod");
        Assert.Equal("1.1.0-canary", discovered!.PolicyVersion);
        Assert.True(canary.IssuedAtUtc > active.IssuedAtUtc);
    }

    [Fact]
    public async Task PublishCanary_RequiresActiveBaseline_AndRefusesASecondCanary()
    {
        var (svc, _, _) = NewAuthority();

        // No active baseline yet → refused.
        await Assert.ThrowsAsync<PolicyAuthorityException>(() => svc.PublishCanaryAsync(
            SampleDoc(), "acme", "prod", "1.1.0-canary", "alice", null,
            DateTimeOffset.UtcNow.AddDays(30), CanaryCohort.ForPercentage(10)));

        await PublishActive(svc, "1.0.0");
        await svc.PublishCanaryAsync(SampleDoc(), "acme", "prod", "1.1.0-canary", "alice", null,
            DateTimeOffset.UtcNow.AddDays(30), CanaryCohort.ForPercentage(10));

        // A second concurrent canary is refused until the first is promoted or halted.
        var second = await Assert.ThrowsAsync<PolicyAuthorityException>(() => svc.PublishCanaryAsync(
            SampleDoc(), "acme", "prod", "1.2.0-canary", "alice", null,
            DateTimeOffset.UtcNow.AddDays(30), CanaryCohort.ForPercentage(20)));
        Assert.Contains("already in progress", second.Message);
    }

    [Fact]
    public async Task PublishCanary_RejectsInvalidCohort_DuplicateVersion_AndPastExpiry()
    {
        var (svc, _, clock) = NewAuthority();
        await PublishActive(svc, "1.0.0");

        // Invalid cohort (both selectors).
        await Assert.ThrowsAsync<PolicyAuthorityException>(() => svc.PublishCanaryAsync(
            SampleDoc(), "acme", "prod", "c1", "alice", null, clock.Now.AddDays(30),
            new CanaryCohort { Group = "g", Percentage = 5 }));

        // Duplicate version.
        await Assert.ThrowsAsync<PolicyAuthorityException>(() => svc.PublishCanaryAsync(
            SampleDoc(), "acme", "prod", "1.0.0", "alice", null, clock.Now.AddDays(30),
            CanaryCohort.ForPercentage(5)));

        // Past expiry.
        await Assert.ThrowsAsync<PolicyAuthorityException>(() => svc.PublishCanaryAsync(
            SampleDoc(), "acme", "prod", "c2", "alice", null, clock.Now.AddDays(-1),
            CanaryCohort.ForPercentage(5)));
    }

    [Fact]
    public async Task PublishedCanaryEnvelope_VerifiesAndParsesThroughTheClient()
    {
        var (svc, signer, clock) = NewAuthority();
        await PublishActive(svc, "1.0.0");
        await svc.PublishCanaryAsync(SampleDoc(), "acme", "prod", "1.1.0-canary", "alice", null,
            clock.Now.AddDays(30), CanaryCohort.ForGroup("ring0"));

        var canary = await svc.GetCanaryVersionAsync("acme", "prod");
        var envelope = System.Text.Json.JsonSerializer.Deserialize<SignedOrganizationPolicyEnvelope>(
            canary!.SignedEnvelopeJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.NotNull(EnterprisePolicySignature.VerifyAndParse(
            envelope!, Enrollment("acme", signer.PublicKeyPem), clock.Now));
    }

    // ── Promote ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PromoteCanary_BecomesFleetActive_AndSupersedesPriorActive()
    {
        var (svc, _, _) = NewAuthority();
        await PublishActive(svc, "1.0.0");
        await svc.PublishCanaryAsync(SampleDoc(), "acme", "prod", "1.1.0-canary", "alice", null,
            DateTimeOffset.UtcNow.AddDays(30), CanaryCohort.ForPercentage(10));

        var promoted = await svc.PromoteCanaryAsync("acme", "prod", "1.1.0-canary");
        Assert.Equal(PolicyRolloutState.Active, promoted.RolloutState);

        Assert.Equal("1.1.0-canary", (await svc.GetActiveVersionAsync("acme", "prod"))!.PolicyVersion);
        Assert.Null(await svc.GetCanaryVersionAsync("acme", "prod")); // no canary in progress anymore

        var history = await svc.ListVersionsAsync("acme", "prod");
        Assert.Equal(PolicyRolloutState.Superseded,
            history.Single(v => v.PolicyVersion == "1.0.0").RolloutState);
    }

    [Fact]
    public async Task PromoteCanary_RejectsNonCanaryVersion()
    {
        var (svc, _, _) = NewAuthority();
        await PublishActive(svc, "1.0.0");

        var ex = await Assert.ThrowsAsync<PolicyAuthorityException>(
            () => svc.PromoteCanaryAsync("acme", "prod", "1.0.0"));
        Assert.Contains("only a canary version can be promoted", ex.Message);
    }

    // ── Halt ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HaltCanary_ReissuesActiveLaterThanCanary_SoTheCohortReverts()
    {
        var (svc, signer, clock) = NewAuthority();
        // Distinct documents so we can prove the reverted document is the fleet's, not the canary's.
        var fleetRoot = Path.Combine(Path.GetTempPath(), "fleet");
        var canaryRoot = Path.Combine(Path.GetTempPath(), "canary");
        await svc.PublishAsync(SampleDoc(fleetRoot), "acme", "prod", "1.0.0", "alice", null, clock.Now.AddDays(30));
        var canary = await svc.PublishCanaryAsync(SampleDoc(canaryRoot), "acme", "prod", "1.1.0-canary",
            "alice", null, clock.Now.AddDays(30), CanaryCohort.ForPercentage(50));

        var reissued = await svc.HaltCanaryAsync("acme", "prod", "1.1.0-canary", "carol", "dave");

        // The canary is halted and gone; a fleet-wide active remains.
        Assert.Null(await svc.GetCanaryVersionAsync("acme", "prod"));
        var history = await svc.ListVersionsAsync("acme", "prod");
        Assert.Equal(PolicyRolloutState.RolledBack,
            history.Single(v => v.PolicyVersion == "1.1.0-canary").RolloutState);
        Assert.Equal(PolicyRolloutState.Superseded,
            history.Single(v => v.PolicyVersion == "1.0.0").RolloutState);

        // The re-issued active carries the FLEET document and issues later than the canary, so a cohort
        // machine holding the canary envelope accepts the revert (client rejects only older issuance).
        Assert.Equal(PolicyRolloutState.Active, reissued.RolloutState);
        Assert.True(reissued.IssuedAtUtc > canary.IssuedAtUtc);
        var active = await svc.RetrieveActiveEnvelopeAsync("acme", "prod");
        var doc = EnterprisePolicySignature.VerifyAndParse(active!, Enrollment("acme", signer.PublicKeyPem), clock.Now);
        Assert.Contains(fleetRoot, doc.Filesystem.ApprovedRoots);
        Assert.DoesNotContain(canaryRoot, doc.Filesystem.ApprovedRoots);
    }

    [Fact]
    public async Task HaltCanary_RejectsNonCanary_AndRefusesWhenActiveHasExpired()
    {
        var (svc, _, clock) = NewAuthority();
        await svc.PublishAsync(SampleDoc(), "acme", "prod", "1.0.0", "alice", null, clock.Now.AddDays(1));
        await svc.PublishCanaryAsync(SampleDoc(), "acme", "prod", "1.1.0-canary", "alice", null,
            clock.Now.AddDays(1), CanaryCohort.ForPercentage(50));

        // Halting a non-canary version is refused.
        await Assert.ThrowsAsync<PolicyAuthorityException>(
            () => svc.HaltCanaryAsync("acme", "prod", "1.0.0", "carol", null));

        // Once the active baseline has expired, re-issuing it on halt would sign an expired policy:
        // refuse and demand a fresh active first.
        clock.Now = clock.Now.AddDays(2);
        var ex = await Assert.ThrowsAsync<PolicyAuthorityException>(
            () => svc.HaltCanaryAsync("acme", "prod", "1.1.0-canary", "carol", null));
        Assert.Contains("expired", ex.Message);
    }

    [Fact]
    public async Task Canary_IsIsolatedPerTenantEnvironment()
    {
        var (svc, _, _) = NewAuthority();
        await PublishActive(svc, "1.0.0");
        await svc.PublishCanaryAsync(SampleDoc(), "acme", "prod", "1.1.0-canary", "alice", null,
            DateTimeOffset.UtcNow.AddDays(30), CanaryCohort.ForPercentage(10));

        // A different environment has no canary and no active baseline is shared.
        Assert.Null(await svc.GetCanaryVersionAsync("acme", "dev"));
        Assert.Null(await svc.GetActiveVersionAsync("acme", "dev"));
    }
}
