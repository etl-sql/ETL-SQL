using System.Security.Claims;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Controllers;

[ApiController]
[Route("api/folders")]
[Authorize]
public class FoldersController(
    PortalDbContext db,
    AuditService audit,
    FolderPermissionService folderPermissions,
    SecuritySessionService securitySessions,
    PortalTenantCatalogScope catalogScope) : ControllerBase
{
    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool IsAdmin => User.IsInRole("Admin");

    // ── Endpoints ─────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetTree()
    {
        var folders = await VisibleFoldersQuery()
            .AsNoTracking()
            .OrderBy(f => f.Path)
            .Select(f => new FolderTreeRow(f.Id, f.ParentId, f.Name, f.Path, f.Version))
            .ToListAsync();

        var visibleIds = folders.Select(f => f.Id).ToHashSet();
        var childrenByParent = folders
            .GroupBy(f => f.ParentId ?? 0)
            .ToDictionary(g => g.Key, g => g.ToList());
        var roots = folders
            .Where(f => f.ParentId == null || !visibleIds.Contains(f.ParentId.Value))
            .ToList();

        FolderDto ToDto(FolderTreeRow f) => new(
            f.Id, f.ParentId, f.Name, f.Path,
            childrenByParent.TryGetValue(f.Id, out var children)
                ? children.Select(ToDto).ToList()
                : [],
            f.Version);

        return Ok(roots.Select(ToDto));
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Publisher")]
    public async Task<IActionResult> Create([FromBody] CreateFolderRequest req)
    {
        string path;
        if (req.ParentId.HasValue)
        {
            var parent = await catalogScope.Folders.SingleOrDefaultAsync(f => f.Id == req.ParentId.Value);
            if (parent is null) return NotFound("Parent folder not found");
            if (!await folderPermissions.HasPermissionAsync(parent.Id, FolderPermission.Manage, User))
                return Forbid();
            path = $"{parent.Path}/{req.Name}";
        }
        else
        {
            if (!IsAdmin) return Forbid();
            path = $"/{req.Name}";
        }

        if (await catalogScope.Folders.AnyAsync(f => f.Path == path))
            return Conflict(new { error = $"Folder '{path}' already exists" });

        var ownerId = CurrentUserId;
        if (!string.IsNullOrWhiteSpace(req.OwnerUsername))
        {
            if (!IsAdmin) return Forbid();
            var requestedOwner = await db.Users.SingleOrDefaultAsync(u =>
                u.TenantId == catalogScope.TenantId && u.UserName == req.OwnerUsername);
            if (requestedOwner is null) return BadRequest($"Catalog owner '{req.OwnerUsername}' was not found.");
            ownerId = requestedOwner.Id;
        }

        var folder = new Folder
        {
            TenantId = catalogScope.TenantId,
            ParentId = req.ParentId,
            Name = req.Name,
            Path = path,
            OwnerId = ownerId
        };
        db.Folders.Add(folder);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            if (await catalogScope.Folders.AsNoTracking().AnyAsync(f => f.Path == path))
                return Conflict(new { error = $"Folder '{path}' already exists" });
            throw;
        }
        await audit.LogAsync(CurrentUserId, "CREATE_FOLDER", "Folder", folder.Id.ToString(), path);

        return CreatedAtAction(nameof(GetById), new { id = folder.Id },
            new FolderDto(folder.Id, folder.ParentId, folder.Name, folder.Path, [], folder.Version));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var folder = await catalogScope.Folders.SingleOrDefaultAsync(f => f.Id == id);
        if (folder is null) return NotFound();
        if (!IsAdmin && !await folderPermissions.HasPermissionAsync(id, FolderPermission.Read, User))
            return Forbid();

        OptimisticConcurrency.SetETag(Response, folder.Version);
        return Ok(new FolderDto(folder.Id, folder.ParentId, folder.Name, folder.Path, [], folder.Version));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,Publisher")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateFolderRequest req)
    {
        var folder = await catalogScope.Folders.SingleOrDefaultAsync(f => f.Id == id);
        if (folder is null) return NotFound();

        if (!await folderPermissions.HasPermissionAsync(folder.Id, FolderPermission.Manage, User))
            return Forbid();

        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null)
            return OptimisticConcurrency.MissingVersion(this);
        if (!OptimisticConcurrency.Prepare(db, folder, expectedVersion.Value))
            return OptimisticConcurrency.Conflict(
                this, new FolderDto(folder.Id, folder.ParentId, folder.Name, folder.Path, [], folder.Version));

        bool pathChanged = false;
        if (req.Name is not null && req.Name != folder.Name)
        {
            folder.Name = req.Name;
            pathChanged = true;
        }

        if (req.ParentId.HasValue && req.ParentId != folder.ParentId)
        {
            if (req.ParentId == id) return BadRequest("Folder cannot be its own parent.");

            // Check for circular reference
            var p = await catalogScope.Folders.SingleOrDefaultAsync(f => f.Id == req.ParentId.Value);
            if (p is null) return NotFound("Target parent folder not found");

            var curr = p;
            while (curr != null)
            {
                if (curr.Id == id) return BadRequest("Folder cannot be moved into its own descendant.");
                curr = curr.ParentId.HasValue
                    ? await catalogScope.Folders.SingleOrDefaultAsync(f => f.Id == curr.ParentId.Value)
                    : null;
            }

            if (!await folderPermissions.HasPermissionAsync(p.Id, FolderPermission.Manage, User))
                return Forbid();

            folder.ParentId = req.ParentId;
            pathChanged = true;
        }
        else if (req.ParentId == null && folder.ParentId != null)
        {
            if (!IsAdmin) return Forbid();
            folder.ParentId = null;
            pathChanged = true;
        }

        if (pathChanged)
        {
            string parentPath = "";
            if (folder.ParentId.HasValue)
            {
                var parent = await catalogScope.Folders.SingleAsync(f => f.Id == folder.ParentId.Value);
                parentPath = parent!.Path;
            }
            folder.Path = parentPath == "" ? $"/{folder.Name}" : $"{parentPath}/{folder.Name}";

            // Recursively update all children's paths
            await UpdatePathsRecursiveAsync(folder);
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await db.Entry(folder).ReloadAsync();
            return OptimisticConcurrency.Conflict(
                this, new FolderDto(folder.Id, folder.ParentId, folder.Name, folder.Path, [], folder.Version));
        }
        await audit.LogAsync(CurrentUserId, "UPDATE_FOLDER", "Folder", id.ToString(), folder.Path);
        OptimisticConcurrency.SetETag(Response, folder.Version);
        return Ok(new FolderDto(folder.Id, folder.ParentId, folder.Name, folder.Path, [], folder.Version));
    }

    private async Task UpdatePathsRecursiveAsync(Folder folder)
    {
        var children = await catalogScope.Folders.Where(f => f.ParentId == folder.Id).ToListAsync();
        foreach (var child in children)
        {
            child.Version = checked(child.Version + 1);
            child.Path = $"{folder.Path}/{child.Name}";
            await UpdatePathsRecursiveAsync(child);
        }
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id, [FromQuery] bool cascade = false)
    {
        var folder = await catalogScope.Folders
            .Include(f => f.Children)
            .Include(f => f.Reports)
            .Include(f => f.Acls)
            .FirstOrDefaultAsync(f => f.Id == id);
        if (folder is null) return NotFound();

        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null)
            return OptimisticConcurrency.MissingVersion(this);
        if (!OptimisticConcurrency.Prepare(db, folder, expectedVersion.Value))
            return OptimisticConcurrency.Conflict(
                this, new FolderDto(folder.Id, folder.ParentId, folder.Name, folder.Path, [], folder.Version));

        bool hasChildren = folder.Children.Any() || folder.Reports.Any(r => !r.IsDeleted);
        if (hasChildren && !cascade)
            return Conflict(new { error = "Folder has contents. Use ?cascade=true to delete recursively." });

        await DeleteFolderRecursiveAsync(folder);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var current = await catalogScope.Folders.SingleOrDefaultAsync(f => f.Id == id);
            return current is null
                ? NotFound()
                : OptimisticConcurrency.Conflict(
                    this, new FolderDto(current.Id, current.ParentId, current.Name, current.Path, [], current.Version));
        }
        await audit.LogAsync(CurrentUserId, "DELETE_FOLDER", "Folder", id.ToString(), folder.Path);
        return NoContent();
    }

    private async Task DeleteFolderRecursiveAsync(Folder folder)
    {
        var children = await catalogScope.Folders
            .Include(f => f.Children)
            .Include(f => f.Reports)
            .Include(f => f.Acls)
            .Where(f => f.ParentId == folder.Id)
            .ToListAsync();

        foreach (var child in children)
            await DeleteFolderRecursiveAsync(child);

        foreach (var report in folder.Reports)
            report.IsDeleted = true;

        db.FolderAcls.RemoveRange(folder.Acls);
        db.Folders.Remove(folder);
    }

    // ── ACL endpoints ─────────────────────────────────────────────────────────

    [HttpGet("{id:int}/acl")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAcl(int id)
    {
        if (!await catalogScope.Folders.AnyAsync(folder => folder.Id == id)) return NotFound();
        var acls = await catalogScope.FolderAcls
            .Include(a => a.Group)
            .Where(a => a.FolderId == id)
            .Select(a => new FolderAclDto(a.GroupId, a.Group.Name, a.Permission))
            .ToListAsync();
        return Ok(acls);
    }

    [HttpPost("{id:int}/acl")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Grant(int id, [FromBody] GrantPermissionRequest req)
    {
        var folder = await catalogScope.Folders.SingleOrDefaultAsync(f => f.Id == id);
        if (folder is null) return NotFound("Folder not found");
        if (!await db.Groups.AnyAsync(g =>
                g.TenantId == catalogScope.TenantId && g.Id == req.GroupId))
            return NotFound("Group not found");
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null)
            return OptimisticConcurrency.MissingVersion(this);
        if (!OptimisticConcurrency.Prepare(db, folder, expectedVersion.Value))
            return OptimisticConcurrency.Conflict(
                this, new FolderDto(folder.Id, folder.ParentId, folder.Name, folder.Path, [], folder.Version));

        var existing = await catalogScope.FolderAcls.FirstOrDefaultAsync(
            a => a.FolderId == id && a.GroupId == req.GroupId);

        if (existing is not null)
            existing.Permission = req.Permission;
        else
            db.FolderAcls.Add(new FolderAcl { FolderId = id, GroupId = req.GroupId, Permission = req.Permission });

        // Staged so the grant and its audit row share one commit (P1.6).
        audit.Stage(CurrentUserId, "GRANT_PERMISSION", "Folder", id.ToString(),
            $"group={req.GroupId} perm={req.Permission}");
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var current = await catalogScope.Folders.SingleOrDefaultAsync(f => f.Id == id);
            return current is null
                ? NotFound()
                : OptimisticConcurrency.Conflict(
                    this, new FolderDto(current.Id, current.ParentId, current.Name, current.Path, [], current.Version));
        }
        await securitySessions.InvalidateGroupMembersAsync(req.GroupId);
        OptimisticConcurrency.SetETag(Response, folder.Version);
        return Ok(new { folder.Version });
    }

    [HttpDelete("{id:int}/acl/{groupId:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Revoke(int id, int groupId)
    {
        var folder = await catalogScope.Folders.SingleOrDefaultAsync(f => f.Id == id);
        if (folder is null) return NotFound();
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null)
            return OptimisticConcurrency.MissingVersion(this);
        if (!OptimisticConcurrency.Prepare(db, folder, expectedVersion.Value))
            return OptimisticConcurrency.Conflict(
                this, new FolderDto(folder.Id, folder.ParentId, folder.Name, folder.Path, [], folder.Version));

        var acl = await catalogScope.FolderAcls.FirstOrDefaultAsync(
            a => a.FolderId == id && a.GroupId == groupId);
        if (acl is null) return NotFound();

        db.FolderAcls.Remove(acl);
        audit.Stage(CurrentUserId, "REVOKE_PERMISSION", "Folder", id.ToString(),
            $"group={groupId}");
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await db.Entry(folder).ReloadAsync();
            return OptimisticConcurrency.Conflict(
                this, new FolderDto(folder.Id, folder.ParentId, folder.Name, folder.Path, [], folder.Version));
        }
        await securitySessions.InvalidateGroupMembersAsync(groupId);
        OptimisticConcurrency.SetETag(Response, folder.Version);
        return Ok(new { folder.Version });
    }

    private IQueryable<Folder> VisibleFoldersQuery()
    {
        if (IsAdmin)
            return catalogScope.Folders;

        var userId = CurrentUserId;
        return catalogScope.Folders.Where(f => catalogScope.FolderAcls.Any(a =>
            a.FolderId == f.Id
            && a.Permission >= FolderPermission.Read
            && db.UserGroups.Any(ug => ug.UserId == userId && ug.GroupId == a.GroupId)));
    }

    private sealed record FolderTreeRow(int Id, int? ParentId, string Name, string Path, long Version);
}
