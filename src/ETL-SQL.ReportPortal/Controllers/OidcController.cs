using System.Text.Json;
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
    IDataProtectionProvider dataProtection,
    AuditService auditService,
    ILogger<OidcController> log) : ControllerBase
{
    private const string FlowCookie = "ETLSQL_OIDC_FLOW";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private IDataProtector Protector => dataProtection.CreateProtector("ETL_SQL.ReportPortal.Oidc.LoginFlow.v1");

    private sealed record FlowState(string State, string Nonce, string CodeVerifier);

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
            return Redirect("/login.html?error=sso_failed");
        }

        FlowState? flow = ReadFlow(cookie);
        if (flow is null || string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state)
            || !CryptoEquals(state, flow.State))
        {
            log.LogWarning("OIDC callback rejected: missing/invalid flow state");
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
            await auditService.LogAsync(null, "LOGIN_FAILED", "User", null, "OIDC authentication failed.");
            return Redirect("/login.html?error=sso_failed");
        }

        if (result.Disabled || result.Session is null)
            return Redirect("/login.html?error=account_disabled");

        return Redirect(BuildSuccessRedirect(result.Session));
    }

    private string BuildRedirectUri() =>
        $"{Request.Scheme}://{Request.Host}{config.Identity.Oidc.CallbackPath}";

    private string BuildSuccessRedirect(OidcSession session)
    {
        // SPA handoff via URL fragment: the fragment is not sent to servers or written to most
        // access logs, and the SPA stores the tokens exactly as it does after a password login.
        var fragment =
            $"access_token={Uri.EscapeDataString(session.AccessToken)}" +
            $"&refresh_token={Uri.EscapeDataString(session.RefreshToken)}" +
            $"&expires_at={Uri.EscapeDataString(session.ExpiresAt.ToString("O"))}";
        return $"{config.Identity.Oidc.PostLoginRedirectPath}#{fragment}";
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
