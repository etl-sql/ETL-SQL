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
    PortalDbContext db,
    IDatasetRegistry registry,
    AuditService audit) : ControllerBase
{
    private int  CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool IsAdmin       => User.IsInRole("Admin");

    // ── Permission helpers ────────────────────────────────────────────────────

    private static DatasetPermission? GetEffectivePermission(
        Dataset dataset, int currentUserId, bool isAdmin, ICollection<int> groupIds)
    {
        if (isAdmin) return DatasetPermission.Owner;
        if (dataset.OwningReport?.CreatedBy == currentUserId) return DatasetPermission.Owner;
        if (dataset.AccessLevel == DatasetAccessLevel.Public) return DatasetPermission.Viewer;

        var acls = dataset.Acls.Where(a => groupIds.Contains(a.GroupId)).ToList();
        if (!acls.Any()) return null;
        return (DatasetPermission)acls.Max(a => (int)a.Permission);
    }

    private async Task<DatasetPermission?> GetEffectivePermissionAsync(Dataset dataset)
    {
        if (IsAdmin) return DatasetPermission.Owner;
        if (dataset.OwningReport?.CreatedBy == CurrentUserId) return DatasetPermission.Owner;
        if (dataset.AccessLevel == DatasetAccessLevel.Public) return DatasetPermission.Viewer;

        var groupIds = await db.UserGroups
            .Where(ug => ug.UserId == CurrentUserId)
            .Select(ug => ug.GroupId)
            .ToListAsync();

        var acls = dataset.Acls.Where(a => groupIds.Contains(a.GroupId)).ToList();
        if (!acls.Any()) return null;
        return (DatasetPermission)acls.Max(a => (int)a.Permission);
    }

    private static bool CanView(DatasetPermission? p)   => p is not null;
    private static bool CanEdit(DatasetPermission? p)   => p is DatasetPermission.Editor or DatasetPermission.Owner;
    private static bool CanManage(DatasetPermission? p) => p is DatasetPermission.Owner;

    // ── GET /api/datasets ─────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var datasets = await db.Datasets
            .Include(d => d.OwningReport)
            .Include(d => d.Acls)
            .ToListAsync();

        var groupIds = IsAdmin
            ? (List<int>)[]
            : await db.UserGroups
                .Where(ug => ug.UserId == CurrentUserId)
                .Select(ug => ug.GroupId)
                .ToListAsync();

        var result = datasets
            .Where(d => CanView(GetEffectivePermission(d, CurrentUserId, IsAdmin, groupIds)))
            .Select(ToDto)
            .ToList();

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
        if (!CanEdit(perm)) return Forbid();

        await registry.SetStale(dataset.Name, dataset.FolderPath);
        await audit.LogAsync(CurrentUserId, "REFRESH_DATASET", "Dataset", id.ToString(), dataset.Name);

        return Ok(new { message = $"Dataset '{dataset.Name}' marked for refresh. It will re-materialise on next USE DATASET." });
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

    // ── DELETE /api/datasets/{id} ─────────────────────────────────────────────

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var dataset = await LoadDataset(id);
        if (dataset is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(dataset);
        if (!CanManage(perm)) return Forbid();

        await registry.Delete(dataset.Name, dataset.FolderPath);
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
            return BadRequest(new { error = "permission must be 'Viewer', 'Editor', or 'Owner'." });

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
