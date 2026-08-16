using System.Net;
using System.Security.Claims;
using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Services;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// The assertion exchange: a Portal session in, a short-lived Orchestrator assertion out.
///
/// <para>The property worth holding onto is that a caller <em>presents</em> an identity and never
/// <em>requests</em> one. Everything in the token — the subject, its roles, its groups, its tenant,
/// its scopes — is read from the server's own view of the caller, which is why the endpoint takes no
/// parameters at all.</para>
///
/// <para>Most of this exercises <see cref="OrchestratorAssertionIssuer"/> directly rather than over
/// HTTP. The endpoint is a thin wrapper over it, and the proxy uses the same issuer, so testing it
/// here covers both callers. Driving it through the host would additionally require the signing
/// secret to survive the test harness's configuration layering — <c>Program.cs</c> binds
/// <c>PortalConfig</c> before the factory's in-memory source is applied — which is a property of the
/// harness rather than of this feature.</para>
/// </summary>
[Trait("Category", "Portal")]
[Trait("Category", "Smoke.Security")]
public sealed class OrchestratorAssertionExchangeTests
{
    private const string SigningSecret = "portal-test-orchestrator-identity-signing-secret";

    private static PortalConfig FederatingConfig()
    {
        var config = new PortalConfig();
        config.Orchestrator.IdentitySigningSecret = SigningSecret;
        return config;
    }

    private static ClaimsPrincipal User(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "Test"));

    [Fact]
    public async Task AnAnonymousCallerGetsNoAssertionOverHttp()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/auth/orchestrator-assertion", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnAuthenticatedCallerGetsATokenTheOrchestratorAlreadyKnowsHowToValidate()
    {
        var issuer = new OrchestratorAssertionIssuer(FederatingConfig());

        var issued = await issuer.IssueForAsync(User(
            new Claim(ClaimTypes.NameIdentifier, "42"),
            new Claim(ClaimTypes.Name, "alice"),
            new Claim(ClaimTypes.Role, "Viewer")));

        Assert.NotNull(issued);

        // The Orchestrator gains no trust code for this — it already validates exactly this token —
        // so the assertion has to satisfy the existing validator unchanged.
        Assert.True(OrchestratorIdentityAssertion.TryValidate(
            issued!.Assertion, SigningSecret, out var caller, out var error), error);
        Assert.Equal("user", caller!.SubjectType);
        Assert.Equal("42", caller.SubjectId);
        Assert.Contains("Viewer", caller.Roles);
        Assert.Equal(OrchestratorIdentityAssertion.Audience, issued.Audience);

        // The expiry is returned so a client can renew ahead of it rather than discovering it through
        // a failed call, and it is short by design.
        Assert.True(issued.ExpiresAt > DateTimeOffset.UtcNow);
        Assert.True(issued.ExpiresAt <= DateTimeOffset.UtcNow.Add(OrchestratorIdentityAssertion.DefaultLifetime));
    }

    [Fact]
    public async Task ThePrincipalsOwnSubjectIsUsedWhicheverWayTheClaimIsSpelled()
    {
        // The Portal token carries `sub`; whether it arrives mapped to NameIdentifier depends on the
        // handler's inbound claim mapping. Reading only one spelling leaves the caller unidentifiable
        // and silently unable to obtain an assertion at all.
        var issuer = new OrchestratorAssertionIssuer(FederatingConfig());

        var issued = await issuer.IssueForAsync(User(new Claim("sub", "42")));

        Assert.NotNull(issued);
        Assert.True(OrchestratorIdentityAssertion.TryValidate(
            issued!.Assertion, SigningSecret, out var caller, out _));
        Assert.Equal("42", caller!.SubjectId);
    }

    [Fact]
    public async Task AnInteractiveUserCarriesNoScopesHoweverManyTheirTokenHolds()
    {
        var issuer = new OrchestratorAssertionIssuer(FederatingConfig());

        // Scopes cap a *service* caller. A person's authority is their roles and grants, and letting
        // them mint themselves orchestrator.admin here would make the ceiling decorative.
        var issued = await issuer.IssueForAsync(User(
            new Claim(ClaimTypes.NameIdentifier, "42"),
            new Claim(TokenService.ScopeClaim, "orchestrator.admin")));

        Assert.True(OrchestratorIdentityAssertion.TryValidate(
            issued!.Assertion, SigningSecret, out var caller, out _));
        Assert.Empty(caller!.EffectiveScopes);
        Assert.Empty(issued.Scopes);
    }

    [Fact]
    public async Task AServiceCallerCarriesExactlyTheScopesItsTokenHolds()
    {
        var issuer = new OrchestratorAssertionIssuer(FederatingConfig());

        var issued = await issuer.IssueForAsync(User(
            new Claim(TokenService.IdentityTypeClaim, TokenService.ServiceIdentityType),
            new Claim(TokenService.ServiceAccountIdClaim, "sa_runner"),
            new Claim(TokenService.ScopeClaim, "orchestrator.read"),
            new Claim(TokenService.ScopeClaim, "orchestrator.execute")));

        Assert.True(OrchestratorIdentityAssertion.TryValidate(
            issued!.Assertion, SigningSecret, out var caller, out _));
        Assert.Equal("service", caller!.SubjectType);
        Assert.Equal("sa_runner", caller.SubjectId);
        Assert.Equal(
            ["orchestrator.execute", "orchestrator.read"],
            caller.EffectiveScopes.OrderBy(scope => scope, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task APortalThatDoesNotFederateIdentityIssuesNothing()
    {
        // No signing secret: this deployment has no Orchestrator to federate to. The endpoint reports
        // this the same way it reports an unresolvable principal — a caller learning which one it is
        // learns something about a deployment it is not authenticated to know.
        var issuer = new OrchestratorAssertionIssuer(new PortalConfig());

        Assert.Null(await issuer.IssueForAsync(User(new Claim(ClaimTypes.NameIdentifier, "42"))));
    }

    [Fact]
    public async Task AnUnauthenticatedOrUnidentifiablePrincipalIssuesNothing()
    {
        var issuer = new OrchestratorAssertionIssuer(FederatingConfig());

        Assert.Null(await issuer.IssueForAsync(null));
        Assert.Null(await issuer.IssueForAsync(new ClaimsPrincipal(new ClaimsIdentity())));
        // Authenticated, but carries no subject to assert.
        Assert.Null(await issuer.IssueForAsync(User(new Claim(ClaimTypes.Name, "nameless"))));
    }

    [Fact]
    public void ThePortalsOwnBackgroundWorkCarriesTheWholeLadder()
    {
        var issuer = new OrchestratorAssertionIssuer(FederatingConfig());

        // The control plane acting as itself, not an automation someone scoped. A service caller with
        // no scopes can do nothing, so leaving them off would silently stop every scheduled delivery.
        var assertion = issuer.IssueForBackground();

        Assert.NotNull(assertion);
        Assert.True(OrchestratorIdentityAssertion.TryValidate(
            assertion!, SigningSecret, out var caller, out _));
        Assert.Equal("service", caller!.SubjectType);
        foreach (var rung in ServiceAccountScopes.OrchestratorLadder)
            Assert.Contains(rung, caller.EffectiveScopes);
    }
}
