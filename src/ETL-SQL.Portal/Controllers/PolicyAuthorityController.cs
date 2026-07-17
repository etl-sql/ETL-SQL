using System.Security.Claims;
using System.Text.Json;
using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Controllers;

/// <summary>
/// Administrator workflow over the organization policy authority: validate, publish (optionally
/// staged), activate, roll back, and inspect signed policy versions per tenant/environment.
/// Signing happens through the externally referenced certificate key — this API never receives,
/// stores, or returns private-key material.
/// </summary>
[ApiController]
[Route("api/admin/policy-authority")]
[Authorize(Roles = "Admin")]
public class PolicyAuthorityController(
    PolicyAuthorityService authority,
    IPolicyEnvelopeSigner signer,
    PortalDbContext db,
    AuditService audit) : ControllerBase
{
    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private string CurrentUserName => User.Identity?.Name ?? $"user:{CurrentUserId}";

    /// <summary>True when the previously active envelope no longer verifies under the currently
    /// configured signing key — i.e. this publish is the first after a signing-key rotation. The
    /// durable audit trail records rotations because machines pin the key at enrollment and must be
    /// re-enrolled (or re-provisioned) to accept envelopes signed with the new key.</summary>
    private bool SigningKeyRotatedSince(PublishedPolicyVersion? previousActive)
    {
        if (previousActive is null)
            return false;
        var envelope = JsonSerializer.Deserialize<SignedOrganizationPolicyEnvelope>(
            previousActive.SignedEnvelopeJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return envelope is not null
            && !EnterprisePolicySignature.VerifiesWithKey(envelope, signer.PublicKeyPem);
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        try
        {
            return Ok(new { configured = true, signingPublicKeyPem = signer.PublicKeyPem });
        }
        catch (PolicyAuthorityException ex)
        {
            return Ok(new { configured = false, error = ex.Message });
        }
    }

    [HttpPost("validate")]
    public IActionResult Validate([FromBody] PolicyValidateRequest request)
    {
        try
        {
            var document = OrganizationPolicySchema.ParseJson(request.PolicyJson);
            var result = OrganizationPolicySchema.Validate(document);
            return Ok(new { isValid = result.IsValid, errors = result.Errors });
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return Ok(new { isValid = false, errors = new[] { ex.Message } });
        }
    }

    [HttpGet("versions")]
    public async Task<IActionResult> ListVersions(
        [FromQuery] string tenant, [FromQuery] string environment, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(environment))
            return BadRequest(new { error = "tenant and environment are required." });
        var versions = await authority.ListVersionsAsync(tenant, environment, cancellationToken);
        return Ok(versions.Select(PolicyVersionDto.From));
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActive(
        [FromQuery] string tenant, [FromQuery] string environment, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(environment))
            return BadRequest(new { error = "tenant and environment are required." });
        var active = await authority.GetActiveVersionAsync(tenant, environment, cancellationToken);
        if (active is null)
            return NotFound(new { error = $"No active policy for {tenant}/{environment}." });
        return Ok(new
        {
            version = PolicyVersionDto.From(active),
            signedEnvelopeJson = active.SignedEnvelopeJson
        });
    }

    [HttpPost("publish")]
    public async Task<IActionResult> Publish(
        [FromBody] PolicyPublishRequest request, CancellationToken cancellationToken)
    {
        OrganizationPolicyDocument document;
        try
        {
            document = OrganizationPolicySchema.ParseJson(request.PolicyJson);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return BadRequest(new { error = ex.Message });
        }

        try
        {
            var previousActive = await authority.GetActiveVersionAsync(
                request.Tenant, request.Environment, cancellationToken);
            var version = await authority.PublishAsync(
                document, request.Tenant, request.Environment, request.PolicyVersion,
                CurrentUserName, request.Reviewer, request.ExpiresAtUtc, request.Staged,
                cancellationToken);
            await audit.LogAsync(CurrentUserId, "PUBLISH_ORG_POLICY", "OrganizationPolicy",
                $"{request.Tenant}/{request.Environment}",
                $"Version={version.PolicyVersion}; Staged={request.Staged}; " +
                $"Hash={version.PolicyHash}; Superseded={version.SupersededVersion ?? "none"}; " +
                $"SigningKeyRotated={SigningKeyRotatedSince(previousActive)}");
            return Ok(PolicyVersionDto.From(version));
        }
        catch (Exception ex) when (ex is PolicyAuthorityException or ArgumentException)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("activate")]
    public async Task<IActionResult> Activate(
        [FromBody] PolicyActivateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var version = await authority.ActivateStagedAsync(
                request.Tenant, request.Environment, request.PolicyVersion, cancellationToken);
            await audit.LogAsync(CurrentUserId, "ACTIVATE_ORG_POLICY", "OrganizationPolicy",
                $"{request.Tenant}/{request.Environment}",
                $"Version={version.PolicyVersion}; PromotedFromStaged=true");
            return Ok(PolicyVersionDto.From(version));
        }
        catch (Exception ex) when (ex is PolicyAuthorityException or ArgumentException)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ── Machine registry ──────────────────────────────────────────────────────
    // Retrieval responses are bound to the tenant/environment registered here, never to what a
    // caller claims; revocation makes the identity unusable immediately.

    [HttpGet("machines")]
    public async Task<IActionResult> ListMachines(
        [FromQuery] string? tenant, [FromQuery] string? environment, CancellationToken cancellationToken)
    {
        var query = db.PolicyMachines.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(tenant))
            query = query.Where(m => m.Tenant == tenant);
        if (!string.IsNullOrWhiteSpace(environment))
            query = query.Where(m => m.Environment == environment);
        var machines = await query.OrderBy(m => m.Id).ToListAsync(cancellationToken);
        return Ok(machines.Select(PolicyMachineDto.From));
    }

    [HttpPost("machines")]
    public async Task<IActionResult> RegisterMachine(
        [FromBody] PolicyMachineRegisterRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(request.MachineId, "N", out _)
            || !Guid.TryParseExact(request.EnrollmentId, "N", out _))
            return BadRequest(new { error = "Machine and enrollment IDs must be 32-character GUIDs." });
        if (string.IsNullOrWhiteSpace(request.Tenant) || string.IsNullOrWhiteSpace(request.Environment))
            return BadRequest(new { error = "tenant and environment are required." });

        string? thumbprint = null;
        if (!string.IsNullOrWhiteSpace(request.ClientCertificateThumbprint))
        {
            thumbprint = request.ClientCertificateThumbprint
                .Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
            if (thumbprint.Length is not (40 or 64) || thumbprint.Any(c => !Uri.IsHexDigit(c)))
                return BadRequest(new { error = "Client certificate thumbprint must be a SHA-1 or SHA-256 hexadecimal value." });
        }

        var existing = await db.PolicyMachines
            .FirstOrDefaultAsync(m => m.MachineId == request.MachineId, cancellationToken);
        if (existing is not null && !existing.Revoked)
            return BadRequest(new
            {
                error = $"Machine '{request.MachineId}' is already registered to " +
                        $"{existing.Tenant}/{existing.Environment}; revoke it before re-registering."
            });

        var reRegistered = existing is not null;
        if (existing is null)
        {
            existing = new PolicyMachineEntity { MachineId = request.MachineId };
            db.PolicyMachines.Add(existing);
        }
        existing.EnrollmentId = request.EnrollmentId;
        existing.Tenant = request.Tenant;
        existing.Environment = request.Environment;
        existing.ClientCertificateThumbprint = thumbprint;
        existing.CanaryGroup = string.IsNullOrWhiteSpace(request.CanaryGroup)
            ? null : request.CanaryGroup.Trim();
        existing.Revoked = false;
        existing.RevokedAtUtc = null;
        existing.RevokedReason = null;
        existing.RegisteredAtUtc = DateTimeOffset.UtcNow;
        existing.LastSeenAtUtc = null;
        await db.SaveChangesAsync(cancellationToken);

        await audit.LogAsync(CurrentUserId, "REGISTER_POLICY_MACHINE", "PolicyMachine",
            request.MachineId,
            $"Tenant={request.Tenant}; Environment={request.Environment}; " +
            $"ClientCertRequired={thumbprint is not null}; CanaryGroup={existing.CanaryGroup ?? "none"}; " +
            $"ReRegistered={reRegistered}");
        return Ok(PolicyMachineDto.From(existing));
    }

    [HttpPost("machines/{machineId}/revoke")]
    public async Task<IActionResult> RevokeMachine(
        string machineId, [FromBody] PolicyMachineRevokeRequest request, CancellationToken cancellationToken)
    {
        var machine = await db.PolicyMachines
            .FirstOrDefaultAsync(m => m.MachineId == machineId, cancellationToken);
        if (machine is null)
            return NotFound(new { error = $"Machine '{machineId}' is not registered." });
        if (!machine.Revoked)
        {
            machine.Revoked = true;
            machine.RevokedAtUtc = DateTimeOffset.UtcNow;
            machine.RevokedReason = request.Reason;
            await db.SaveChangesAsync(cancellationToken);
        }

        await audit.LogAsync(CurrentUserId, "REVOKE_POLICY_MACHINE", "PolicyMachine", machineId,
            $"Tenant={machine.Tenant}; Environment={machine.Environment}; Reason={request.Reason ?? "none"}");
        return Ok(PolicyMachineDto.From(machine));
    }

    [HttpPost("rollback")]
    public async Task<IActionResult> Rollback(
        [FromBody] PolicyRollbackRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var version = await authority.RollbackToAsync(
                request.Tenant, request.Environment, request.TargetPolicyVersion,
                request.NewPolicyVersion, CurrentUserName, request.Reviewer,
                request.ExpiresAtUtc, cancellationToken);
            await audit.LogAsync(CurrentUserId, "ROLLBACK_ORG_POLICY", "OrganizationPolicy",
                $"{request.Tenant}/{request.Environment}",
                $"Target={request.TargetPolicyVersion}; RepublishedAs={version.PolicyVersion}; " +
                $"Hash={version.PolicyHash}");
            return Ok(PolicyVersionDto.From(version));
        }
        catch (Exception ex) when (ex is PolicyAuthorityException or ArgumentException)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ── Canary rollout ─────────────────────────────────────────────────────────
    // A canary is published alongside the active version and served only to its cohort. Promotion
    // makes it fleet-wide; halting rolls it back and re-issues the active document so the cohort reverts.

    [HttpGet("canary")]
    public async Task<IActionResult> GetCanary(
        [FromQuery] string tenant, [FromQuery] string environment, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(environment))
            return BadRequest(new { error = "tenant and environment are required." });
        var canary = await authority.GetCanaryVersionAsync(tenant, environment, cancellationToken);
        if (canary is null)
            return NotFound(new { error = $"No canary in progress for {tenant}/{environment}." });
        return Ok(PolicyVersionDto.From(canary));
    }

    [HttpPost("publish-canary")]
    public async Task<IActionResult> PublishCanary(
        [FromBody] PolicyCanaryPublishRequest request, CancellationToken cancellationToken)
    {
        OrganizationPolicyDocument document;
        try
        {
            document = OrganizationPolicySchema.ParseJson(request.PolicyJson);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return BadRequest(new { error = ex.Message });
        }

        CanaryCohort cohort;
        try
        {
            cohort = BuildCohort(request.CanaryGroup, request.CanaryPercentage);
        }
        catch (Exception ex) when (ex is PolicyAuthorityException or ArgumentException)
        {
            return BadRequest(new { error = ex.Message });
        }

        try
        {
            var version = await authority.PublishCanaryAsync(
                document, request.Tenant, request.Environment, request.PolicyVersion,
                CurrentUserName, request.Reviewer, request.ExpiresAtUtc, cohort, cancellationToken);
            await audit.LogAsync(CurrentUserId, "PUBLISH_CANARY_POLICY", "OrganizationPolicy",
                $"{request.Tenant}/{request.Environment}",
                $"Version={version.PolicyVersion}; Cohort={CohortLabel(cohort)}; Hash={version.PolicyHash}");
            return Ok(PolicyVersionDto.From(version));
        }
        catch (Exception ex) when (ex is PolicyAuthorityException or ArgumentException)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("promote-canary")]
    public async Task<IActionResult> PromoteCanary(
        [FromBody] PolicyCanaryPromoteRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var version = await authority.PromoteCanaryAsync(
                request.Tenant, request.Environment, request.PolicyVersion, cancellationToken);
            await audit.LogAsync(CurrentUserId, "PROMOTE_CANARY_POLICY", "OrganizationPolicy",
                $"{request.Tenant}/{request.Environment}",
                $"Version={version.PolicyVersion}; PromotedToFleetWide=true");
            return Ok(PolicyVersionDto.From(version));
        }
        catch (Exception ex) when (ex is PolicyAuthorityException or ArgumentException)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("halt-canary")]
    public async Task<IActionResult> HaltCanary(
        [FromBody] PolicyCanaryHaltRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var reissued = await authority.HaltCanaryAsync(
                request.Tenant, request.Environment, request.PolicyVersion,
                CurrentUserName, request.Reviewer, cancellationToken);
            await audit.LogAsync(CurrentUserId, "HALT_CANARY_POLICY", "OrganizationPolicy",
                $"{request.Tenant}/{request.Environment}",
                $"HaltedVersion={request.PolicyVersion}; ReissuedActive={reissued.PolicyVersion}");
            return Ok(PolicyVersionDto.From(reissued));
        }
        catch (Exception ex) when (ex is PolicyAuthorityException or ArgumentException)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Builds a cohort from the request's group/percentage, requiring exactly one.</summary>
    private static CanaryCohort BuildCohort(string? group, int? percentage)
    {
        var hasGroup = !string.IsNullOrWhiteSpace(group);
        if (hasGroup == percentage.HasValue)
            throw new PolicyAuthorityException("Specify exactly one of canary group or percentage.");
        var cohort = hasGroup ? CanaryCohort.ForGroup(group!) : CanaryCohort.ForPercentage(percentage!.Value);
        cohort.Validate();
        return cohort;
    }

    private static string CohortLabel(CanaryCohort cohort) =>
        cohort.Group is not null ? $"group:{cohort.Group}" : $"{cohort.Percentage}%";
}
