using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ETL_SQL.Portal.Services;

/// <summary>The authorization request a federated login must send the browser to, plus the
/// per-flow secrets the callback needs to verify it (carried in a protected cookie, never the URL).</summary>
public sealed record OidcAuthorizationRequest(string AuthorizationUrl, string State, string Nonce, string CodeVerifier);

/// <summary>The validated identity extracted from a provider's id_token.</summary>
public sealed record OidcIdentity(string Subject, string Username, string? Email, IReadOnlyList<string> Groups);

/// <summary>Raised when a federated authentication step fails (bad code, invalid/forged token,
/// nonce mismatch). The message is operator/log-facing; callers surface a generic failure to users.</summary>
public sealed class OidcAuthenticationException(string message) : Exception(message);

public interface IOidcAuthenticationService
{
    bool Enabled { get; }

    /// <summary>Builds the authorization-code request (with PKCE, state, and nonce) for the given
    /// absolute redirect URI. Resolves discovery up front so a misconfigured authority fails at
    /// login rather than after the browser round-trip.</summary>
    Task<OidcAuthorizationRequest> BuildAuthorizationRequestAsync(string redirectUri, CancellationToken ct = default);

    /// <summary>Exchanges the authorization code for tokens and validates the id_token
    /// (issuer/audience/lifetime/signature via JWKS, plus the expected nonce), returning the
    /// validated identity. Throws <see cref="OidcAuthenticationException"/> on any failure.</summary>
    Task<OidcIdentity> CompleteAsync(
        string code, string codeVerifier, string redirectUri, string expectedNonce, CancellationToken ct = default);
}

/// <summary>Provider discovery metadata (endpoints + JWKS signing keys), abstracted so tests can
/// supply a fake configuration without a live identity provider.</summary>
public interface IOidcDiscoveryProvider
{
    Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken ct = default);
}

/// <summary>Production discovery via the cached, auto-refreshing
/// <see cref="ConfigurationManager{T}"/> over the authority's well-known endpoint (HTTPS-only).</summary>
public sealed class OidcDiscoveryProvider : IOidcDiscoveryProvider
{
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? _manager;

    public OidcDiscoveryProvider(PortalConfig config, HttpClient http)
    {
        var oidc = config.Identity.Oidc;
        if (oidc.Enabled && !string.IsNullOrWhiteSpace(oidc.Authority))
        {
            var metadataAddress = oidc.Authority!.TrimEnd('/') + "/.well-known/openid-configuration";
            _manager = new ConfigurationManager<OpenIdConnectConfiguration>(
                metadataAddress,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever(http) { RequireHttps = true });
        }
    }

    public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken ct = default) =>
        _manager?.GetConfigurationAsync(ct)
        ?? throw new OidcAuthenticationException("OIDC discovery is not configured.");
}

public sealed class OidcAuthenticationService(
    PortalConfig config,
    HttpClient http,
    IOidcDiscoveryProvider discovery) : IOidcAuthenticationService
{
    private readonly OidcIdentityConfig _cfg = config.Identity.Oidc;

    public bool Enabled => _cfg.Enabled;

    public async Task<OidcAuthorizationRequest> BuildAuthorizationRequestAsync(string redirectUri, CancellationToken ct = default)
    {
        if (!Enabled) throw new OidcAuthenticationException("OIDC is not enabled.");

        var state = RandomToken();
        var nonce = RandomToken();
        var codeVerifier = RandomToken();
        var codeChallenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

        var configuration = await discovery.GetConfigurationAsync(ct);
        if (string.IsNullOrEmpty(configuration.AuthorizationEndpoint))
            throw new OidcAuthenticationException("Identity provider did not advertise an authorization endpoint.");

        var scopes = _cfg.Scopes is { Length: > 0 } s ? s : ["openid"];
        if (!scopes.Any(x => string.Equals(x, "openid", StringComparison.OrdinalIgnoreCase)))
            scopes = ["openid", .. scopes];

        var query = new Dictionary<string, string?>
        {
            ["client_id"] = _cfg.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["scope"] = string.Join(' ', scopes),
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256"
        };

        var url = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(
            configuration.AuthorizationEndpoint, query);
        return new OidcAuthorizationRequest(url, state, nonce, codeVerifier);
    }

    public async Task<OidcIdentity> CompleteAsync(
        string code, string codeVerifier, string redirectUri, string expectedNonce, CancellationToken ct = default)
    {
        if (!Enabled) throw new OidcAuthenticationException("OIDC is not enabled.");
        if (string.IsNullOrEmpty(code)) throw new OidcAuthenticationException("Missing authorization code.");

        var configuration = await discovery.GetConfigurationAsync(ct);
        var idToken = await ExchangeCodeAsync(configuration, code, codeVerifier, redirectUri, ct);
        return await ValidateIdTokenAsync(configuration, idToken, expectedNonce);
    }

    private async Task<string> ExchangeCodeAsync(
        OpenIdConnectConfiguration configuration, string code, string codeVerifier, string redirectUri, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(configuration.TokenEndpoint))
            throw new OidcAuthenticationException("Identity provider did not advertise a token endpoint.");

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = _cfg.ClientId ?? string.Empty,
            ["client_secret"] = _cfg.ClientSecret ?? string.Empty,
            ["code_verifier"] = codeVerifier
        });

        using var response = await http.PostAsync(configuration.TokenEndpoint, content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new OidcAuthenticationException(
                $"Token exchange failed with status {(int)response.StatusCode}.");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("id_token", out var idTokenElement)
            || idTokenElement.ValueKind != JsonValueKind.String)
            throw new OidcAuthenticationException("Token response did not contain an id_token.");

        return idTokenElement.GetString()!;
    }

    private async Task<OidcIdentity> ValidateIdTokenAsync(
        OpenIdConnectConfiguration configuration, string idToken, string expectedNonce)
    {
        var audiences = new List<string>();
        if (!string.IsNullOrWhiteSpace(_cfg.ClientId)) audiences.Add(_cfg.ClientId!);
        audiences.AddRange(_cfg.AdditionalAudiences);

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = configuration.Issuer,
            ValidateAudience = true,
            ValidAudiences = audiences,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = configuration.SigningKeys,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(Math.Max(0, _cfg.ClockSkewSeconds))
        };

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(idToken, parameters);
        if (!result.IsValid)
            throw new OidcAuthenticationException("id_token validation failed: " + (result.Exception?.Message ?? "invalid token."));

        var identity = result.ClaimsIdentity;
        var nonce = identity.FindFirst("nonce")?.Value;
        if (string.IsNullOrEmpty(nonce) || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(nonce), Encoding.UTF8.GetBytes(expectedNonce)))
            throw new OidcAuthenticationException("id_token nonce did not match the login request.");

        var subject = identity.FindFirst("sub")?.Value
            ?? throw new OidcAuthenticationException("id_token did not contain a subject (sub) claim.");

        // Required-claims policy: fail closed if the provider omitted a claim the deployment mandates.
        foreach (var required in _cfg.RequiredClaims ?? [])
            if (!string.IsNullOrEmpty(required) && identity.FindFirst(required) is null)
                throw new OidcAuthenticationException($"id_token is missing required claim '{required}'.");

        var username = FirstClaim(identity, _cfg.UsernameClaimType)
            ?? FirstClaim(identity, "preferred_username")
            ?? subject;
        var email = FirstClaim(identity, _cfg.EmailClaimType) ?? FirstClaim(identity, "email");

        var groups = new List<string>();
        foreach (var claimType in _cfg.GroupClaimTypes ?? [])
            groups.AddRange(identity.FindAll(claimType).Select(c => c.Value));

        return new OidcIdentity(subject, username, email, groups.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static string? FirstClaim(System.Security.Claims.ClaimsIdentity identity, string type) =>
        string.IsNullOrEmpty(type) ? null : identity.FindFirst(type)?.Value;

    private static string RandomToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
}
