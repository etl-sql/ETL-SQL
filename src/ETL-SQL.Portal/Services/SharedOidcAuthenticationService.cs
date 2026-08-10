using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ETL_SQL.Portal.Services;

public interface ISharedOidcAuthenticationService
{
    Task<OidcAuthorizationRequest> BuildAuthorizationRequestAsync(
        SharedIdentityAuthorityBinding authority,
        string redirectUri,
        CancellationToken ct = default);

    Task<OidcIdentity> CompleteAsync(
        SharedIdentityAuthorityBinding authority,
        string code,
        string codeVerifier,
        string redirectUri,
        string expectedNonce,
        CancellationToken ct = default);
}

/// <summary>
/// Executes an OIDC authorization-code flow against the server-routed Shared authority. Authority,
/// client id, issuer and optional tenant secret are taken only from the protected authority binding.
/// </summary>
public sealed class SharedOidcAuthenticationService(
    PortalConfig config,
    IHttpClientFactory clients,
    IServiceProvider services,
    SharedIdentityAuthorityResolver authorities) : ISharedOidcAuthenticationService
{
    public async Task<OidcAuthorizationRequest> BuildAuthorizationRequestAsync(
        SharedIdentityAuthorityBinding authority,
        string redirectUri,
        CancellationToken ct = default)
    {
        var metadata = await DiscoverAsync(authority, ct);
        if (string.IsNullOrEmpty(metadata.AuthorizationEndpoint))
            throw new OidcAuthenticationException("Identity provider did not advertise an authorization endpoint.");

        var state = RandomToken();
        var nonce = RandomToken();
        var verifier = RandomToken();
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var scopes = config.Identity.Oidc.Scopes is { Length: > 0 } configured ? configured : ["openid"];
        if (!scopes.Any(x => string.Equals(x, "openid", StringComparison.OrdinalIgnoreCase)))
            scopes = ["openid", .. scopes];

        var url = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(
            metadata.AuthorizationEndpoint,
            new Dictionary<string, string?>
            {
                ["client_id"] = authority.ClientId,
                ["response_type"] = "code",
                ["redirect_uri"] = redirectUri,
                ["scope"] = string.Join(' ', scopes),
                ["state"] = state,
                ["nonce"] = nonce,
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256"
            });
        return new OidcAuthorizationRequest(url, state, nonce, verifier);
    }

    public async Task<OidcIdentity> CompleteAsync(
        SharedIdentityAuthorityBinding authority,
        string code,
        string codeVerifier,
        string redirectUri,
        string expectedNonce,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(code))
            throw new OidcAuthenticationException("Missing authorization code.");
        var metadata = await DiscoverAsync(authority, ct);
        if (string.IsNullOrEmpty(metadata.TokenEndpoint))
            throw new OidcAuthenticationException("Identity provider did not advertise a token endpoint.");

        var secretReference = await authorities.ResolveClientSecretReferenceAsync(authority, ct);
        string? clientSecret = null;
        if (authority.ClientSecretConfigured)
        {
            if (string.IsNullOrWhiteSpace(secretReference)
                || !secretReference.StartsWith("SECRET:", StringComparison.OrdinalIgnoreCase))
                throw new OidcAuthenticationException("The tenant OIDC client credential is unavailable.");
            var secretStore = services.GetRequiredService<PortalSecretStoreService>();
            clientSecret = await secretStore.ResolveAsync(secretReference["SECRET:".Length..], ct);
        }

        var fields = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = authority.ClientId,
            ["code_verifier"] = codeVerifier
        };
        if (clientSecret is not null) fields["client_secret"] = clientSecret;
        using var content = new FormUrlEncodedContent(fields);
        using var response = await clients.CreateClient("oidc-discovery").PostAsync(metadata.TokenEndpoint, content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new OidcAuthenticationException($"Token exchange failed with status {(int)response.StatusCode}.");
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("id_token", out var element)
            || element.ValueKind != JsonValueKind.String)
            throw new OidcAuthenticationException("Token response did not contain an id_token.");

        return await ValidateAsync(authority, metadata, element.GetString()!, expectedNonce);
    }

    private async Task<OpenIdConnectConfiguration> DiscoverAsync(
        SharedIdentityAuthorityBinding authority,
        CancellationToken ct)
    {
        var manager = new ConfigurationManager<OpenIdConnectConfiguration>(
            authority.Issuer.TrimEnd('/') + "/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(clients.CreateClient("oidc-discovery")) { RequireHttps = true });
        try
        {
            var metadata = await manager.GetConfigurationAsync(ct);
            if (!string.Equals(
                    SharedIdentityAuthorityService.NormalizeIssuer(metadata.Issuer),
                    authority.Issuer,
                    StringComparison.Ordinal))
                throw new OidcAuthenticationException("OIDC discovery issuer did not match the routed authority.");
            return metadata;
        }
        catch (OidcAuthenticationException) { throw; }
        catch (Exception ex)
        {
            throw new OidcAuthenticationException("OIDC discovery failed: " + ex.Message);
        }
    }

    private async Task<OidcIdentity> ValidateAsync(
        SharedIdentityAuthorityBinding authority,
        OpenIdConnectConfiguration metadata,
        string idToken,
        string expectedNonce)
    {
        var oidc = config.Identity.Oidc;
        var audiences = new List<string> { authority.ClientId };
        audiences.AddRange(oidc.AdditionalAudiences);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authority.Issuer,
            ValidateAudience = true,
            ValidAudiences = audiences,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = metadata.SigningKeys,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(Math.Max(0, oidc.ClockSkewSeconds))
        });
        if (!result.IsValid)
            throw new OidcAuthenticationException("id_token validation failed: " +
                (result.Exception?.Message ?? "invalid token."));

        var identity = result.ClaimsIdentity;
        var nonce = identity.FindFirst("nonce")?.Value;
        if (string.IsNullOrEmpty(nonce) || !CryptoEquals(nonce, expectedNonce))
            throw new OidcAuthenticationException("id_token nonce did not match the login request.");
        var subject = identity.FindFirst("sub")?.Value
            ?? throw new OidcAuthenticationException("id_token did not contain a subject (sub) claim.");
        foreach (var required in oidc.RequiredClaims ?? [])
            if (!string.IsNullOrEmpty(required) && identity.FindFirst(required) is null)
                throw new OidcAuthenticationException($"id_token is missing required claim '{required}'.");

        var username = FirstClaim(identity, oidc.UsernameClaimType)
            ?? FirstClaim(identity, "preferred_username") ?? subject;
        var email = FirstClaim(identity, oidc.EmailClaimType) ?? FirstClaim(identity, "email");
        var groups = (oidc.GroupClaimTypes ?? [])
            .SelectMany(identity.FindAll)
            .Select(x => x.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new OidcIdentity(subject, username, email, groups, authority.Issuer);
    }

    private static string? FirstClaim(System.Security.Claims.ClaimsIdentity identity, string type) =>
        string.IsNullOrEmpty(type) ? null : identity.FindFirst(type)?.Value;

    private static string RandomToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    private static bool CryptoEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
}
