using System.Net;
using System.Text;
using ETL_SQL.Portal;
using ETL_SQL.Portal.Services;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// P1.2 certification for the OIDC token crypto: authorization-request shape, and the id_token
/// validation path (signature via JWKS, issuer/audience/lifetime, and nonce binding). Uses a fake
/// discovery provider with an in-memory signing key and a stub token endpoint so no live IdP is needed.
/// </summary>
[Trait("Category", "Portal")]
public sealed class OidcAuthenticationServiceTests
{
    private const string Issuer = "https://idp.test";
    private const string ClientId = "etl-portal";
    private const string TokenEndpoint = "https://idp.test/token";

    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("oidc-test-signing-key-at-least-32-bytes!!"));

    [Fact]
    public async Task BuildAuthorizationRequest_IncludesPkceStateNonceAndScopes()
    {
        var service = NewService(new StubHandler());

        var request = await service.BuildAuthorizationRequestAsync("https://portal.test/api/auth/oidc/callback");

        Assert.StartsWith(Issuer + "/authorize", request.AuthorizationUrl);
        Assert.Contains("response_type=code", request.AuthorizationUrl);
        Assert.Contains("code_challenge_method=S256", request.AuthorizationUrl);
        Assert.Contains("scope=openid", request.AuthorizationUrl);
        Assert.Contains("client_id=" + ClientId, request.AuthorizationUrl);
        Assert.False(string.IsNullOrEmpty(request.State));
        Assert.False(string.IsNullOrEmpty(request.Nonce));
        Assert.False(string.IsNullOrEmpty(request.CodeVerifier));
    }

    [Fact]
    public async Task Complete_ValidIdToken_ReturnsIdentityWithClaims()
    {
        const string nonce = "expected-nonce";
        var idToken = CreateIdToken(nonce: nonce, subject: "user-1",
            username: "alice", email: "alice@example.com", groups: ["analysts", "publishers"]);
        var service = NewService(new StubHandler(idToken));

        var identity = await service.CompleteAsync("code", "verifier", "https://portal.test/cb", nonce);

        Assert.Equal("user-1", identity.Subject);
        Assert.Equal("alice", identity.Username);
        Assert.Equal("alice@example.com", identity.Email);
        Assert.Equal(["analysts", "publishers"], identity.Groups.OrderBy(g => g));
    }

    [Fact]
    public async Task Complete_NonceMismatch_Throws()
    {
        var idToken = CreateIdToken(nonce: "actual-nonce", subject: "u", username: "u", email: null, groups: []);
        var service = NewService(new StubHandler(idToken));

        await Assert.ThrowsAsync<OidcAuthenticationException>(
            () => service.CompleteAsync("code", "verifier", "https://portal.test/cb", "different-nonce"));
    }

    [Fact]
    public async Task Complete_WrongAudience_Throws()
    {
        var idToken = CreateIdToken(nonce: "n", subject: "u", username: "u", email: null, groups: [], audience: "some-other-client");
        var service = NewService(new StubHandler(idToken));

        await Assert.ThrowsAsync<OidcAuthenticationException>(
            () => service.CompleteAsync("code", "verifier", "https://portal.test/cb", "n"));
    }

    [Fact]
    public async Task Complete_ExpiredIdToken_Throws()
    {
        var idToken = CreateIdToken(nonce: "n", subject: "u", username: "u", email: null, groups: [],
            notBefore: DateTime.UtcNow.AddHours(-2), expires: DateTime.UtcNow.AddHours(-1));
        var service = NewService(new StubHandler(idToken));

        await Assert.ThrowsAsync<OidcAuthenticationException>(
            () => service.CompleteAsync("code", "verifier", "https://portal.test/cb", "n"));
    }

    [Fact]
    public async Task Complete_MissingRequiredClaim_Throws()
    {
        // Token is otherwise valid but omits the mandated 'email' claim.
        var idToken = CreateIdToken(nonce: "n", subject: "u", username: "u", email: null, groups: []);
        var service = NewService(new StubHandler(idToken), requiredClaims: ["email"]);

        await Assert.ThrowsAsync<OidcAuthenticationException>(
            () => service.CompleteAsync("code", "verifier", "https://portal.test/cb", "n"));
    }

    [Fact]
    public async Task Complete_RequiredClaimPresent_Succeeds()
    {
        var idToken = CreateIdToken(nonce: "n", subject: "u", username: "u", email: "u@example.com", groups: []);
        var service = NewService(new StubHandler(idToken), requiredClaims: ["email"]);

        var identity = await service.CompleteAsync("code", "verifier", "https://portal.test/cb", "n");
        Assert.Equal("u@example.com", identity.Email);
    }

    [Fact]
    public async Task Complete_TokenEndpointFailure_Throws()
    {
        var service = NewService(new StubHandler(statusCode: HttpStatusCode.BadRequest));

        await Assert.ThrowsAsync<OidcAuthenticationException>(
            () => service.CompleteAsync("code", "verifier", "https://portal.test/cb", "n"));
    }

    [Fact]
    public async Task Complete_TokenSignedWithRotatedKey_Succeeds()
    {
        // JWKS rotation: the IdP now signs with a new key; discovery advertises it, so it validates.
        var newKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("rotated-oidc-key-also-at-least-32-bytes!!"));
        var idToken = CreateIdToken(nonce: "n", subject: "u", username: "u", email: null, groups: [], signingKey: newKey);
        var service = NewService(new StubHandler(idToken), discovery: new KeyedDiscovery(newKey));

        var identity = await service.CompleteAsync("code", "verifier", "https://portal.test/cb", "n");
        Assert.Equal("u", identity.Subject);
    }

    [Fact]
    public async Task Complete_TokenSignedWithRetiredKey_Throws()
    {
        // A token signed with a key no longer in the published JWKS (rotated out) must be rejected.
        var retiredKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("retired-oidc-key-also-at-least-32-bytes!"));
        var idToken = CreateIdToken(nonce: "n", subject: "u", username: "u", email: null, groups: [], signingKey: retiredKey);
        var service = NewService(new StubHandler(idToken), discovery: new FakeDiscovery()); // JWKS has only the current key

        await Assert.ThrowsAsync<OidcAuthenticationException>(
            () => service.CompleteAsync("code", "verifier", "https://portal.test/cb", "n"));
    }

    [Fact]
    public async Task Complete_WhenDiscoveryUnavailable_Throws()
    {
        var service = NewService(new StubHandler("ignored"), discovery: new ThrowingDiscovery());

        await Assert.ThrowsAsync<OidcAuthenticationException>(
            () => service.CompleteAsync("code", "verifier", "https://portal.test/cb", "n"));
    }

    private static OidcAuthenticationService NewService(
        StubHandler handler, string[]? requiredClaims = null, IOidcDiscoveryProvider? discovery = null)
    {
        var config = new PortalConfig
        {
            Identity = new IdentityConfig
            {
                Oidc = new OidcIdentityConfig
                {
                    Enabled = true,
                    Authority = Issuer,
                    ClientId = ClientId,
                    ClientSecret = "secret",
                    GroupClaimTypes = ["groups"],
                    UsernameClaimType = "preferred_username",
                    EmailClaimType = "email",
                    RequiredClaims = requiredClaims ?? []
                }
            }
        };
        return new OidcAuthenticationService(config, new HttpClient(handler), discovery ?? new FakeDiscovery());
    }

    private static string CreateIdToken(
        string nonce, string subject, string username, string? email, string[] groups,
        string? audience = null, DateTime? notBefore = null, DateTime? expires = null, SecurityKey? signingKey = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = subject,
            ["preferred_username"] = username,
            ["nonce"] = nonce
        };
        if (email is not null) claims["email"] = email;
        if (groups.Length > 0) claims["groups"] = groups;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience ?? ClientId,
            Claims = claims,
            NotBefore = notBefore ?? DateTime.UtcNow.AddMinutes(-1),
            Expires = expires ?? DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.HmacSha256)
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private sealed class FakeDiscovery : IOidcDiscoveryProvider
    {
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken ct = default) =>
            Task.FromResult(ConfigWith(SigningKey));
    }

    private sealed class KeyedDiscovery(SecurityKey key) : IOidcDiscoveryProvider
    {
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken ct = default) =>
            Task.FromResult(ConfigWith(key));
    }

    private sealed class ThrowingDiscovery : IOidcDiscoveryProvider
    {
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken ct = default) =>
            throw new OidcAuthenticationException("OIDC discovery is unavailable.");
    }

    private static OpenIdConnectConfiguration ConfigWith(SecurityKey key)
    {
        var config = new OpenIdConnectConfiguration
        {
            Issuer = Issuer,
            AuthorizationEndpoint = Issuer + "/authorize",
            TokenEndpoint = TokenEndpoint
        };
        config.SigningKeys.Add(key);
        return config;
    }

    private sealed class StubHandler(string? idToken = null, HttpStatusCode statusCode = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (statusCode != HttpStatusCode.OK)
                return Task.FromResult(new HttpResponseMessage(statusCode));

            var body = $"{{\"id_token\":\"{idToken}\",\"access_token\":\"a\",\"token_type\":\"Bearer\"}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}

/// <summary>P1.1 certification: OIDC startup validation accepts a complete config and rejects each
/// missing/invalid field (fail-secure when enabled).</summary>
[Trait("Category", "Portal")]
public sealed class OidcConfigValidationTests
{
    private static OidcIdentityConfig Valid() => new()
    {
        Enabled = true,
        Authority = "https://idp.example.test",
        ClientId = "etl-portal",
        ClientSecret = "secret",
        Scopes = ["openid", "profile"],
        CallbackPath = "/api/auth/oidc/callback",
        PostLoginRedirectPath = "/index.html"
    };

    [Fact]
    public void Validate_CompleteConfig_HasNoErrors() =>
        Assert.Empty(OidcConfigValidationService.Validate(Valid()));

    [Fact]
    public void Validate_MissingAuthority_Fails()
    {
        var cfg = Valid(); cfg.Authority = null;
        Assert.Contains(OidcConfigValidationService.Validate(cfg), e => e.Contains("Authority"));
    }

    [Fact]
    public void Validate_NonHttpsAuthority_Fails()
    {
        var cfg = Valid(); cfg.Authority = "http://idp.example.test";
        Assert.Contains(OidcConfigValidationService.Validate(cfg), e => e.Contains("HTTPS"));
    }

    [Fact]
    public void Validate_MissingClientId_Fails()
    {
        var cfg = Valid(); cfg.ClientId = "";
        Assert.Contains(OidcConfigValidationService.Validate(cfg), e => e.Contains("ClientId"));
    }

    [Fact]
    public void Validate_MissingClientSecret_Fails()
    {
        var cfg = Valid(); cfg.ClientSecret = null;
        Assert.Contains(OidcConfigValidationService.Validate(cfg), e => e.Contains("ClientSecret"));
    }

    [Fact]
    public void Validate_ScopesWithoutOpenId_Fails()
    {
        var cfg = Valid(); cfg.Scopes = ["profile", "email"];
        Assert.Contains(OidcConfigValidationService.Validate(cfg), e => e.Contains("openid"));
    }

    [Fact]
    public void Validate_RelativeCallbackPath_Fails()
    {
        var cfg = Valid(); cfg.CallbackPath = "api/auth/oidc/callback";
        Assert.Contains(OidcConfigValidationService.Validate(cfg), e => e.Contains("CallbackPath"));
    }
}
