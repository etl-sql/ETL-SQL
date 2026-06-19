using System.Text.Json;
using ETL_SQL.Core.Common;
using ETL_SQL.ReportPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ETL_SQL.ReportPortal.Controllers;

/// <summary>
/// Federated OIDC login (P1.2): bridges an external identity provider's authorization-code flow to
/// the portal's own JWT/refresh-token session, exactly like password and LDAP logins. The per-flow
/// secrets (state, nonce, PKCE verifier) ride in a short-lived, encrypted, HttpOnly cookie rather
/// than the URL. Endpoints report 404 when OIDC is disabled so the local flow is unaffected.
/// </summary>
[ApiController]
[Route("api/auth/oidc")]
[EnableRateLimiting("auth")]
public sealed class OidcController(
    PortalConfig config,
    IOidcAuthenticationService oidc,
    OidcUserProvisioningService provisioning,
    IOidcDiscoveryProvider discovery,
    IDataProtectionProvider dataProtection,
    AuditService auditService,
    ILogger<OidcController> log) : ControllerBase
{
    private const string FlowCookie = "ETLSQL_OIDC_FLOW";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private IDataProtector Protector => dataProtection.CreateProtector("ETL_SQL.ReportPortal.Oidc.LoginFlow.v1");

    private sealed record FlowState(string State, string Nonce, string CodeVerifier);

    /// <summary>Admin-only OIDC diagnostics (P2.1): the effective configuration with the client
    /// secret redacted to a presence flag, the startup validation errors, and a live discovery
    /// reachability probe — so an operator can see why federated login is failing without reading
    /// logs or exposing secrets.</summary>
    [HttpGet("diagnostics")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Diagnostics(CancellationToken ct)
    {
        var o = config.Identity.Oidc;
        return Ok(new
        {
            enabled = o.Enabled,
            authority = o.Authority,
            clientId = o.ClientId,
            clientSecretConfigured = !string.IsNullOrEmpty(o.ClientSecret),
            scopes = o.Scopes,
            callbackPath = o.CallbackPath,
            postLoginRedirectPath = o.PostLoginRedirectPath,
            groupClaimTypes = o.GroupClaimTypes,
            requiredClaims = o.RequiredClaims,
            clockSkewSeconds = o.ClockSkewSeconds,
            configErrors = OidcConfigValidationService.Validate(o).ToArray(),
            discovery = await ProbeDiscoveryAsync(ct)
        });
    }

    private async Task<object> ProbeDiscoveryAsync(CancellationToken ct)
    {
        if (!config.Identity.Oidc.Enabled)
            return new { reachable = false, error = "OIDC is disabled." };
        try
        {
            var c = await discovery.GetConfigurationAsync(ct);
            return new
            {
                reachable = true,
                issuer = c.Issuer,
                authorizationEndpoint = c.AuthorizationEndpoint,
                tokenEndpoint = c.TokenEndpoint,
                signingKeyCount = c.SigningKeys.Count
            };
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "OIDC discovery probe failed");
            return new { reachable = false, error = SecretRedactor.Redact(ex.Message) };
        }
    }

    [HttpGet("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(CancellationToken ct)
    {
        if (!oidc.Enabled)
            return NotFound(new { error = "oidc_not_configured" });

        var redirectUri = BuildRedirectUri();
        OidcAuthorizationRequest request;
        try
        {
            request = await oidc.BuildAuthorizationRequestAsync(redirectUri, ct);
        }
        catch (OidcAuthenticationException ex)
        {
            log.LogError(ex, "OIDC login could not start");
            return Redirect("/login.html?error=sso_unavailable");
        }

        var payload = JsonSerializer.Serialize(new FlowState(request.State, request.Nonce, request.CodeVerifier), Json);
        Response.Cookies.Append(FlowCookie, Protector.Protect(payload), FlowCookieOptions(expires: true));
        return Redirect(request.AuthorizationUrl);
    }

    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken ct)
    {
        if (!oidc.Enabled)
            return NotFound(new { error = "oidc_not_configured" });

        // Always clear the one-time flow cookie before doing anything else.
        var cookie = Request.Cookies[FlowCookie];
        Response.Cookies.Delete(FlowCookie, FlowCookieOptions(expires: false));

        if (!string.IsNullOrEmpty(error))
        {
            log.LogWarning("OIDC provider returned error {Error}", error);
            await auditService.LogAsync(null, "LOGIN_FAILED", "User", null, $"OIDC provider returned error: {error}");
            return Redirect("/login.html?error=sso_failed");
        }

        FlowState? flow = ReadFlow(cookie);
        if (flow is null || string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state)
            || !CryptoEquals(state, flow.State))
        {
            // A missing/forged flow or mismatched state is a tampering/CSRF signal — audit it.
            log.LogWarning("OIDC callback rejected: missing/invalid flow state");
            await auditService.LogAsync(null, "LOGIN_FAILED", "User", null, "OIDC callback state validation failed.");
            return Redirect("/login.html?error=sso_failed");
        }

        OidcUserProvisioningService.Result result;
        try
        {
            var identity = await oidc.CompleteAsync(code, flow.CodeVerifier, BuildRedirectUri(), flow.Nonce, ct);
            result = await provisioning.SignInAsync(identity, ct);
        }
        catch (OidcAuthenticationException ex)
        {
            log.LogWarning(ex, "OIDC authentication failed");
            await auditService.LogAsync(null, "LOGIN_FAILED", "User", null, "OIDC authentication failed: " + ex.Message);
            return Redirect("/login.html?error=sso_failed");
        }

        if (result.Refused)
            return Redirect("/login.html?error=sso_failed");
        if (result.Disabled || result.Session is null)
            return Redirect("/login.html?error=account_disabled");

        // Hand the session to the SPA via a server-rendered page (tokens in a JSON data-island read
        // by a same-origin script), never the URL — so the long-lived refresh token does not land in
        // browser history or referrers. Cache-Control: no-store is already applied globally.
        return Content(BuildHandoffPage(result.Session), "text/html; charset=utf-8");
    }

    private string BuildRedirectUri() =>
        $"{Request.Scheme}://{Request.Host}{config.Identity.Oidc.CallbackPath}";

    private string BuildHandoffPage(OidcSession session)
    {
        // Tokens travel in a non-executable JSON data-island, read by the same-origin module
        // /js/sso-complete.js (CSP allows script-src 'self'). System.Text.Json escapes the values;
        // JWT/base64 tokens contain no '<', so they cannot break out of the script element.
        var payload = JsonSerializer.Serialize(new
        {
            token = session.AccessToken,
            refreshToken = session.RefreshToken,
            expiresAt = session.ExpiresAt.ToString("O"),
            redirect = config.Identity.Oidc.PostLoginRedirectPath
        }, Json);

        return "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">" +
               "<title>Signing in…</title></head><body>" +
               "<p>Signing in…</p>" +
               $"<script type=\"application/json\" id=\"sso-data\">{payload}</script>" +
               "<script type=\"module\" src=\"/js/sso-complete.js\"></script>" +
               "</body></html>";
    }

    private FlowState? ReadFlow(string? cookie)
    {
        if (string.IsNullOrEmpty(cookie)) return null;
        try
        {
            return JsonSerializer.Deserialize<FlowState>(Protector.Unprotect(cookie), Json);
        }
        catch
        {
            return null; // tampered/expired/undecryptable cookie → treat as no flow
        }
    }

    private CookieOptions FlowCookieOptions(bool expires) => new()
    {
        HttpOnly = true,
        Secure = Request.IsHttps,
        SameSite = SameSiteMode.Lax, // callback is a top-level GET navigation
        IsEssential = true,
        Path = "/api/auth/oidc",
        MaxAge = expires ? TimeSpan.FromMinutes(10) : TimeSpan.Zero
    };

    private static bool CryptoEquals(string a, string b) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));
}
