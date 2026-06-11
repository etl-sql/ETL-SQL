using System.IO.Pipelines;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ETL_SQL.Core.Data;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Models;
using ETL_SQL.ReportPortal.Services;

namespace ETL_SQL.ReportPortal.Controllers;

[ApiController]
[Route("api/datasets")]
[Authorize]
public class DatasetController(
    PortalDbContext     db,
    IDatasetRegistry    registry,
    AuditService        audit,
    ExecutionJobService jobService,
    DatasetViewerService viewer,
    DatasetPermissionService datasetPermissions,
    FolderPermissionService folderPermissions,
    SessionCache sessions) : ControllerBase
{
    private int  CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool IsAdmin       => User.IsInRole("Admin");

    // ── Permission helpers ────────────────────────────────────────────────────

    private Task<DatasetPermission?> GetEffectivePermissionAsync(Dataset dataset) =>
        datasetPermissions.GetEffectivePermissionAsync(dataset, CurrentUserId, IsAdmin);

    private static bool CanView(DatasetPermission? p)   => DatasetPermissionService.CanView(p);
    private static bool CanRefresh(DatasetPermission? p) => DatasetPermissionService.CanRefresh(p);
    private static bool CanEdit(DatasetPermission? p)   => DatasetPermissionService.CanEdit(p);
    private static bool CanManage(DatasetPermission? p) => DatasetPermissionService.CanManage(p);

    // ── GET /api/datasets ─────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var datasets = await db.Datasets
            .Include(d => d.OwningReport)
            .Include(d => d.Acls)
            .ToListAsync();

        var groupIds = IsAdmin
            ? new HashSet<int>()
            : await datasetPermissions.GetUserGroupIdsAsync(CurrentUserId);
        var result = new List<DatasetDto>();
        foreach (var dataset in datasets)
        {
            var permission = await datasetPermissions.GetEffectivePermissionAsync(
                dataset,
                CurrentUserId,
                IsAdmin,
                groupIds);
            if (CanView(permission))
                result.Add(ToDto(dataset));
        }

        return Ok(result);
    }

    // ── GET /api/datasets/{id} ────────────────────────────────────────────────

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var dataset = await LoadDataset(id);
        if (dataset is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(dataset);
        if (!CanView(perm)) return Forbid();

        return Ok(ToDto(dataset));
    }

    // ── GET /api/datasets/{id}/data ──────────────────────────────────────────

    [HttpGet("{id:int}/data")]
    public async Task<IActionResult> GetData(
        int id,
        [FromQuery] int    page     = 1,
        [FromQuery] int    pageSize = 50,
        [FromQuery] string? sort   = null,
        [FromQuery] string? dir    = null,
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
        [FromQuery] string? sort    = null,
        [FromQuery] string? dir     = null,
        [FromQuery] string? search  = null,
        [FromQuery] string? filters = null,
        [FromQuery] string? format  = null)
    {
        var dataset = await LoadDataset(id);
        if (dataset is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(dataset);
        if (!CanView(perm)) return Forbid();

        var filterList = ParseFilters(filters);
        var safeName   = string.Concat(dataset.Name.Where(c => char.IsLetterOrDigit(c) || c == '_'));

        try
        {
            var (filteredRows, columns) = await viewer.PrepareExportAsync(id, sort, dir, search, filterList);

            if (string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase))
            {
                var pipe = new Pipe();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await using var stream = pipe.Writer.AsStream();
                        await viewer.ExportXlsxAsync(columns, filteredRows, stream, dataset.Name);
                        await pipe.Writer.CompleteAsync();
                    }
                    catch (Exception ex)
                    {
                        await pipe.Writer.CompleteAsync(ex);
                    }
                });

                return File(pipe.Reader.AsStream(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"{safeName}.xlsx");
            }

            var csvPipe = new Pipe();
            _ = Task.Run(async () =>
            {
                try
                {
                    await using var stream = csvPipe.Writer.AsStream();
                    await viewer.ExportCsvAsync(columns, filteredRows, stream);
                    await csvPipe.Writer.CompleteAsync();
                }
                catch (Exception ex)
                {
                    await csvPipe.Writer.CompleteAsync(ex);
                }
            });

            return File(csvPipe.Reader.AsStream(),
                "text/csv; charset=utf-8",
                $"{safeName}.csv");
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
        [FromQuery] int     limit  = 50)
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

        // Concurrent refresh guard: reject if the owning report is already executing
        if (dataset.OwningReportId.HasValue)
        {
            var existingJobId = jobService.GetActiveRefreshJobId(dataset.OwningReportId.Value);
            if (existingJobId is not null)
            {
                Response.Headers.Append("Location", $"/api/jobs/{existingJobId}");
                return Conflict(new { error = "Refresh already in progress.", jobId = existingJobId });
            }
        }

        // Mark stale so the engine re-materialises on next CREATE DATASET hit
        await registry.SetStale(dataset.Name);

        // If we have an owning report, queue a real engine refresh.
        // Path security is enforced inside RunJobAsync — no need to pre-validate here.
        if (dataset.OwningReport is not null)
        {
            var jobId = jobService.EnqueueRefresh(dataset.OwningReport.Id, CurrentUserId, dataset.OwningReport.ScriptPath);
            await audit.LogAsync(CurrentUserId, "REFRESH_DATASET", "Dataset", id.ToString(), dataset.Name);
            Response.Headers.Append("Location", $"/api/jobs/{jobId}");
            return Accepted(new { triggered = true, jobId });
        }

        // No owning report — dataset was created outside the Portal (e.g. ad-hoc CLI run).
        // Mark stale so it re-materialises when the producing script next executes.
        await audit.LogAsync(CurrentUserId, "REFRESH_DATASET", "Dataset", id.ToString(), dataset.Name + " (stale-only)");
        return Accepted(new { triggered = false, message = $"Dataset '{dataset.Name}' has no linked report. Run the script that produced it to refresh the data." });
    }

    // ── GET /api/datasets/{id}/refresh-status ────────────────────────────────

    [HttpGet("{id:int}/refresh-status")]
    public async Task<IActionResult> GetRefreshStatus(int id)
    {
        var dataset = await LoadDataset(id);
        if (dataset is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(dataset);
        if (!CanView(perm)) return Forbid();

        if (dataset.OwningReportId.HasValue)
        {
            var jobId = jobService.GetActiveRefreshJobId(dataset.OwningReportId.Value);
            if (jobId is not null)
            {
                var job = jobService.Get(jobId);
                return Ok(new DatasetRefreshStatusDto(
                    "InProgress", jobId,
                    job?.StartedAt, null, null,
                    dataset.LastRefresh, IsStale(dataset)));
            }
        }

        return Ok(new DatasetRefreshStatusDto(
            "Idle", null, null, null, null,
            dataset.LastRefresh, IsStale(dataset)));
    }

    // ── PATCH /api/datasets/{id} ──────────────────────────────────────────────

    [HttpPatch("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateDatasetRequest req)
    {
        var dataset = await LoadDataset(id);
        if (dataset is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(dataset);
        if (!CanEdit(perm)) return Forbid();

        if (req.AccessLevel is not null)
        {
            if (!Enum.TryParse<DatasetAccessLevel>(req.AccessLevel, ignoreCase: true, out var level))
                return BadRequest(new { error = "accessLevel must be 'Public' or 'Private'." });
            dataset.AccessLevel = level;
        }

        if (req.Ttl is not null)
            dataset.Ttl = string.IsNullOrWhiteSpace(req.Ttl) ? null : req.Ttl;

        dataset.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "UPDATE_DATASET", "Dataset", id.ToString(), dataset.Name);

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

        if (dataset.FolderId == destination.Id)
            return Ok(ToDto(dataset));

        if (dataset.FolderId is int sourceFolderId)
        {
            if (!await folderPermissions.HasPermissionAsync(
                    sourceFolderId,
                    FolderPermission.Manage,
                    User))
            {
                return Forbid();
            }
        }
        else if (!IsAdmin)
        {
            return Forbid();
        }

        if (!await folderPermissions.HasPermissionAsync(
                destination.Id,
                FolderPermission.Manage,
                User))
        {
            return Forbid();
        }

        var sourcePath = dataset.FolderPath;
        dataset.FolderId = destination.Id;
        dataset.FolderPath = destination.Path;
        dataset.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        if (dataset.OwningReportId is int reportId)
            await sessions.InvalidateReportAsync(reportId);

        await audit.LogAsync(
            CurrentUserId,
            "MOVE_DATASET",
            "Dataset",
            id.ToString(),
            $"{dataset.Name}: {sourcePath} -> {destination.Path}");

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

        await registry.Delete(dataset.Name);
        await audit.LogAsync(CurrentUserId, "DELETE_DATASET", "Dataset", id.ToString(), dataset.Name);

        return NoContent();
    }

    // ── GET /api/datasets/{id}/acl ────────────────────────────────────────────

    [HttpGet("{id:int}/acl")]
    public async Task<IActionResult> GetAcl(int id)
    {
        var dataset = await db.Datasets
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

        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "GRANT_DATASET_PERMISSION", "Dataset", id.ToString(),
            $"{dataset.Name} → group {req.GroupId} as {granted}");

        return Ok();
    }

    // ── DELETE /api/datasets/{id}/acl/{groupId} ───────────────────────────────

    [HttpDelete("{id:int}/acl/{groupId:int}")]
    public async Task<IActionResult> RevokePermission(int id, int groupId)
    {
        var dataset = await LoadDataset(id);
        if (dataset is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(dataset);
        if (!CanManage(perm)) return Forbid();

        var acl = await db.DatasetAcls
            .FirstOrDefaultAsync(a => a.DatasetId == id && a.GroupId == groupId);
        if (acl is null) return NotFound(new { error = "ACL entry not found." });

        db.DatasetAcls.Remove(acl);
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "REVOKE_DATASET_PERMISSION", "Dataset", id.ToString(),
            $"{dataset.Name} → group {groupId}");

        return NoContent();
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
        IsEncrypted: !string.IsNullOrWhiteSpace(d.ParquetFilePath),
        d.CreatedAt, d.UpdatedAt,
        d.OwningReport?.Name,
        d.OwningReportId);

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
            _   => null
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
