using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace ETL_SQL.Portal.Services;

public sealed record SharedOidcFlowStart(
    string ProtectedState,
    SharedIdentityAuthorityBinding Authority);

public sealed record SharedOidcFlowResume(
    SharedIdentityAuthorityBinding Authority,
    string Nonce,
    string CodeVerifier,
    string RedirectUri);

/// <summary>
/// Pins an anonymous Shared OIDC login to the exact server-routed authority selected before the
/// redirect. The callback restores this protected envelope and never reselects tenant or issuer
/// from callback request values.
/// </summary>
public sealed class SharedOidcFlowStateService(
    SharedIdentityAuthorityResolver authorities,
    IDataProtectionProvider dataProtection,
    TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly IDataProtector _protector = dataProtection.CreateProtector(
        "ETL_SQL.Portal.SharedOidc.LoginFlow.v1");

    private sealed record Payload(
        string AuthorityId,
        long AuthorityVersion,
        string PortalHost,
        string State,
        string Nonce,
        string CodeVerifier,
        string RedirectUri,
        DateTimeOffset IssuedAtUtc,
        DateTimeOffset ExpiresAtUtc);

    public async Task<SharedOidcFlowStart> BeginAsync(
        HttpRequest request,
        OidcAuthorizationRequest authorization,
        string redirectUri,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(authorization);
        var authority = await authorities.ResolveForRequestAsync(request, ct)
            ?? throw new OidcAuthenticationException(
                "No enabled identity authority is registered for the routed Portal host.");
        return Begin(authority, authorization, redirectUri);
    }

    public SharedOidcFlowStart Begin(
        SharedIdentityAuthorityBinding authority,
        OidcAuthorizationRequest authorization,
        string redirectUri)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(authorization);
        var normalizedRedirect = RequireRedirectUri(redirectUri, authority.PortalHost);
        var now = clock.GetUtcNow();
        var payload = new Payload(
            authority.AuthorityId,
            authority.Version,
            authority.PortalHost,
            RequireToken(authorization.State, "state"),
            RequireToken(authorization.Nonce, "nonce"),
            RequireToken(authorization.CodeVerifier, "PKCE verifier"),
            normalizedRedirect,
            now,
            now.Add(Lifetime));
        return new SharedOidcFlowStart(
            _protector.Protect(JsonSerializer.Serialize(payload, Json)), authority);
    }

    public async Task<SharedOidcFlowResume> ResumeAsync(
        string protectedState,
        string callbackState,
        CancellationToken ct = default)
    {
        Payload payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(
                _protector.Unprotect(protectedState), Json)
                ?? throw new InvalidOperationException();
        }
        catch (Exception ex) when (ex is not OidcAuthenticationException)
        {
            throw new OidcAuthenticationException("Shared OIDC flow state is invalid or has been tampered with.");
        }

        var now = clock.GetUtcNow();
        if (payload.ExpiresAtUtc <= now || payload.IssuedAtUtc > now.AddMinutes(1))
            throw new OidcAuthenticationException("Shared OIDC flow state has expired or is not yet valid.");
        if (!CryptoEquals(RequireToken(callbackState, "callback state"), payload.State))
            throw new OidcAuthenticationException("Shared OIDC callback state does not match the login flow.");

        var authority = await authorities.ResolveProtectedFlowAsync(
            payload.AuthorityId, payload.PortalHost, payload.AuthorityVersion, ct)
            ?? throw new OidcAuthenticationException(
                "The identity authority was disabled or changed while the login flow was active.");

        var redirect = RequireRedirectUri(payload.RedirectUri, authority.PortalHost);
        return new SharedOidcFlowResume(
            authority,
            RequireToken(payload.Nonce, "nonce"),
            RequireToken(payload.CodeVerifier, "PKCE verifier"),
            redirect);
    }

    private static string RequireRedirectUri(string value, string portalHost)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.Equals(
                SharedIdentityAuthorityService.NormalizeDomain(uri.Host, "redirectHost"),
                portalHost,
                StringComparison.Ordinal))
        {
            throw new OidcAuthenticationException(
                "Shared OIDC redirect URI must be HTTPS and match the server-routed Portal host.");
        }
        return uri.AbsoluteUri;
    }

    private static string RequireToken(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048)
            throw new OidcAuthenticationException($"Shared OIDC {name} is missing or invalid.");
        return value;
    }

    private static bool CryptoEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
}
