using System.Security.Claims;
using System.Text.Json;
using ETL_SQL.Core.Governance;
using ETL_SQL.ReportPortal.Models;
using ETL_SQL.ReportPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETL_SQL.ReportPortal.Controllers;

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
    AuditService audit) : ControllerBase
{
    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private string CurrentUserName => User.Identity?.Name ?? $"user:{CurrentUserId}";

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
            var version = await authority.PublishAsync(
                document, request.Tenant, request.Environment, request.PolicyVersion,
                CurrentUserName, request.Reviewer, request.ExpiresAtUtc, request.Staged,
                cancellationToken);
            await audit.LogAsync(CurrentUserId, "PUBLISH_ORG_POLICY", "OrganizationPolicy",
                $"{request.Tenant}/{request.Environment}",
                $"Version={version.PolicyVersion}; Staged={request.Staged}; " +
                $"Hash={version.PolicyHash}; Superseded={version.SupersededVersion ?? "none"}");
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
}
