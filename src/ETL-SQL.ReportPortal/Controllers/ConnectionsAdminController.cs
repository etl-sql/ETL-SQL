using System.Security.Claims;
using ETL_SQL.Core.Governance;
using ETL_SQL.ReportPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETL_SQL.ReportPortal.Controllers;

/// <summary>
/// Administrative lifecycle for the Portal shared connection catalog (SHARED:alias). Entries hold
/// SECRET: references, never credential values; every mutation is audited; detail responses mask
/// any non-reference credential value.
/// </summary>
[ApiController]
[Route("api/admin/connections")]
[Authorize(Roles = "Admin")]
public class ConnectionsAdminController(
    PortalConnectionCatalogService catalog,
    AuditService audit,
    ISecretProvider secrets) : ControllerBase
{
    public sealed record SetConnectionRequest(
        string ConnectorType,
        string? Target,
        Dictionary<string, string>? Options,
        string? EnvironmentScope,
        List<string>? SensitiveFields = null);

    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await catalog.ListAsync(ct));

    [HttpGet("{alias}")]
    public async Task<IActionResult> Get(string alias, CancellationToken ct)
    {
        var detail = await catalog.GetDetailAsync(alias, ct);
        return detail == null
            ? NotFound(new { error = $"Shared connection '{alias}' does not exist." })
            : Ok(detail);
    }

    [HttpPut("{alias}")]
    public async Task<IActionResult> Set(string alias, [FromBody] SetConnectionRequest request, CancellationToken ct)
    {
        var entry = new PortalSharedConnectionExport(
            alias,
            request?.ConnectorType ?? "",
            request?.Target,
            new Dictionary<string, string>(request?.Options ?? [], StringComparer.OrdinalIgnoreCase),
            request?.EnvironmentScope,
            Disabled: false,
            request?.SensitiveFields);

        try
        {
            var existed = await catalog.GetStatusAsync(alias, ct) != SecretLifecycleStatus.NotFound;
            audit.Stage(CurrentUserId, existed ? "SHARED_CONNECTION_UPDATE" : "SHARED_CONNECTION_CREATE",
                "PortalSharedConnection", alias, $"ConnectorType={entry.ConnectorType.Trim()}");
            await catalog.StoreAsync(entry, CurrentUserId, ct);
            await catalog.SaveAsync(ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{alias}/verify")]
    public async Task<IActionResult> Verify(string alias, CancellationToken ct)
    {
        var status = await catalog.GetStatusAsync(alias, ct);
        if (status == SecretLifecycleStatus.NotFound)
            return NotFound(new { error = $"Shared connection '{alias}' does not exist." });

        string outcome;
        IActionResult result;
        if (status == SecretLifecycleStatus.Disabled)
        {
            outcome = "disabled";
            result = Conflict(new { alias, status = "disabled" });
        }
        else
        {
            try
            {
                var secretCount = await catalog.VerifySecretReferencesAsync(alias, secrets, ct);
                await catalog.SaveAsync(ct);
                outcome = "ok";
                result = Ok(new { alias, status = "ok", secretReferences = secretCount });
            }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
            {
                outcome = "unresolvable";
                result = Conflict(new { alias, status = "unresolvable", error = ex.Message });
            }
        }

        await audit.LogAsync(CurrentUserId, "SHARED_CONNECTION_VERIFY", "PortalSharedConnection", alias, $"Outcome={outcome}");
        return result;
    }

    [HttpPost("{alias}/disable")]
    public async Task<IActionResult> Disable(string alias, CancellationToken ct)
    {
        if (await catalog.GetStatusAsync(alias, ct) == SecretLifecycleStatus.NotFound)
            return NotFound(new { error = $"Shared connection '{alias}' does not exist." });

        audit.Stage(CurrentUserId, "SHARED_CONNECTION_DISABLE", "PortalSharedConnection", alias);
        await catalog.DisableAsync(alias, CurrentUserId, ct);
        await catalog.SaveAsync(ct);
        return NoContent();
    }

    /// <summary>Re-enables a disabled entry; the stored definition is retained.</summary>
    [HttpPost("{alias}/enable")]
    public async Task<IActionResult> Enable(string alias, CancellationToken ct)
    {
        if (await catalog.GetStatusAsync(alias, ct) == SecretLifecycleStatus.NotFound)
            return NotFound(new { error = $"Shared connection '{alias}' does not exist." });

        audit.Stage(CurrentUserId, "SHARED_CONNECTION_ENABLE", "PortalSharedConnection", alias);
        await catalog.EnableAsync(alias, CurrentUserId, ct);
        await catalog.SaveAsync(ct);
        return NoContent();
    }

    [HttpDelete("{alias}")]
    public async Task<IActionResult> Delete(string alias, CancellationToken ct)
    {
        if (await catalog.GetStatusAsync(alias, ct) == SecretLifecycleStatus.NotFound)
            return NotFound(new { error = $"Shared connection '{alias}' does not exist." });

        audit.Stage(CurrentUserId, "SHARED_CONNECTION_DELETE", "PortalSharedConnection", alias);
        await catalog.DeleteAsync(alias, ct);
        await catalog.SaveAsync(ct);
        return NoContent();
    }

    /// <summary>What breaks if this entry is disabled or deleted: referencing scripts + recorded consumers.</summary>
    [HttpGet("{alias}/impact")]
    public async Task<IActionResult> Impact(string alias, [FromServices] ReferenceImpactService impact, CancellationToken ct)
    {
        if (await catalog.GetStatusAsync(alias, ct) == SecretLifecycleStatus.NotFound)
            return NotFound(new { error = $"Shared connection '{alias}' does not exist." });

        return Ok(await impact.ForSharedConnectionAsync(alias, ct));
    }

    public sealed record GrantUseRequest(int GroupId);

    /// <summary>
    /// Use grants: an entry with no grants is usable by any caller; adding the first grant
    /// restricts use to admins, the owner, and members of granted groups.
    /// </summary>
    [HttpGet("{alias}/acl")]
    public async Task<IActionResult> ListAcl(string alias, CancellationToken ct)
    {
        if (await catalog.GetStatusAsync(alias, ct) == SecretLifecycleStatus.NotFound)
            return NotFound(new { error = $"Shared connection '{alias}' does not exist." });

        return Ok(await catalog.ListAclsAsync(alias, ct));
    }

    [HttpPost("{alias}/acl")]
    public async Task<IActionResult> GrantUse(string alias, [FromBody] GrantUseRequest request, CancellationToken ct)
    {
        if (await catalog.GetStatusAsync(alias, ct) == SecretLifecycleStatus.NotFound)
            return NotFound(new { error = $"Shared connection '{alias}' does not exist." });

        try
        {
            audit.Stage(CurrentUserId, "SHARED_CONNECTION_GRANT_USE", "PortalSharedConnection", alias,
                $"GroupId={request.GroupId}");
            var created = await catalog.GrantUseAsync(alias, request.GroupId, ct);
            await catalog.SaveAsync(ct);
            return created ? NoContent() : Ok(new { alreadyGranted = true });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpDelete("{alias}/acl/{groupId:int}")]
    public async Task<IActionResult> RevokeUse(string alias, int groupId, CancellationToken ct)
    {
        if (await catalog.GetStatusAsync(alias, ct) == SecretLifecycleStatus.NotFound)
            return NotFound(new { error = $"Shared connection '{alias}' does not exist." });

        audit.Stage(CurrentUserId, "SHARED_CONNECTION_REVOKE_USE", "PortalSharedConnection", alias,
            $"GroupId={groupId}");
        var removed = await catalog.RevokeUseAsync(alias, groupId, ct);
        await catalog.SaveAsync(ct);
        return removed
            ? NoContent()
            : NotFound(new { error = $"No use grant for group {groupId} on '{alias}'." });
    }

    /// <summary>Exports entry metadata (options hold SECRET: references only, never secret values).</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var entries = await catalog.ExportAsync(ct);
        await audit.LogAsync(CurrentUserId, "SHARED_CONNECTION_EXPORT", "PortalSharedConnection", null,
            $"Count={entries.Count}");
        return Ok(entries);
    }

    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] List<PortalSharedConnectionExport> entries, CancellationToken ct)
    {
        if (entries == null || entries.Count == 0)
            return BadRequest(new { error = "No entries to import." });

        var created = 0;
        var updated = 0;
        try
        {
            foreach (var entry in entries)
            {
                audit.Stage(CurrentUserId, "SHARED_CONNECTION_IMPORT", "PortalSharedConnection", entry.Alias,
                    $"ConnectorType={entry.ConnectorType?.Trim()}");
                if (await catalog.StoreAsync(entry, CurrentUserId, ct)) updated++; else created++;
            }

            await catalog.SaveAsync(ct);
            return Ok(new { created, updated });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
