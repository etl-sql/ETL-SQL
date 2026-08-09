using ETL_SQL.Core.Governance;
using ETL_SQL.Orchestrator.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Portal-to-Orchestrator identity is a short-lived signed assertion. A caller-controlled actor
/// header is deliberately ignored: attribution and authorization must use the same verified
/// principal or the audit trail can be forged independently of the access decision.
/// </summary>
public sealed class OrchestratorIdentityAssertionTests
{
    private const string Secret = "test-only-orchestrator-identity-secret-32-bytes";

    private static IConfiguration Config(
        string? key = "real-key",
        string? secret = Secret,
        bool requireIdentity = true) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orchestrator:ApiKey"] = key,
                ["Orchestrator:IdentitySigningSecret"] = secret,
                ["Orchestrator:RequireFederatedIdentity"] = requireIdentity.ToString()
            })
            .Build();

    [Fact]
    public void SignedAssertionRoundTripsIdentityRolesAndGroups()
    {
        var expected = new OrchestratorCaller(
            "user", "42", "jsmith", ["OrchestratorManager"], ["7", "11"]);
        var assertion = OrchestratorIdentityAssertion.Create(expected, Secret);

        Assert.True(OrchestratorIdentityAssertion.TryValidate(
            assertion, Secret, out var actual, out var error), error);
        Assert.Equal(expected.SubjectType, actual!.SubjectType);
        Assert.Equal(expected.SubjectId, actual.SubjectId);
        Assert.Equal(expected.Roles, actual.Roles);
        Assert.Equal(expected.GroupIds, actual.GroupIds);
    }

    [Fact]
    public void VerifiedAssertionBecomesTheOnlyAuditActor()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[OrchestratorIdentityAssertion.HeaderName] =
            OrchestratorIdentityAssertion.Create(
                new OrchestratorCaller("user", "42", "jsmith", ["Admin"], []), Secret);
        ctx.Request.Headers["X-Orchestrator-Actor"] = "999:forged-admin";

        Assert.True(JobApiEndpoints.FederatedIdentityAccepted(ctx, Config()));
        Assert.Equal("user:42:jsmith", JobApiEndpoints.RequestActor(ctx));
    }

    [Fact]
    public void UnsignedActorHeaderHasNoIdentityOrAttributionAuthority()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Orchestrator-Actor"] = "42:admin";

        Assert.False(JobApiEndpoints.FederatedIdentityAccepted(ctx, Config()));
        Assert.Equal("service:unverified:Unverified caller", JobApiEndpoints.RequestActor(ctx));
    }

    [Fact]
    public void TamperingWithSubjectInvalidatesTheAssertion()
    {
        var assertion = OrchestratorIdentityAssertion.Create(
            new OrchestratorCaller("user", "42", "jsmith", [], []), Secret);
        var parts = assertion.Split('.');
        var tampered = "e30." + parts[1];

        Assert.False(OrchestratorIdentityAssertion.TryValidate(
            tampered, Secret, out _, out var error));
        Assert.Contains("signature", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpiredAssertionIsRejected()
    {
        var issuedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var assertion = OrchestratorIdentityAssertion.Create(
            new OrchestratorCaller("user", "42", "jsmith", [], []), Secret, issuedAt);

        Assert.False(OrchestratorIdentityAssertion.TryValidate(
            assertion, Secret, out _, out var error, DateTimeOffset.UtcNow));
        Assert.Contains("expired", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExplicitLegacyModeGetsANamedServicePrincipalButCannotForgeAHuman()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Orchestrator-Actor"] = "42:admin";

        Assert.True(JobApiEndpoints.FederatedIdentityAccepted(
            ctx, Config(secret: null, requireIdentity: false)));
        Assert.Equal("service:legacy-api-key:Legacy API-key client", JobApiEndpoints.RequestActor(ctx));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-key")]
    public void IdentityAssertionNeverSubstitutesForTheApiKey(string? providedKey)
    {
        Assert.False(JobApiEndpoints.ApiKeyAccepted(Config(), providedKey));
    }

    [Fact]
    public void ApiKeyAndIdentityAreIndependentFactors()
    {
        Assert.True(JobApiEndpoints.ApiKeyAccepted(Config(), "real-key"));
        Assert.False(JobApiEndpoints.FederatedIdentityAccepted(
            new DefaultHttpContext(), Config()));
    }
}
