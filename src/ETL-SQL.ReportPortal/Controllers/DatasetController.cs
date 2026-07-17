using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using ETL_SQL.Core.Data;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Filters;
using ETL_SQL.ReportPortal.Models;
using ETL_SQL.ReportPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Controllers;

[ApiController]
[Route("api/datasets")]
[Authorize]
[RequirePortalModule("Reporting")]
public class DatasetController(
    PortalDbContext db,
    IDatasetRegistry registry,
    AuditService audit,
    DatasetViewerService viewer,
    DatasetPermissionService datasetPermissions,
    SecuritySessionService securitySessions,
    DatasetExportService exports,
    DatasetRefreshService refreshes,
    DatasetMoveService moves) : ControllerBase
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
        var datasets = await db.Datasets
            .AsNoTracking()
            .Include(d => d.OwningReport)
            .Include(d => d.Acls)
            .ToListAsync();

        var permissions = await datasetPermissions.GetEffectivePermissionsAsync(
            datasets, CurrentUserId, IsAdmin);

        return Ok(datasets
            .Where(d => CanView(permissions[d.Id]))
            .Select(ToDto)
            .ToList());
    }

    // ── GET /api/datasets/{id} ────────────────────────────────────────────────

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var dataset = await LoadDataset(id);
        if (dataset is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(dataset);
        if (!CanView(perm)) return Forbid();

        OptimisticConcurrency.SetETag(Response, dataset.Version);
        return Ok(ToDto(dataset));
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
        var dataset = await LoadDataset(id);
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
        var dataset = await LoadDataset(id);
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
        var dataset = await LoadDataset(id);
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
        var dataset = await LoadDataset(id);
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
        var dataset = await LoadDataset(id);
        if (dataset is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(dataset);
        if (!CanView(perm)) return Forbid();

        return Ok(new DatasetPreviewDto(ParseColumnSchema(dataset.ColumnSchema), dataset.RowCount));
    }

    // ── POST /api/datasets/{id}/refresh ──────────────────────────────────────

    [HttpPost("{id:int}/refresh")]
    public async Task<IActionResult> Refresh(int id)
    {
        var dataset = await LoadDataset(id);
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
        var dataset = await LoadDataset(id);
        if (dataset is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(dataset);
        if (!CanView(perm)) return Forbid();

        return Ok(await refreshes.GetStatusAsync(dataset));
    }

    // ── PATCH /api/datasets/{id} ──────────────────────────────────────────────

    [HttpPatch("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateDatasetRequest req)
    {
        var dataset = await LoadDataset(id);
        if (dataset is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(dataset);
        if (!CanEdit(perm)) return Forbid();
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null)
            return OptimisticConcurrency.MissingVersion(this);
        if (!OptimisticConcurrency.Prepare(db, dataset, expectedVersion.Value))
            return OptimisticConcurrency.Conflict(this, ToDto(dataset));

        if (req.AccessLevel is not null)
        {
            if (!Enum.TryParse<DatasetAccessLevel>(req.AccessLevel, ignoreCase: true, out var level))
                return BadRequest(new { error = "accessLevel must be 'Public' or 'Private'." });

            // Changing exposure (Private→Public widens access to every folder reader) is the
            // same class of operation as ACL grant/revoke and requires Manage, not Edit.
            if (level != dataset.AccessLevel && !CanManage(perm))
                return Forbid();

            dataset.AccessLevel = level;
        }

        if (req.Ttl is not null)
            dataset.Ttl = string.IsNullOrWhiteSpace(req.Ttl) ? null : req.Ttl;

        dataset.UpdatedAt = DateTime.UtcNow;
        // Staged so the mutation and its audit row share one commit (P1.6).
        audit.Stage(CurrentUserId, "UPDATE_DATASET", "Dataset", id.ToString(), dataset.Name);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await db.Entry(dataset).ReloadAsync();
            return OptimisticConcurrency.Conflict(this, ToDto(dataset));
        }

        OptimisticConcurrency.SetETag(Response, dataset.Version);
        return Ok(ToDto(dataset));
    }

    // ── POST /api/datasets/{id}/move ─────────────────────────────────────────

    [HttpPost("{id:int}/move")]
    public async Task<IActionResult> Move(int id, [FromBody] MoveDatasetRequest req)
    {
        var dataset = await LoadDataset(id);
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
            return OptimisticConcurrency.Conflict(this, ToDto(dataset));
        }

        OptimisticConcurrency.SetETag(Response, dataset.Version);
        return Ok(ToDto(dataset));
    }

    // ── DELETE /api/datasets/{id} ─────────────────────────────────────────────

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var dataset = await LoadDataset(id);
        if (dataset is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(dataset);
        if (!CanManage(perm)) return Forbid();

        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null)
            return OptimisticConcurrency.MissingVersion(this);
        if (!OptimisticConcurrency.Prepare(db, dataset, expectedVersion.Value))
            return OptimisticConcurrency.Conflict(this, ToDto(dataset));

        // Staged on the shared scoped context, so it commits inside the registry's delete save.
        audit.Stage(CurrentUserId, "DELETE_DATASET", "Dataset", id.ToString(), dataset.Name);
        try
        {
            await registry.Delete(dataset.Name);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var current = await LoadDataset(id);
            return current is null ? NotFound() : OptimisticConcurrency.Conflict(this, ToDto(current));
        }

        return NoContent();
    }

    // ── GET /api/datasets/{id}/acl ────────────────────────────────────────────

    [HttpGet("{id:int}/acl")]
    public async Task<IActionResult> GetAcl(int id)
    {
        var dataset = await db.Datasets
            .AsNoTracking()
            .Include(d => d.OwningReport)
            .Include(d => d.Acls).ThenInclude(a => a.Group)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (dataset is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(dataset);
        if (!CanView(perm)) return Forbid();

        return Ok(dataset.Acls.Select(a =>
            new DatasetAclEntryDto(a.GroupId, a.Group.Name, a.Permission.ToString())));
    }

    // ── POST /api/datasets/{id}/acl ───────────────────────────────────────────

    [HttpPost("{id:int}/acl")]
    public async Task<IActionResult> GrantPermission(int id, [FromBody] GrantDatasetPermissionRequest req)
    {
        var dataset = await LoadDataset(id);
        if (dataset is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(dataset);
        if (!CanManage(perm)) return Forbid();
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null)
            return OptimisticConcurrency.MissingVersion(this);
        if (!OptimisticConcurrency.Prepare(db, dataset, expectedVersion.Value))
            return OptimisticConcurrency.Conflict(this, ToDto(dataset));

        if (!Enum.TryParse<DatasetPermission>(req.Permission, ignoreCase: true, out var granted))
            return BadRequest(new { error = "permission must be 'Viewer', 'Refresh', 'Editor', or 'Owner'." });

        if (!await db.Groups.AnyAsync(g => g.Id == req.GroupId))
            return NotFound(new { error = "Group not found." });

        var existing = await db.DatasetAcls
            .FirstOrDefaultAsync(a => a.DatasetId == id && a.GroupId == req.GroupId);

        if (existing is null)
            db.DatasetAcls.Add(new DatasetAcl { DatasetId = id, GroupId = req.GroupId, Permission = granted });
        else
            existing.Permission = granted;

        audit.Stage(CurrentUserId, "GRANT_DATASET_PERMISSION", "Dataset", id.ToString(),
            $"{dataset.Name} → group {req.GroupId} as {granted}");
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await db.Entry(dataset).ReloadAsync();
            return OptimisticConcurrency.Conflict(this, ToDto(dataset));
        }
        await securitySessions.InvalidateGroupMembersAsync(req.GroupId);

        OptimisticConcurrency.SetETag(Response, dataset.Version);
        return Ok(new { dataset.Version });
    }

    // ── DELETE /api/datasets/{id}/acl/{groupId} ───────────────────────────────

    [HttpDelete("{id:int}/acl/{groupId:int}")]
    public async Task<IActionResult> RevokePermission(int id, int groupId)
    {
        var dataset = await LoadDataset(id);
        if (dataset is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(dataset);
        if (!CanManage(perm)) return Forbid();
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null)
            return OptimisticConcurrency.MissingVersion(this);
        if (!OptimisticConcurrency.Prepare(db, dataset, expectedVersion.Value))
            return OptimisticConcurrency.Conflict(this, ToDto(dataset));

        var acl = await db.DatasetAcls
            .FirstOrDefaultAsync(a => a.DatasetId == id && a.GroupId == groupId);
        if (acl is null) return NotFound(new { error = "ACL entry not found." });

        db.DatasetAcls.Remove(acl);
        audit.Stage(CurrentUserId, "REVOKE_DATASET_PERMISSION", "Dataset", id.ToString(),
            $"{dataset.Name} → group {groupId}");
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await db.Entry(dataset).ReloadAsync();
            return OptimisticConcurrency.Conflict(this, ToDto(dataset));
        }
        await securitySessions.InvalidateGroupMembersAsync(groupId);

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

    private Task<Dataset?> LoadDataset(int id) =>
        db.Datasets
            .Include(d => d.OwningReport)
            .Include(d => d.Acls)
            .FirstOrDefaultAsync(d => d.Id == id);

    private static DatasetDto ToDto(Dataset d) => new(
        d.Id, d.Name, d.FolderPath,
        d.AccessLevel.ToString(),
        d.RowCount, IsStale(d),
        d.LastRefresh, d.Ttl, d.RefreshInterval,
        IsEncrypted: !string.IsNullOrWhiteSpace(d.ParquetFilePath)
            && d.EncryptionMode != ETL_SQL.Core.DatasetEncryptionMode.None,
        d.CreatedAt, d.UpdatedAt,
        d.OwningReport?.Name,
        d.OwningReportId,
        d.Version);

    private static bool IsStale(Dataset d)
    {
        if (!d.LastRefresh.HasValue) return true;
        if (string.IsNullOrWhiteSpace(d.Ttl)) return false;
        var ttl = ParseDuration(d.Ttl);
        return ttl.HasValue && d.LastRefresh.Value + ttl.Value < DateTime.UtcNow;
    }

    private static TimeSpan? ParseDuration(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = Regex.Match(s.Trim(), @"^(\d+)([smhd])$", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        int v = int.Parse(m.Groups[1].Value);
        return m.Groups[2].Value.ToUpperInvariant() switch
        {
            "S" => TimeSpan.FromSeconds(v),
            "M" => TimeSpan.FromMinutes(v),
            "H" => TimeSpan.FromHours(v),
            "D" => TimeSpan.FromDays(v),
            _ => null
        };
    }

    private static IEnumerable<DatasetColumnDto> ParseColumnSchema(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            return doc.RootElement.EnumerateArray()
                .Select(e => new DatasetColumnDto(
                    e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    e.TryGetProperty("type", out var t) ? t.GetString() ?? "unknown" : "unknown"))
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .ToList();
        }
        catch { return []; }
    }
}
