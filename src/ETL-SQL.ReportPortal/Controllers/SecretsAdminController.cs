using System.Security.Claims;
using ETL_SQL.Core.Governance;
using ETL_SQL.ReportPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETL_SQL.ReportPortal.Controllers;

/// <summary>
/// Administrative lifecycle for the Portal-managed encrypted secret store. Secret values are
/// accepted on write and never returned by any endpoint; every mutation is audited.
/// </summary>
[ApiController]
[Route("api/admin/secrets")]
[Authorize(Roles = "Admin")]
public class SecretsAdminController(PortalSecretStoreService store, AuditService audit) : ControllerBase
{
    public sealed record SetSecretRequest(string Value);

    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await store.ListAsync(ct));

    [HttpPut("{name}")]
    public async Task<IActionResult> Set(string name, [FromBody] SetSecretRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request?.Value))
            return BadRequest(new { error = "A non-empty secret value is required." });

        try
        {
            SecretNameValidator.Validate(name);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var existed = await store.GetStatusAsync(name, ct) != SecretLifecycleStatus.NotFound;
        // Staged rows commit atomically with the store's own SaveChanges.
        audit.Stage(CurrentUserId, existed ? "SECRET_ROTATE" : "SECRET_SET", "PortalSecret", name);
        await store.StoreAsync(name, request.Value, CurrentUserId, ct);
        return NoContent();
    }

    [HttpPost("{name}/verify")]
    public async Task<IActionResult> Verify(string name, CancellationToken ct)
    {
        var status = await store.GetStatusAsync(name, ct);
        if (status == SecretLifecycleStatus.NotFound)
            return NotFound(new { error = $"Secret '{name}' does not exist." });

        string outcome;
        IActionResult result;
        if (status == SecretLifecycleStatus.Disabled)
        {
            outcome = "disabled";
            result = Conflict(new { name, status = "disabled" });
        }
        else
        {
            try
            {
                await store.VerifyAsync(name, ct);
                outcome = "ok";
                result = Ok(new { name, status = "ok", provider = "PortalStore" });
            }
            catch (InvalidOperationException ex)
            {
                outcome = "undecryptable";
                result = Conflict(new { name, status = "undecryptable", error = ex.Message });
            }
        }

        await audit.LogAsync(CurrentUserId, "SECRET_VERIFY", "PortalSecret", name, $"Outcome={outcome}");
        return result;
    }

    /// <summary>Decrypt-probe of every stored secret — the backup/restore and HA key validation surface.</summary>
    [HttpPost("verify-all")]
    public async Task<IActionResult> VerifyAll(CancellationToken ct)
    {
        var result = await store.CheckKeyRingAsync(ct);
        await audit.LogAsync(CurrentUserId, "SECRET_VERIFY_ALL", "PortalSecret", null,
            $"SecretCount={result.SecretCount}; FailedCount={result.FailedCount}; FirstFailed={result.FirstFailedName}");
        return Ok(result);
    }

    [HttpPost("{name}/disable")]
    public async Task<IActionResult> Disable(string name, CancellationToken ct)
    {
        if (await store.GetStatusAsync(name, ct) == SecretLifecycleStatus.NotFound)
            return NotFound(new { error = $"Secret '{name}' does not exist." });

        audit.Stage(CurrentUserId, "SECRET_DISABLE", "PortalSecret", name);
        await store.DisableAsync(name, CurrentUserId, ct);
        return NoContent();
    }

    /// <summary>Re-enables a disabled secret; the stored value is retained, no new value required.</summary>
    [HttpPost("{name}/enable")]
    public async Task<IActionResult> Enable(string name, CancellationToken ct)
    {
        if (await store.GetStatusAsync(name, ct) == SecretLifecycleStatus.NotFound)
            return NotFound(new { error = $"Secret '{name}' does not exist." });

        audit.Stage(CurrentUserId, "SECRET_ENABLE", "PortalSecret", name);
        await store.EnableAsync(name, CurrentUserId, ct);
        return NoContent();
    }

    [HttpDelete("{name}")]
    public async Task<IActionResult> Delete(string name, CancellationToken ct)
    {
        if (await store.GetStatusAsync(name, ct) == SecretLifecycleStatus.NotFound)
            return NotFound(new { error = $"Secret '{name}' does not exist." });

        audit.Stage(CurrentUserId, "SECRET_DELETE", "PortalSecret", name);
        await store.DeleteAsync(name, ct);
        return NoContent();
    }
}
