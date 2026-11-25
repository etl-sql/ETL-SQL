using System.Net;
using System.Text;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Controllers;

/// <summary>
/// ST56 Slice 1: Native client authentication endpoints (RFC 8252 loopback + PKCE S256).
/// Handles browser sign-in continuation, same-origin CSRF-protected consent confirmation,
/// one-time authorization code exchange, native refresh-token rotation, and single-session logout.
/// </summary>
[ApiController]
[Route("api/auth/client")]
[EnableRateLimiting("auth")]
public sealed class NativeClientAuthController(
    NativeClientAuthService nativeAuth,
    UserManager<PortalUser> userManager,
    SignInManager<PortalUser> signInManager,
    ILdapService ldapService,
    AuditService auditService,
    PortalDbContext db,
    PortalConfig config) : ControllerBase
{
    [HttpGet("authorize")]
    [AllowAnonymous]
    public async Task<IActionResult> Authorize(
        [FromQuery(Name = "client_id")] string? clientId,
        [FromQuery(Name = "redirect_uri")] string? redirectUri,
        [FromQuery(Name = "code_challenge")] string? codeChallenge,
        [FromQuery(Name = "code_challenge_method")] string? codeChallengeMethod,
        [FromQuery(Name = "state")] string? state,
        [FromQuery(Name = "scope")] string? scope,
        CancellationToken ct)
    {
        var started = await nativeAuth.StartAuthorizationAsync(
            Request, clientId, redirectUri, codeChallenge, codeChallengeMethod, state, scope, ct);
        if (!started.Succeeded || started.Transaction is null || started.ProtectedCookie is null)
            return StatusCode(started.StatusCode, new { error = started.Error, detail = started.Detail });

        Response.Cookies.Append(
            NativeClientAuthService.TransactionCookieName,
            started.ProtectedCookie,
            nativeAuth.BuildTransactionCookieOptions(Request, expires: true));

        var txId = Uri.EscapeDataString(started.Transaction.Id);
        return started.UseOidc
            ? Redirect($"/api/auth/oidc/login?native_tx={txId}")
            : Redirect($"/api/auth/client/login?tx={txId}");
    }

    [HttpGet("login")]
    [AllowAnonymous]
    public async Task<IActionResult> LoginPage([FromQuery] string? tx, CancellationToken ct)
    {
        if (config.SharedTenancy.Enabled)
            return Unauthorized(new { error = "Use single sign-on for this host" });

        var (transaction, cookie) = await nativeAuth.ValidatePendingLoginAsync(Request, tx, ct: ct);
        if (transaction is null || cookie is null)
            return BadRequest(new { error = "invalid_transaction", detail = "The sign-in request has expired or is invalid." });

        if (!NativeClientRegistry.TryGet(transaction.ClientId, out var client) || client is null)
            return BadRequest(new { error = "invalid_client" });

        return Content(RenderLoginHtml(transaction, client, cookie.CsrfToken, errorMessage: null), "text/html; charset=utf-8");
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> LoginSubmit(
        [FromForm(Name = "tx")] string? tx,
        [FromForm(Name = "csrf_token")] string? csrfToken,
        [FromForm(Name = "username")] string? username,
        [FromForm(Name = "password")] string? password,
        CancellationToken ct)
    {
        if (config.SharedTenancy.Enabled)
            return Unauthorized(new { error = "Use single sign-on for this host" });

        var (transaction, cookie) = await nativeAuth.ValidatePendingLoginAsync(Request, tx, ct: ct);
        if (transaction is null || cookie is null)
            return BadRequest(new { error = "invalid_transaction", detail = "The sign-in request has expired or is invalid." });

        if (!NativeClientRegistry.TryGet(transaction.ClientId, out var client) || client is null)
            return BadRequest(new { error = "invalid_client" });

        if (string.IsNullOrWhiteSpace(csrfToken) || !string.Equals(cookie.CsrfToken, csrfToken, StringComparison.Ordinal))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "invalid_csrf_or_session" });

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return Content(RenderLoginHtml(transaction, client, cookie.CsrfToken, "Username and password are required."), "text/html; charset=utf-8");

        var (user, error) = await AuthenticateDedicatedUserAsync(username, password, ct);
        if (user is null)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Content(RenderLoginHtml(transaction, client, cookie.CsrfToken, error ?? "Invalid credentials."), "text/html; charset=utf-8");
        }

        if (user.MustChangePassword)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return Content(RenderLoginHtml(
                transaction,
                client,
                cookie.CsrfToken,
                "Your password is temporary. Sign in to the Portal in your browser and change your password first."), "text/html; charset=utf-8");
        }

        var updatedCookie = await nativeAuth.CompleteLoginAsync(Request, transaction.Id, user, transaction.TenantId, ct);
        if (updatedCookie is null)
            return BadRequest(new { error = "invalid_transaction" });

        Response.Cookies.Append(
            NativeClientAuthService.TransactionCookieName,
            updatedCookie,
            nativeAuth.BuildTransactionCookieOptions(Request, expires: true));

        return Redirect($"/api/auth/client/consent?tx={Uri.EscapeDataString(transaction.Id)}");
    }

    [HttpGet("consent")]
    [AllowAnonymous]
    public async Task<IActionResult> ConsentPage([FromQuery] string? tx, CancellationToken ct)
    {
        var view = await nativeAuth.LoadPendingConsentAsync(Request, tx, ct);
        if (view is null)
            return Unauthorized(new { error = "authentication_required", detail = "Complete browser sign-in before approving client access." });

        // Allow the browser to follow the 302 redirect from the same-origin consent POST to the
        // validated loopback callback origin without relaxing global CSP elsewhere.
        if (Uri.TryCreate(view.Transaction.RedirectUri, UriKind.Absolute, out var redirectUri)
            && Response.Headers.TryGetValue("Content-Security-Policy", out var cspValues))
        {
            var loopbackOrigin = redirectUri.GetLeftPart(UriPartial.Authority);
            var csp = cspValues.ToString().Replace(
                "form-action 'self'",
                $"form-action 'self' {loopbackOrigin}",
                StringComparison.Ordinal);
            Response.Headers["Content-Security-Policy"] = csp;
        }

        return Content(RenderConsentHtml(view), "text/html; charset=utf-8");
    }

    [HttpPost("consent")]
    [AllowAnonymous]
    public async Task<IActionResult> ConsentSubmit(
        [FromForm(Name = "tx")] string? tx,
        [FromForm(Name = "csrf_token")] string? csrfToken,
        [FromForm(Name = "decision")] string? decision,
        CancellationToken ct)
    {
        var outcome = await nativeAuth.CompleteConsentAsync(Request, tx, csrfToken, decision, ct);
        if (!outcome.Succeeded || string.IsNullOrEmpty(outcome.RedirectUrl))
            return StatusCode(outcome.StatusCode, new { error = outcome.Error });

        Response.Cookies.Delete(
            NativeClientAuthService.TransactionCookieName,
            nativeAuth.BuildTransactionCookieOptions(Request, expires: false));

        return Redirect(outcome.RedirectUrl);
    }

    [HttpPost("exchange")]
    [AllowAnonymous]
    public async Task<IActionResult> Exchange([FromBody] NativeClientExchangeRequest req, CancellationToken ct)
    {
        var outcome = await nativeAuth.ExchangeCodeAsync(Request, req, ct);
        if (!outcome.Succeeded || outcome.Response is null)
            return StatusCode(outcome.StatusCode, new { error = outcome.Error, detail = outcome.Detail });

        return Ok(outcome.Response);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] NativeClientRefreshRequest req, CancellationToken ct)
    {
        var outcome = await nativeAuth.RefreshAsync(Request, req, ct);
        if (!outcome.Succeeded || outcome.Response is null)
            return StatusCode(outcome.StatusCode, new { error = outcome.Error, detail = outcome.Detail });

        return Ok(outcome.Response);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout([FromBody] NativeClientLogoutRequest? req, CancellationToken ct)
    {
        var statusCode = await nativeAuth.LogoutAsync(Request, User, req, ct);
        return statusCode == StatusCodes.Status204NoContent
            ? NoContent()
            : StatusCode(statusCode, new { error = statusCode == StatusCodes.Status403Forbidden ? "native_client_session_required" : "unauthorized" });
    }

    private async Task<(PortalUser? User, string? Error)> AuthenticateDedicatedUserAsync(
        string rawUsername,
        string password,
        CancellationToken ct)
    {
        var isDomainQualified = rawUsername.Contains('@') || rawUsername.Contains('\\');
        var accountName = LdapService.AccountName(
            rawUsername,
            config.Identity.Ldap.Enabled ? config.Identity.Ldap.Domain : null);
        if (accountName is null)
            return (null, "Invalid credentials");

        var user = await userManager.FindByNameAsync(accountName);
        if (user is not null && !user.IsActive)
        {
            await auditService.LogAsync(user.Id, "LOGIN_FAILED", "User", user.Id.ToString(), "Account is disabled.");
            return (null, "Invalid credentials");
        }

        var useLdap = config.Identity.Ldap.Enabled
            && ((user is not null && user.Provider == "LDAP")
                || (user is null && (isDomainQualified || !string.IsNullOrEmpty(config.Identity.Ldap.Domain))));

        if (useLdap)
        {
            var ldapResult = await ldapService.AuthenticateAsync(rawUsername, password);
            if (ldapResult is null)
            {
                if (user is not null)
                    await auditService.LogAsync(user.Id, "LOGIN_FAILED", "User", user.Id.ToString(), "LDAP authentication failed.");
                return (null, "Invalid LDAP credentials");
            }

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                if (user is null)
                {
                    user = new PortalUser
                    {
                        UserName = ldapResult.Username,
                        Email = ldapResult.Email ?? $"{ldapResult.Username}@{config.Identity.Ldap.Domain}",
                        FirstName = ldapResult.FirstName,
                        LastName = ldapResult.LastName,
                        IsActive = true,
                        MustChangePassword = false,
                        Provider = "LDAP"
                    };
                    var created = await userManager.CreateAsync(user);
                    if (!created.Succeeded)
                    {
                        await transaction.RollbackAsync(ct);
                        return (null, "Unable to provision LDAP account.");
                    }
                }
                else
                {
                    user.Email = ldapResult.Email ?? user.Email;
                    user.FirstName = ldapResult.FirstName ?? user.FirstName;
                    user.LastName = ldapResult.LastName ?? user.LastName;
                    await userManager.UpdateAsync(user);
                }

                await transaction.CommitAsync(ct);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }

            return (user, null);
        }

        if (user is null || !user.IsActive)
            return (null, "Invalid credentials");
        if (user.Provider == "LDAP")
            return (null, "Invalid LDAP credentials");
        if (user.Provider == "OIDC")
            return (null, "Use single sign-on for this account");

        var check = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (!check.Succeeded)
        {
            await auditService.LogAsync(user.Id, "LOGIN_FAILED", "User", user.Id.ToString());
            return check.IsLockedOut
                ? (null, "Account locked. Try again in 15 minutes.")
                : (null, "Invalid credentials");
        }

        return (user, null);
    }

    private static string RenderLoginHtml(
        NativeClientAuthTransaction tx,
        NativeClientDefinition client,
        string csrfToken,
        string? errorMessage)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append("<title>Sign In — ETL-SQL Portal</title></head><body>");
        sb.Append($"<h1>Sign in to authorize {Html(client.DisplayName)}</h1>");
        sb.Append($"<p>Portal: <strong>{Html(tx.CanonicalOrigin)}</strong> &middot; Tenant: <strong>{Html(tx.TenantId)}</strong></p>");
        if (!string.IsNullOrEmpty(errorMessage))
            sb.Append($"<p role=\"alert\">{Html(errorMessage)}</p>");
        sb.Append("<form method=\"post\" action=\"/api/auth/client/login\">");
        sb.Append($"<input type=\"hidden\" name=\"tx\" value=\"{Html(tx.Id)}\">");
        sb.Append($"<input type=\"hidden\" name=\"csrf_token\" value=\"{Html(csrfToken)}\">");
        sb.Append("<label for=\"username\">Username</label>");
        sb.Append("<input id=\"username\" name=\"username\" type=\"text\" autocomplete=\"username\" required>");
        sb.Append("<label for=\"password\">Password</label>");
        sb.Append("<input id=\"password\" name=\"password\" type=\"password\" autocomplete=\"current-password\" required>");
        sb.Append("<button type=\"submit\">Continue</button>");
        sb.Append("</form></body></html>");
        return sb.ToString();
    }

    private static string RenderConsentHtml(NativeClientAuthService.PendingConsentView view)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append("<title>Authorize Client — ETL-SQL Portal</title></head><body>");
        sb.Append($"<h1>Authorize {Html(view.Client.DisplayName)}?</h1>");
        sb.Append($"<p>Signed in as <strong>{Html(view.User.UserName ?? string.Empty)}</strong>");
        sb.Append($" on <strong>{Html(view.Transaction.CanonicalOrigin)}</strong>");
        sb.Append($" (tenant <strong>{Html(view.Transaction.TenantId)}</strong>).</p>");
        sb.Append($"<p>Client ID: <code>{Html(view.Client.ClientId)}</code> &middot; Callback: <code>{Html(view.Transaction.RedirectUri)}</code></p>");
        sb.Append("<h2>Requested permissions</h2><ul>");
        foreach (var scope in view.Scopes)
            sb.Append($"<li><code>{Html(scope)}</code> — {Html(NativeClientScopes.Describe(scope))}</li>");
        sb.Append("</ul>");
        sb.Append("<form method=\"post\" action=\"/api/auth/client/consent\">");
        sb.Append($"<input type=\"hidden\" name=\"tx\" value=\"{Html(view.Transaction.Id)}\">");
        sb.Append($"<input type=\"hidden\" name=\"csrf_token\" value=\"{Html(view.CsrfToken)}\">");
        sb.Append("<button type=\"submit\" name=\"decision\" value=\"approve\">Approve</button> ");
        sb.Append("<button type=\"submit\" name=\"decision\" value=\"deny\">Cancel</button>");
        sb.Append("</form></body></html>");
        return sb.ToString();
    }

    private static string Html(string value) => WebUtility.HtmlEncode(value);
}
