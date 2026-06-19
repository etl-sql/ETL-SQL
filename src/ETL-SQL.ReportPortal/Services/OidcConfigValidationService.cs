using Microsoft.Extensions.Hosting;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// Validates the OIDC configuration at startup when federated login is enabled
/// (Portal:Identity:Oidc:Enabled). Fails secure: a misconfigured identity provider stops the host
/// rather than silently serving with broken or unsafe authentication. Runs as a hosted service so
/// WebApplicationFactory can inject test config first (same pattern as
/// <see cref="JwtSecretValidationService"/>); PortalWebFactory strips hosted services for ordinary
/// API tests, and the hosted-service lane asserts both outcomes.
/// </summary>
public sealed class OidcConfigValidationService(PortalConfig config, IHostApplicationLifetime lifetime)
    : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        var oidc = config.Identity.Oidc;
        if (!oidc.Enabled)
            return Task.CompletedTask; // OIDC off: nothing to validate, Local/LDAP unchanged

        foreach (var error in Validate(oidc))
        {
            Console.Error.WriteLine("FATAL: " + error);
            lifetime.StopApplication();
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>Returns human-readable configuration errors (empty when valid). Pure and allocation-light
    /// so diagnostics (P2.1) and tests can reuse it without starting the host.</summary>
    public static IEnumerable<string> Validate(OidcIdentityConfig oidc)
    {
        if (string.IsNullOrWhiteSpace(oidc.Authority))
        {
            yield return "Portal:Identity:Oidc:Authority is required when OIDC is enabled.";
        }
        else if (!Uri.TryCreate(oidc.Authority, UriKind.Absolute, out var authority)
                 || !string.Equals(authority.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            yield return "Portal:Identity:Oidc:Authority must be an absolute HTTPS URI.";
        }

        if (string.IsNullOrWhiteSpace(oidc.ClientId))
            yield return "Portal:Identity:Oidc:ClientId is required when OIDC is enabled.";

        if (string.IsNullOrWhiteSpace(oidc.ClientSecret))
            yield return "Portal:Identity:Oidc:ClientSecret is required for the authorization-code exchange.";

        if (oidc.Scopes is null || oidc.Scopes.Length == 0
            || !oidc.Scopes.Any(s => string.Equals(s, "openid", StringComparison.OrdinalIgnoreCase)))
            yield return "Portal:Identity:Oidc:Scopes must include 'openid'.";

        if (string.IsNullOrWhiteSpace(oidc.CallbackPath) || !oidc.CallbackPath.StartsWith('/'))
            yield return "Portal:Identity:Oidc:CallbackPath must be an absolute path beginning with '/'.";

        if (string.IsNullOrWhiteSpace(oidc.PostLoginRedirectPath) || !oidc.PostLoginRedirectPath.StartsWith('/'))
            yield return "Portal:Identity:Oidc:PostLoginRedirectPath must be a relative path beginning with '/'.";

        if (oidc.ClockSkewSeconds < 0)
            yield return "Portal:Identity:Oidc:ClockSkewSeconds must be zero or greater.";
    }
}
