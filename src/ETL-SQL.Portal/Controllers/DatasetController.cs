using System.Security.Claims;
using System.Text.Json;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Filters;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETL_SQL.Portal.Controllers;

[ApiController]
[Route("api/datasets")]
[Authorize]
[RequirePortalModule("Reporting")]
public class DatasetController(
    PortalDbContext db,
    DatasetViewerService viewer,
    DatasetPermissionService datasetPermissions,
    DatasetQueryService queries,
    DatasetExportService exports,
    DatasetRefreshService refreshes,
    DatasetMoveService moves,
    DatasetAclService acls,
    DatasetUpdateService updates,
    DatasetDeleteService deletes) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool IsAdmin => User.IsInRole("Admin");

    // ── Permission helpers ────────────────────────────────────────────────────

    private Task<DatasetPermission?> GetEffectivePermissionAsync(Dataset dataset) =>
        datasetPermissions.GetEffectivePermissionAsync(dataset, CurrentUserId, IsAdmin);

    private static bool CanView(DatasetPermission? p) => DatasetPermissionService.CanView(p);
    private static bool CanRefresh(DatasetPermission? p) => DatasetPermissionService.CanRefresh(p);
    private static bool CanEdit(DatasetPermission? p) => DatasetPermissionService.CanEdit(p);
    private static bool CanManage(DatasetPermission? p) => DatasetPermissionService.CanManage(p);

    // ── GET /api/datasets ─────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        return Ok(await queries.GetAllAsync(CurrentUserId, IsAdmin));
    }

    // ── GET /api/datasets/{id} ────────────────────────────────────────────────

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var dataset = await queries.LoadDatasetAsync(id);
        if (dataset is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(dataset);
        if (!CanView(perm)) return Forbid();

        OptimisticConcurrency.SetETag(Response, dataset.Version);
        return Ok(queries.ToDto(dataset));
    }

    // ── GET /api/datasets/{id}/data ──────────────────────────────────────────

    [HttpGet("{id:int}/data")]
    public async Task<IActionResult> GetData(
        int id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? sort = null,
        [FromQuery] string? dir = null,
        [FromQuery] string? search = null,
        [FromQuery] string? filters = null)
    {
        var dataset = await queries.LoadDatasetAsync(id);
        if (dataset is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(dataset);
        if (!CanView(perm)) return Forbid();

        var filterList = ParseFilters(filters);

        try
        {
            if (dataset.RowCount > 500_000)
                Response.Headers.Append("X-Dataset-Large", "true");

            var result = await viewer.QueryAsync(id, page, pageSize, sort, dir, search, filterList);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ── GET /api/datasets/{id}/data/export ───────────────────────────────────

    [HttpGet("{id:int}/data/export")]
    public async Task<IActionResult> ExportData(
        int id,
        [FromQuery] string? sort = null,
        [FromQuery] string? dir = null,
        [FromQuery] string? search = null,
        [FromQuery] string? filters = null,
        [FromQuery] string? format = null)
    {
        var dataset = await queries.LoadDatasetAsync(id);
        if (dataset is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(dataset);
        if (!CanView(perm)) return Forbid();

        var filterList = ParseFilters(filters);

        try
        {
            var export = await exports.PrepareAsync(dataset, sort, dir, search, filterList, format);
            return File(export.Stream, export.ContentType, export.FileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ── GET /api/datasets/{id}/data/stats ────────────────────────────────────

    [HttpGet("{id:int}/data/stats")]
    public async Task<IActionResult> GetStats(int id, [FromQuery] string? filters = null)
    {
        var dataset = await queries.LoadDatasetAsync(id);
        if (dataset is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(dataset);
        if (!CanView(perm)) return Forbid();

        try
        {
            var result = await viewer.GetStatsAsync(id, ParseFilters(filters));
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ── GET /api/datasets/{id}/column/{colName}/values ───────────────────────

    [HttpGet("{id:int}/column/{colName}/values")]
    public async Task<IActionResult> GetColumnValues(
        int id, string colName,
        [FromQuery] string? search = null,
        [FromQuery] int limit = 50)
    {
        var dataset = await queries.LoadDatasetAsync(id);
        if (dataset is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(dataset);
        if (!CanView(perm)) return Forbid();

        try
        {
            var result = await viewer.GetColumnValuesAsync(id, colName, search, limit);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ── GET /api/datasets/{id}/rows ───────────────────────────────────────────

    [HttpGet("{id:int}/rows")]
    public async Task<IActionResult> GetRows(int id)
    {
        var dataset = await queries.LoadDatasetAsync(id);
        if (dataset is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(dataset);
        if (!CanView(perm)) return Forbid();

        return Ok(queries.ToPreview(dataset));
    }

    // ── POST /api/datasets/{id}/refresh ──────────────────────────────────────

    [HttpPost("{id:int}/refresh")]
    public async Task<IActionResult> Refresh(int id)
    {
        var dataset = await queries.LoadDatasetAsync(id);
        if (dataset is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(dataset);
        if (!CanRefresh(perm)) return Forbid();

        var result = await refreshes.RefreshAsync(dataset, User, CurrentUserId, IsAdmin, HttpContext.TraceIdentifier);
        if (result.Kind == DatasetRefreshResultKind.Conflict)
        {
            Response.Headers.Append("Location", $"/api/jobs/{result.JobId}");
            return Conflict(new { error = result.Message, jobId = result.JobId });
        }

        if (result.Kind == DatasetRefreshResultKind.Queued)
        {
            Response.Headers.Append("Location", $"/api/jobs/{result.JobId}");
            return Accepted(new { triggered = true, jobId = result.JobId });
        }

        return Accepted(new { triggered = false, message = result.Message });
    }

    // ── GET /api/datasets/{id}/refresh-status ────────────────────────────────

    [HttpGet("{id:int}/refresh-status")]
    public async Task<IActionResult> GetRefreshStatus(int id)
    {
        var dataset = await queries.LoadDatasetAsync(id);
        if (dataset is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(dataset);
        if (!CanView(perm)) return Forbid();

        return Ok(await refreshes.GetStatusAsync(dataset));
    }

    // ── PATCH /api/datasets/{id} ──────────────────────────────────────────────

    [HttpPatch("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateDatasetRequest req)
    {
        var dataset = await queries.LoadDatasetAsync(id);
        if (dataset is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(dataset);
        if (!CanEdit(perm)) return Forbid();
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null)
            return OptimisticConcurrency.MissingVersion(this);

        var result = await updates.UpdateAsync(dataset, req, expectedVersion.Value, CurrentUserId, CanManage(perm));
        if (result.Kind == DatasetUpdateResultKind.InvalidAccessLevel)
            return BadRequest(new { error = "accessLevel must be 'Public' or 'Private'." });
        if (result.Kind == DatasetUpdateResultKind.Forbidden)
            return Forbid();
        if (result.Kind == DatasetUpdateResultKind.Conflict)
            return OptimisticConcurrency.Conflict(this, queries.ToDto(dataset));

        OptimisticConcurrency.SetETag(Response, dataset.Version);
        return Ok(queries.ToDto(dataset));
    }

    // ── POST /api/datasets/{id}/move ─────────────────────────────────────────

    [HttpPost("{id:int}/move")]
    public async Task<IActionResult> Move(int id, [FromBody] MoveDatasetRequest req)
    {
        var dataset = await queries.LoadDatasetAsync(id);
        if (dataset is null) return NotFound();

        var destination = await db.Folders.FindAsync(req.DestinationFolderId);
        if (destination is null)
            return NotFound(new { error = "Destination folder not found." });

        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null)
            return OptimisticConcurrency.MissingVersion(this);

        var result = await moves.MoveAsync(dataset, destination, expectedVersion.Value, User, CurrentUserId, IsAdmin);
        if (result.Kind == DatasetMoveResultKind.Forbidden)
        {
            return Forbid();
        }
        if (result.Kind == DatasetMoveResultKind.Conflict)
        {
            return OptimisticConcurrency.Conflict(this, queries.ToDto(dataset));
        }

        OptimisticConcurrency.SetETag(Response, dataset.Version);
        return Ok(queries.ToDto(dataset));
    }

    // ── DELETE /api/datasets/{id} ─────────────────────────────────────────────

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var dataset = await queries.LoadDatasetAsync(id);
        if (dataset is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(dataset);
        if (!CanManage(perm)) return Forbid();

        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null)
            return OptimisticConcurrency.MissingVersion(this);

        var result = await deletes.DeleteAsync(dataset, expectedVersion.Value, CurrentUserId);
        if (result.Kind == DatasetDeleteResultKind.NotFound)
            return NotFound();
        if (result.Kind == DatasetDeleteResultKind.Conflict)
            return OptimisticConcurrency.Conflict(this, queries.ToDto(result.Current!));

        return NoContent();
    }

    // ── GET /api/datasets/{id}/acl ────────────────────────────────────────────

    [HttpGet("{id:int}/acl")]
    public async Task<IActionResult> GetAcl(int id)
    {
        var dataset = await queries.LoadDatasetWithAclGroupsAsync(id);
        if (dataset is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(dataset);
        if (!CanView(perm)) return Forbid();

        return Ok(queries.ToAclEntries(dataset));
    }

    // ── POST /api/datasets/{id}/acl ───────────────────────────────────────────

    [HttpPost("{id:int}/acl")]
    public async Task<IActionResult> GrantPermission(int id, [FromBody] GrantDatasetPermissionRequest req)
    {
        var dataset = await queries.LoadDatasetAsync(id);
        if (dataset is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(dataset);
        if (!CanManage(perm)) return Forbid();
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null)
            return OptimisticConcurrency.MissingVersion(this);

        var result = await acls.GrantAsync(dataset, req.GroupId, req.Permission, expectedVersion.Value, CurrentUserId);
        if (result.Kind == DatasetAclMutationResultKind.InvalidPermission)
            return BadRequest(new { error = "permission must be 'Viewer', 'Refresh', 'Editor', or 'Owner'." });
        if (result.Kind == DatasetAclMutationResultKind.GroupNotFound)
            return NotFound(new { error = "Group not found." });
        if (result.Kind == DatasetAclMutationResultKind.Conflict)
            return OptimisticConcurrency.Conflict(this, queries.ToDto(dataset));

        OptimisticConcurrency.SetETag(Response, dataset.Version);
        return Ok(new { dataset.Version });
    }

    // ── DELETE /api/datasets/{id}/acl/{groupId} ───────────────────────────────

    [HttpDelete("{id:int}/acl/{groupId:int}")]
    public async Task<IActionResult> RevokePermission(int id, int groupId)
    {
        var dataset = await queries.LoadDatasetAsync(id);
        if (dataset is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(dataset);
        if (!CanManage(perm)) return Forbid();
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null)
            return OptimisticConcurrency.MissingVersion(this);

        var result = await acls.RevokeAsync(dataset, groupId, expectedVersion.Value, CurrentUserId);
        if (result.Kind == DatasetAclMutationResultKind.AclNotFound)
            return NotFound(new { error = "ACL entry not found." });
        if (result.Kind == DatasetAclMutationResultKind.Conflict)
            return OptimisticConcurrency.Conflict(this, queries.ToDto(dataset));

        OptimisticConcurrency.SetETag(Response, dataset.Version);
        return Ok(new { dataset.Version });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IEnumerable<DatasetColumnFilterDto> ParseFilters(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<DatasetColumnFilterDto[]>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? []; }
        catch { return []; }
    }

}
