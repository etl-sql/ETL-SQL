using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Controllers;

/// <summary>
/// The endpoint enrolled machines poll for their signed organization policy. Machines authenticate
/// with the enrollment headers the v0.13.0 client runtime already sends (tenant, enrollment ID,
/// machine ID) plus an optional TLS client certificate; the served envelope is bound to the
/// tenant/environment recorded in the machine registry — never to what the caller claims. Unknown,
/// revoked, and reassigned identities are refused with a uniform error, and every denial is audited.
/// </summary>
[ApiController]
[Route("api/policy-authority")]
public class PolicyDistributionController(
    PortalDbContext db,
    PolicyAuthorityService authority,
    AuditService audit,
    IConfiguration configuration,
    DedicatedPolicyAuthorityGuard tenantGuard) : ControllerBase
{
    private const string DeniedMessage = "This machine identity is not authorized for policy retrieval.";

    [HttpGet("envelope")]
    [AllowAnonymous]
    [EnableRateLimiting("anonymous-token")]
    public async Task<IActionResult> GetEnvelope(CancellationToken cancellationToken)
    {
        var tenant = Request.Headers[EnterprisePolicyTransport.TenantHeader].ToString();
        var enrollmentId = Request.Headers[EnterprisePolicyTransport.EnrollmentHeader].ToString();
        var machineId = Request.Headers[EnterprisePolicyTransport.MachineHeader].ToString();
        if (string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(enrollmentId)
            || string.IsNullOrWhiteSpace(machineId))
            return await DenyAsync(machineId, "Missing enrollment headers.", StatusCodes.Status401Unauthorized);

        var machine = await db.PolicyMachines
            .FirstOrDefaultAsync(m => m.MachineId == machineId, cancellationToken);
        if (machine is null)
            return await DenyAsync(machineId, "Unknown machine identity.");
        try
        {
            tenantGuard.AuthorizeRead(machine.Tenant);
        }
        catch (UnauthorizedAccessException)
        {
            return await DenyAsync(machineId,
                "Machine registration is outside this host's Dedicated tenant authority.");
        }
        if (machine.Revoked)
            return await DenyAsync(machineId, $"Machine identity is revoked ({machine.RevokedReason ?? "no reason recorded"}).");
        if (!string.Equals(machine.EnrollmentId, enrollmentId, StringComparison.Ordinal)
            || !string.Equals(machine.Tenant, tenant, StringComparison.Ordinal))
            return await DenyAsync(machineId,
                $"Presented enrollment/tenant does not match the registration (reassigned or copied identity); " +
                $"presented tenant '{tenant}'.");

        if (!string.IsNullOrWhiteSpace(machine.ClientCertificateThumbprint))
        {
            var certificate = await ResolveClientCertificateAsync(cancellationToken);
            if (certificate is null)
                return await DenyAsync(machineId, "A TLS client certificate is required but was not presented.");
            if (!ThumbprintMatches(certificate, machine.ClientCertificateThumbprint))
                return await DenyAsync(machineId, "The presented client certificate does not match the registered thumbprint.");
        }

        // The response is bound to the registered tenant/environment, never to caller-supplied values.
        // A machine in the current canary cohort receives the canary version; everyone else stays on the
        // fleet-wide active version. Cohort membership is decided from the machine's own registered
        // identity and group label, never from caller-supplied values.
        var canary = await authority.GetCanaryVersionAsync(machine.Tenant, machine.Environment, cancellationToken);
        var served = canary?.Canary is not null
            && canary.Canary.Includes(machine.MachineId, machine.CanaryGroup)
                ? canary
                : await authority.GetActiveVersionAsync(machine.Tenant, machine.Environment, cancellationToken);
        if (served is null)
            return NotFound(new { error = "No active policy is published for this machine's tenant/environment." });

        machine.LastSeenAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        // Serve the stored envelope verbatim — re-serializing could disturb the signed payload.
        return Content(served.SignedEnvelopeJson, "application/json");
    }

    /// <summary>Prefers the TLS-negotiated certificate; falls back to a base64-DER forwarding header
    /// only when an administrator has explicitly configured its name for a terminating proxy.</summary>
    private async Task<X509Certificate2?> ResolveClientCertificateAsync(CancellationToken cancellationToken)
    {
        var tls = await HttpContext.Connection.GetClientCertificateAsync(cancellationToken);
        if (tls is not null)
            return tls;

        var headerName = configuration["Portal:PolicyAuthority:ClientCertificateForwardingHeader"];
        if (string.IsNullOrWhiteSpace(headerName)
            || !Request.Headers.TryGetValue(headerName, out var forwarded)
            || string.IsNullOrWhiteSpace(forwarded))
            return null;
        try
        {
            return X509CertificateLoader.LoadCertificate(Convert.FromBase64String(forwarded.ToString()));
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }

    private static bool ThumbprintMatches(X509Certificate2 certificate, string registered)
    {
        var actual = registered.Length == 64
            ? Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256))
            : certificate.Thumbprint;
        return string.Equals(actual, registered, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<IActionResult> DenyAsync(
        string machineId, string reason, int statusCode = StatusCodes.Status403Forbidden)
    {
        await audit.LogAsync(null, "POLICY_ENVELOPE_DENIED", "PolicyMachine",
            string.IsNullOrWhiteSpace(machineId) ? null : machineId,
            reason, actorType: "machine", actorId: string.IsNullOrWhiteSpace(machineId) ? null : machineId);
        return StatusCode(statusCode, new { error = DeniedMessage });
    }
}
