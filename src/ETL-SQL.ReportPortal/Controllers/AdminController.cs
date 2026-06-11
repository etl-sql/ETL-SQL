using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Models;
using ETL_SQL.ReportPortal.Services;

namespace ETL_SQL.ReportPortal.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController(
    UserManager<PortalUser> userManager,
    PortalDbContext          db,
    AuditService             audit,
    PortalConfig             config,
    SubscriptionDeliveryStatusService deliveryStatus,
    DatasetAtRestKeyRotationService datasetKeyRotation,
    IHostApplicationLifetime lifetime) : ControllerBase
{
    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("datasets/rotate-at-rest-key")]
    public async Task<IActionResult> RotateDatasetAtRestKey(CancellationToken cancellationToken)
    {
        DatasetKeyRotationResult result;
        try
        {
            result = await datasetKeyRotation.RotateAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        await audit.LogAsync(
            CurrentUserId,
            "ROTATE_DATASET_AT_REST_KEY",
            "Dataset",
            null,
            $"TargetVersion={result.TargetVersion}; Rotated={result.Rotated}; " +
            $"AlreadyCurrent={result.AlreadyCurrent}; Failed={result.FailedDatasets.Count}");

        return Ok(result);
    }

    // ── Users ─────────────────────────────────────────────────────────────────

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var users = await userManager.Users
            .Include(u => u.UserGroups).ThenInclude(ug => ug.Group)
            .ToListAsync();

        var dtos = new List<UserDto>();
        foreach (var u in users)
        {
            var roles  = await userManager.GetRolesAsync(u);
            var groups = u.UserGroups.Select(ug => ug.Group.Name).ToList();
            dtos.Add(new UserDto(u.Id, u.UserName!, u.Email, u.FirstName, u.LastName,
                u.IsActive, u.MustChangePassword, u.CreatedAt, roles, groups, u.Provider));
        }
        return Ok(dtos);
    }

    [HttpGet("users/catalog")]
    public async Task<IActionResult> GetUserCatalog(
        [FromQuery] string? q = null,
        [FromQuery] string? status = null,
        [FromQuery] string? provider = null,
        [FromQuery] string? role = null,
        [FromQuery] string? group = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = userManager.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = $"%{q.Trim()}%";
            query = query.Where(u =>
                EF.Functions.Like(u.UserName!, term) ||
                EF.Functions.Like(u.Email ?? "", term) ||
                EF.Functions.Like(u.FirstName ?? "", term) ||
                EF.Functions.Like(u.LastName ?? "", term));
        }
        if (string.Equals(status, "active", StringComparison.OrdinalIgnoreCase))
            query = query.Where(u => u.IsActive);
        else if (string.Equals(status, "inactive", StringComparison.OrdinalIgnoreCase))
            query = query.Where(u => !u.IsActive);
        if (!string.IsNullOrWhiteSpace(provider))
            query = query.Where(u => u.Provider == provider);
        if (!string.IsNullOrWhiteSpace(group))
            query = query.Where(u => u.UserGroups.Any(ug => ug.Group.Name == group));
        if (!string.IsNullOrWhiteSpace(role))
        {
            var roleId = await db.Roles
                .Where(r => r.Name == role)
                .Select(r => (int?)r.Id)
                .FirstOrDefaultAsync();
            query = roleId.HasValue
                ? query.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == roleId.Value))
                : query.Where(_ => false);
        }

        var total = await query.CountAsync();
        var users = await query
            .Include(u => u.UserGroups).ThenInclude(ug => ug.Group)
            .OrderBy(u => u.UserName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var items = new List<UserDto>();
        foreach (var u in users)
        {
            var roles = await userManager.GetRolesAsync(u);
            var groups = u.UserGroups.Select(ug => ug.Group.Name).OrderBy(name => name).ToList();
            items.Add(new UserDto(u.Id, u.UserName!, u.Email, u.FirstName, u.LastName,
                u.IsActive, u.MustChangePassword, u.CreatedAt, roles, groups, u.Provider));
        }
        return Ok(new PagedResult<UserDto>(items, total, page, pageSize));
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest req)
    {
        var provider = req.Provider ?? "Local";
        var user = new PortalUser
        {
            UserName           = req.Username,
            Email              = req.Email,
            FirstName          = req.FirstName,
            LastName           = req.LastName,
            IsActive           = true,
            MustChangePassword = provider != "LDAP",
            Provider           = provider
        };

        IdentityResult result;
        if (provider == "LDAP")
        {
            result = await userManager.CreateAsync(user);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(req.Password))
                return BadRequest(new { errors = new[] { "Password is required for local users." } });
            result = await userManager.CreateAsync(user, req.Password);
        }

        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        if (!string.IsNullOrWhiteSpace(req.Role))
            await userManager.AddToRoleAsync(user, req.Role);

        await audit.LogAsync(CurrentUserId, "CREATE_USER", "User", user.Id.ToString(), req.Username);
        return CreatedAtAction(nameof(GetUser), new { id = user.Id },
            new UserDto(user.Id, user.UserName!, user.Email, user.FirstName, user.LastName,
                true, user.MustChangePassword, user.CreatedAt, [req.Role], [], user.Provider));
    }

    [HttpGet("users/{id:int}")]
    public async Task<IActionResult> GetUser(int id)
    {
        var user = await userManager.Users
            .Include(u => u.UserGroups).ThenInclude(ug => ug.Group)
            .FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();

        var roles  = await userManager.GetRolesAsync(user);
        var groups = user.UserGroups.Select(ug => ug.Group.Name).ToList();
        return Ok(new UserDto(user.Id, user.UserName!, user.Email, user.FirstName, user.LastName,
            user.IsActive, user.MustChangePassword, user.CreatedAt, roles, groups, user.Provider));
    }

    [HttpPut("users/{id:int}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserRequest req)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();

        var wasActive = user.IsActive;
        if (req.Email    is not null) user.Email    = req.Email;
        if (req.FirstName is not null) user.FirstName = req.FirstName;
        if (req.LastName  is not null) user.LastName  = req.LastName;
        if (req.IsActive.HasValue)    user.IsActive  = req.IsActive.Value;

        if (req.Role is not null)
        {
            var currentRoles = await userManager.GetRolesAsync(user);
            await userManager.RemoveFromRolesAsync(user, currentRoles);
            await userManager.AddToRoleAsync(user, req.Role);
        }

        await userManager.UpdateAsync(user);
        if (wasActive && req.IsActive == false)
        {
            var tokens = await db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null).ToListAsync();
            foreach (var t in tokens) t.RevokedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        await audit.LogAsync(CurrentUserId, "UPDATE_USER", "User", id.ToString());
        return NoContent();
    }

    [HttpPost("users/bulk-status")]
    public async Task<IActionResult> BulkUpdateUserStatus([FromBody] BulkUserStatusRequest req)
    {
        var ids = req.UserIds.Distinct().ToList();
        if (ids.Count == 0) return BadRequest(new { error = "Select at least one user." });

        var users = await db.Users.Where(u => ids.Contains(u.Id)).ToListAsync();
        foreach (var user in users)
            user.IsActive = req.IsActive;

        if (!req.IsActive)
        {
            var tokens = await db.RefreshTokens
                .Where(t => ids.Contains(t.UserId) && t.RevokedAt == null)
                .ToListAsync();
            foreach (var token in tokens) token.RevokedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "BULK_UPDATE_USER_STATUS", "User", null,
            $"{users.Count} users set active={req.IsActive}");
        return Ok(new { Updated = users.Count });
    }

    [HttpDelete("users/{id:int}")]
    public async Task<IActionResult> DeleteUser(int id, [FromQuery] bool cascade = false)
    {
        var user = await userManager.Users
            .Include(u => u.Subscriptions.Where(s => s.IsActive))
            .FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();

        if (user.Subscriptions.Any() && !cascade)
            return Conflict(new { error = "User has active subscriptions. Use ?cascade=true." });

        if (cascade)
            foreach (var sub in user.Subscriptions)
                sub.IsActive = false;

        // Revoke all tokens and remove group memberships
        var tokens = await db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null).ToListAsync();
        foreach (var t in tokens) t.RevokedAt = DateTime.UtcNow;

        var memberships = await db.UserGroups.Where(ug => ug.UserId == id).ToListAsync();
        db.UserGroups.RemoveRange(memberships);

        await db.SaveChangesAsync();
        await userManager.DeleteAsync(user);
        await audit.LogAsync(CurrentUserId, "DELETE_USER", "User", id.ToString(), user.UserName);
        return NoContent();
    }

    // ── Admin password reset ──────────────────────────────────────────────────

    [HttpPost("users/{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, [FromBody] ResetPasswordRequest req)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();

        var token  = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, req.NewPassword);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        user.MustChangePassword = true;
        await userManager.UpdateAsync(user);
        await audit.LogAsync(CurrentUserId, "RESET_PASSWORD", "User", id.ToString());
        return NoContent();
    }

    [HttpPost("users/{id:int}/revoke-tokens")]
    public async Task<IActionResult> RevokeTokens(int id)
    {
        var tokens = await db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null).ToListAsync();
        foreach (var t in tokens) t.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "REVOKE_TOKENS", "User", id.ToString());
        return NoContent();
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> GetActiveSessions()
    {
        var now = DateTime.UtcNow;
        var sessions = await db.RefreshTokens
            .Include(t => t.User)
            .Where(t => t.RevokedAt == null && t.ExpiresAt > now)
            .OrderBy(t => t.ExpiresAt)
            .Select(t => new
            {
                t.Id,
                t.UserId,
                Username = t.User.UserName,
                t.ExpiresAt
            })
            .ToListAsync();

        return Ok(sessions);
    }

    [HttpPost("users/{id:int}/disconnect")]
    public async Task<IActionResult> DisconnectUser(int id)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id)) return NotFound();

        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == id && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();
        foreach (var t in tokens) t.RevokedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "DISCONNECT_USER", "User", id.ToString());
        return Ok(new { Revoked = tokens.Count });
    }

    [HttpGet("users/{userId:int}/favorites")]
    public async Task<IActionResult> GetUserFavorites(int userId, [FromQuery] int limit = 50)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId)) return NotFound();
        limit = Math.Clamp(limit, 1, 100);

        var reports = await db.ReportFavorites
            .Include(f => f.Report).ThenInclude(r => r.Folder)
            .Where(f => f.UserId == userId && !f.Report.IsDeleted)
            .OrderByDescending(f => f.CreatedAt)
            .Take(limit)
            .Select(f => f.Report)
            .ToListAsync();

        return Ok(reports.Select(r => new
        {
            Type = "Report",
            r.Id,
            r.Name,
            Path = CombinePath(r.Folder.Path, r.Name),
            r.FolderId,
            r.Description,
            r.Tags,
            r.Category,
            r.Owner,
            r.Certification,
            r.LastViewedAt,
            r.LastRefreshStatus,
            r.LastRefreshError,
            r.LastRefreshDurationMs,
            IsFavorite = true
        }));
    }

    [HttpPost("users/{userId:int}/favorites/{reportId:int}")]
    public async Task<IActionResult> FavoriteReportForUser(int userId, int reportId)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId)) return NotFound(new { error = "User not found" });
        if (!await db.Reports.AnyAsync(r => r.Id == reportId && !r.IsDeleted)) return NotFound(new { error = "Report not found" });
        if (!await db.ReportFavorites.AnyAsync(f => f.UserId == userId && f.ReportId == reportId))
        {
            db.ReportFavorites.Add(new ReportFavorite { UserId = userId, ReportId = reportId });
            await db.SaveChangesAsync();
            await audit.LogAsync(CurrentUserId, "FAVORITE_REPORT_FOR_USER", "Report", reportId.ToString(), userId.ToString());
        }
        return NoContent();
    }

    [HttpDelete("users/{userId:int}/favorites/{reportId:int}")]
    public async Task<IActionResult> UnfavoriteReportForUser(int userId, int reportId)
    {
        var favorite = await db.ReportFavorites.FirstOrDefaultAsync(f => f.UserId == userId && f.ReportId == reportId);
        if (favorite is not null)
        {
            db.ReportFavorites.Remove(favorite);
            await db.SaveChangesAsync();
            await audit.LogAsync(CurrentUserId, "UNFAVORITE_REPORT_FOR_USER", "Report", reportId.ToString(), userId.ToString());
        }
        return NoContent();
    }

    // ── Groups ────────────────────────────────────────────────────────────────

    [HttpGet("groups")]
    public async Task<IActionResult> GetGroups()
    {
        var groups = await db.Groups
            .Select(g => new GroupDto(g.Id, g.Name, g.Description,
                g.UserGroups.Count, g.Provider, g.AdGroup))
            .ToListAsync();
        return Ok(groups);
    }

    [HttpGet("groups/catalog")]
    public async Task<IActionResult> GetGroupCatalog(
        [FromQuery] string? q = null,
        [FromQuery] string? provider = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Groups.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = $"%{q.Trim()}%";
            query = query.Where(g =>
                EF.Functions.Like(g.Name, term) ||
                EF.Functions.Like(g.Description ?? "", term) ||
                EF.Functions.Like(g.AdGroup ?? "", term));
        }
        if (!string.IsNullOrWhiteSpace(provider))
            query = query.Where(g => g.Provider == provider);

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(g => g.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(g => new GroupDto(g.Id, g.Name, g.Description,
                g.UserGroups.Count, g.Provider, g.AdGroup))
            .ToListAsync();
        return Ok(new PagedResult<GroupDto>(items, total, page, pageSize));
    }

    [HttpPost("groups")]
    public async Task<IActionResult> CreateGroup([FromBody] CreateGroupRequest req)
    {
        if (await db.Groups.AnyAsync(g => g.Name == req.Name))
            return Conflict(new { error = $"Group '{req.Name}' already exists" });

        var group = new Group
        {
            Name = req.Name,
            Description = req.Description,
            Provider = req.Provider ?? "Local",
            AdGroup = req.AdGroup
        };
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "CREATE_GROUP", "Group", group.Id.ToString(), req.Name);
        return CreatedAtAction(nameof(GetGroup), new { id = group.Id },
            new GroupDto(group.Id, group.Name, group.Description, 0, group.Provider, group.AdGroup));
    }

    [HttpGet("groups/{id:int}")]
    public async Task<IActionResult> GetGroup(int id)
    {
        var group = await db.Groups
            .Where(g => g.Id == id)
            .Select(g => new GroupDto(g.Id, g.Name, g.Description, g.UserGroups.Count, g.Provider, g.AdGroup))
            .FirstOrDefaultAsync();
        return group is null ? NotFound() : Ok(group);
    }

    [HttpPut("groups/{id:int}")]
    public async Task<IActionResult> UpdateGroup(int id, [FromBody] UpdateGroupRequest req)
    {
        var group = await db.Groups.FindAsync(id);
        if (group is null) return NotFound();

        if (req.Name is not null)
        {
            if (await db.Groups.AnyAsync(g => g.Name == req.Name && g.Id != id))
                return Conflict(new { error = $"Group '{req.Name}' already exists" });
            group.Name = req.Name;
        }

        if (req.Description is not null)
            group.Description = req.Description;

        if (req.Provider is not null)
            group.Provider = req.Provider;

        if (req.AdGroup is not null)
            group.AdGroup = req.AdGroup;

        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "UPDATE_GROUP", "Group", id.ToString());
        return NoContent();
    }

    [HttpDelete("groups/{id:int}")]
    public async Task<IActionResult> DeleteGroup(int id, [FromQuery] bool cascade = false)
    {
        var group = await db.Groups
            .Include(g => g.UserGroups)
            .Include(g => g.FolderAcls)
            .FirstOrDefaultAsync(g => g.Id == id);
        if (group is null) return NotFound();

        bool hasEntries = group.UserGroups.Any() || group.FolderAcls.Any();
        if (hasEntries && !cascade)
            return Conflict(new { error = "Group has members or ACL entries. Use ?cascade=true." });

        db.UserGroups.RemoveRange(group.UserGroups);
        db.FolderAcls.RemoveRange(group.FolderAcls);
        db.Groups.Remove(group);
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "DELETE_GROUP", "Group", id.ToString(), group.Name);
        return NoContent();
    }

    [HttpPost("groups/bulk-delete")]
    public async Task<IActionResult> BulkDeleteGroups([FromBody] BulkDeleteGroupsRequest req)
    {
        var ids = req.GroupIds.Distinct().ToList();
        if (ids.Count == 0) return BadRequest(new { error = "Select at least one group." });

        var groups = await db.Groups
            .Include(g => g.UserGroups)
            .Include(g => g.FolderAcls)
            .Where(g => ids.Contains(g.Id))
            .ToListAsync();
        if (!req.Cascade && groups.Any(g => g.UserGroups.Any() || g.FolderAcls.Any()))
            return Conflict(new { error = "One or more groups have members or ACL entries. Use cascade=true." });

        foreach (var group in groups)
        {
            db.UserGroups.RemoveRange(group.UserGroups);
            db.FolderAcls.RemoveRange(group.FolderAcls);
        }
        db.Groups.RemoveRange(groups);
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "BULK_DELETE_GROUPS", "Group", null, $"{groups.Count} groups deleted");
        return Ok(new { Deleted = groups.Count });
    }

    [HttpGet("groups/{id:int}/members")]
    public async Task<IActionResult> GetMembers(int id)
    {
        if (!await db.Groups.AnyAsync(g => g.Id == id)) return NotFound();
        var members = await db.UserGroups
            .Where(ug => ug.GroupId == id)
            .Join(db.Users, ug => ug.UserId, u => u.Id,
                  (ug, u) => new { u.Id, u.UserName })
            .ToListAsync();
        return Ok(members);
    }

    [HttpGet("groups/{id:int}/members/catalog")]
    public async Task<IActionResult> GetMemberCatalog(
        int id,
        [FromQuery] string? q = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        if (!await db.Groups.AnyAsync(g => g.Id == id)) return NotFound();
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.UserGroups
            .Where(ug => ug.GroupId == id)
            .Join(db.Users, ug => ug.UserId, u => u.Id,
                (ug, u) => u);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = $"%{q.Trim()}%";
            query = query.Where(u =>
                EF.Functions.Like(u.UserName!, term) ||
                EF.Functions.Like(u.Email ?? "", term));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(u => u.UserName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new GroupMemberDto(u.Id, u.UserName!, u.Email, u.IsActive))
            .ToListAsync();
        return Ok(new PagedResult<GroupMemberDto>(items, total, page, pageSize));
    }

    [HttpPost("groups/{id:int}/members")]
    public async Task<IActionResult> AddMember(int id, [FromBody] AddUserToGroupRequest req)
    {
        if (!await db.Groups.AnyAsync(g => g.Id == id)) return NotFound("Group not found");

        PortalUser? user = req.UserId.HasValue
            ? await userManager.FindByIdAsync(req.UserId.Value.ToString())
            : req.Username is not null ? await userManager.FindByNameAsync(req.Username) : null;

        if (user is null) return NotFound("User not found");

        if (await db.UserGroups.AnyAsync(ug => ug.UserId == user.Id && ug.GroupId == id))
            return Conflict(new { error = "User is already a member" });

        db.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = id });
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "ADD_USER_TO_GROUP", "Group", id.ToString(), user.UserName);
        return NoContent();
    }

    [HttpPost("groups/{id:int}/members/bulk-add")]
    public async Task<IActionResult> BulkAddMembers(int id, [FromBody] BulkGroupMembershipRequest req)
    {
        if (!await db.Groups.AnyAsync(g => g.Id == id)) return NotFound("Group not found");
        var ids = req.UserIds.Distinct().ToList();
        if (ids.Count == 0) return BadRequest(new { error = "Select at least one user." });

        var existingIds = await db.UserGroups
            .Where(ug => ug.GroupId == id && ids.Contains(ug.UserId))
            .Select(ug => ug.UserId)
            .ToListAsync();
        var validIds = await db.Users
            .Where(u => ids.Contains(u.Id) && !existingIds.Contains(u.Id))
            .Select(u => u.Id)
            .ToListAsync();
        db.UserGroups.AddRange(validIds.Select(userId => new UserGroup { GroupId = id, UserId = userId }));
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "BULK_ADD_USERS_TO_GROUP", "Group", id.ToString(),
            $"{validIds.Count} users added");
        return Ok(new { Added = validIds.Count });
    }

    [HttpPost("groups/{id:int}/members/bulk-remove")]
    public async Task<IActionResult> BulkRemoveMembers(int id, [FromBody] BulkGroupMembershipRequest req)
    {
        if (!await db.Groups.AnyAsync(g => g.Id == id)) return NotFound("Group not found");
        var ids = req.UserIds.Distinct().ToList();
        if (ids.Count == 0) return BadRequest(new { error = "Select at least one user." });

        var memberships = await db.UserGroups
            .Where(ug => ug.GroupId == id && ids.Contains(ug.UserId))
            .ToListAsync();
        db.UserGroups.RemoveRange(memberships);
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "BULK_REMOVE_USERS_FROM_GROUP", "Group", id.ToString(),
            $"{memberships.Count} users removed");
        return Ok(new { Removed = memberships.Count });
    }

    [HttpDelete("groups/{id:int}/members/{userId:int}")]
    public async Task<IActionResult> RemoveMember(int id, int userId)
    {
        var ug = await db.UserGroups.FirstOrDefaultAsync(x => x.GroupId == id && x.UserId == userId);
        if (ug is null) return NotFound();

        db.UserGroups.Remove(ug);
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "REMOVE_USER_FROM_GROUP", "Group", id.ToString());
        return NoContent();
    }

    // ── Reports ───────────────────────────────────────────────────────────────

    [HttpGet("reports")]
    public async Task<IActionResult> GetReports()
    {
        var reports = await db.Reports
            .Include(r => r.Folder)
            .Where(r => !r.IsDeleted)
            .OrderBy(r => r.Folder!.Path).ThenBy(r => r.Name)
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.Description,
                r.ScriptPath,
                r.FolderId,
                FolderName = r.Folder != null ? r.Folder.Name : "Root",
                FolderPath = r.Folder != null ? r.Folder.Path : "/",
                r.CreatedAt,
                r.UpdatedAt
            })
            .ToListAsync();

        return Ok(reports);
    }

    // ── Effective permissions ────────────────────────────────────────────────

    [HttpGet("permissions/effective/user/{userId:int}")]
    public async Task<IActionResult> GetEffectivePermissionsForUser(int userId)
    {
        var user = await userManager.Users
            .Include(u => u.UserGroups).ThenInclude(ug => ug.Group)
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return NotFound();

        var groupIds = user.UserGroups.Select(ug => ug.GroupId).ToHashSet();
        var groupNames = user.UserGroups.Select(ug => ug.Group.Name).OrderBy(n => n).ToList();

        var folders = await db.Folders
            .Include(f => f.Acls).ThenInclude(a => a.Group)
            .OrderBy(f => f.Path)
            .ToListAsync();

        var folderEntries = folders
            .Select(f => BuildFolderPermissionEntry(f, groupIds))
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList();

        var reportRows = await db.Reports
            .Include(r => r.Folder).ThenInclude(f => f.Acls).ThenInclude(a => a.Group)
            .Where(r => !r.IsDeleted)
            .OrderBy(r => r.Folder.Path)
            .ThenBy(r => r.Name)
            .ToListAsync();

        var reports = reportRows
            .Select(r =>
            {
                var entry = BuildFolderPermissionEntry(r.Folder, groupIds);
                return entry is null
                    ? null
                    : new EffectivePermissionEntryDto(
                        "Report",
                        r.Id,
                        r.Name,
                        CombinePath(r.Folder.Path, r.Name),
                        entry.Permission,
                        entry.Sources);
            })
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList();

        return Ok(new EffectiveUserPermissionsDto(user.Id, user.UserName!, groupNames, folderEntries, reports));
    }

    [HttpGet("permissions/effective/folder/{folderId:int}")]
    public async Task<IActionResult> GetEffectivePermissionsForFolder(int folderId)
    {
        var folder = await db.Folders
            .Include(f => f.Acls).ThenInclude(a => a.Group)
            .FirstOrDefaultAsync(f => f.Id == folderId);
        if (folder is null) return NotFound();

        var users = await userManager.Users
            .Include(u => u.UserGroups).ThenInclude(ug => ug.Group)
            .OrderBy(u => u.UserName)
            .ToListAsync();

        return Ok(users
            .Select(u => BuildPrincipalPermission(u, folder.Acls))
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList());
    }

    [HttpGet("permissions/effective/report/{reportId:int}")]
    public async Task<IActionResult> GetEffectivePermissionsForReport(int reportId)
    {
        var report = await db.Reports
            .Include(r => r.Folder).ThenInclude(f => f.Acls).ThenInclude(a => a.Group)
            .FirstOrDefaultAsync(r => r.Id == reportId && !r.IsDeleted);
        if (report is null) return NotFound();

        var users = await userManager.Users
            .Include(u => u.UserGroups).ThenInclude(ug => ug.Group)
            .OrderBy(u => u.UserName)
            .ToListAsync();

        return Ok(users
            .Select(u => BuildPrincipalPermission(u, report.Folder.Acls))
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList());
    }

    // ── Usage metrics ────────────────────────────────────────────────────────

    [HttpGet("metrics/usage")]
    public async Task<IActionResult> GetUsageMetrics([FromQuery] int days = 30)
    {
        days = Math.Clamp(days, 1, 366);
        var since = DateTime.UtcNow.AddDays(-days);
        await deliveryStatus.SynchronizeAllAsync();

        var viewLogs = await db.AuditLogs
            .Where(a => a.Action == "VIEW_SNAPSHOT"
                && a.ResourceType == "Report"
                && a.ResourceId != null
                && a.Timestamp >= since)
            .ToListAsync();

        var viewsByReport = viewLogs
            .Select(a => new
            {
                Log = a,
                Parsed = int.TryParse(a.ResourceId, out var id) ? id : (int?)null
            })
            .Where(x => x.Parsed.HasValue)
            .GroupBy(x => x.Parsed!.Value)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    ViewCount = g.Count(),
                    UniqueViewers = g.Where(x => x.Log.UserId.HasValue).Select(x => x.Log.UserId!.Value).Distinct().Count(),
                    LastViewedAt = g.Max(x => x.Log.Timestamp)
                });

        var subscriptionFailures = await db.Subscriptions
            .Where(s => s.FailCount > 0)
            .GroupBy(s => s.ReportId)
            .Select(g => new { ReportId = g.Key, Failures = g.Sum(s => s.FailCount) })
            .ToDictionaryAsync(x => x.ReportId, x => x.Failures);

        var reports = await db.Reports
            .Include(r => r.Folder)
            .Where(r => !r.IsDeleted)
            .OrderBy(r => r.Folder.Path)
            .ThenBy(r => r.Name)
            .ToListAsync();

        var rows = reports.Select(r =>
        {
            viewsByReport.TryGetValue(r.Id, out var views);
            subscriptionFailures.TryGetValue(r.Id, out var subFailures);
            return new ReportUsageMetricDto(
                r.Id,
                r.Name,
                r.Folder.Path,
                views?.ViewCount ?? 0,
                views?.UniqueViewers ?? 0,
                views?.LastViewedAt,
                r.LastRefreshStatus,
                r.LastRefreshDurationMs,
                r.LastRefreshError,
                subFailures);
        }).ToList();

        var totalRefreshDurations = reports
            .Where(r => r.LastRefreshDurationMs.HasValue)
            .Select(r => r.LastRefreshDurationMs!.Value)
            .ToList();

        return Ok(new PortalUsageMetricsDto(
            viewLogs.Count,
            viewLogs.Where(a => a.UserId.HasValue).Select(a => a.UserId!.Value).Distinct().Count(),
            viewsByReport.Count,
            reports.Count(r => string.Equals(r.LastRefreshStatus, "Failed", StringComparison.OrdinalIgnoreCase)),
            totalRefreshDurations.Count == 0 ? null : totalRefreshDurations.Average(),
            subscriptionFailures.Values.Sum(),
            rows));
    }

    // ── Audit log ─────────────────────────────────────────────────────────────

    [HttpGet("audit")]
    public async Task<IActionResult> GetAudit(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? action = null,
        [FromQuery] int? userId = null)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = db.AuditLogs.AsQueryable();
        if (action  is not null) query = query.Where(a => a.Action == action);
        if (userId.HasValue)    query = query.Where(a => a.UserId == userId);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
#pragma warning disable CS8602
            .Join(db.Users.DefaultIfEmpty(),
                  a => a.UserId,
                  u => (int?)u.Id,
                  (a, u) => new AuditLogDto(a.Id, a.UserId, u != null ? u.UserName! : null,
                      a.Action, a.ResourceType, a.ResourceId, a.Timestamp, a.Detail))
#pragma warning restore CS8602
            .ToListAsync();

        return Ok(new PagedResult<AuditLogDto>(items, total, page, pageSize));
    }

    [HttpGet("audit/export/csv")]
    public async Task<IActionResult> ExportAuditCsv(
        [FromQuery] string? action = null,
        [FromQuery] int? userId = null)
    {
        // COMPAT_BREAK: 0.10
        await audit.LogAsync(CurrentUserId, "EXPORT_AUDIT_LOG", "AuditLog", null,
            $"action={action ?? "*"};userId={userId?.ToString() ?? "*"}");

        var query = db.AuditLogs.AsQueryable();
        if (action  is not null) query = query.Where(a => a.Action == action);
        if (userId.HasValue)    query = query.Where(a => a.UserId == userId);

        var items = await query
            .OrderByDescending(a => a.Timestamp)
            .Take(10_000)
#pragma warning disable CS8602
            .Join(db.Users.DefaultIfEmpty(),
                  a => a.UserId,
                  u => (int?)u.Id,
                  (a, u) => new { a, Username = u != null ? u.UserName : null })
#pragma warning restore CS8602
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("Id,Timestamp,UserId,Username,Action,ResourceType,ResourceId,Detail");
        foreach (var row in items)
        {
            sb.AppendLine(string.Join(",",
                CsvField(row.a.Id.ToString()),
                CsvField(row.a.Timestamp.ToString("o")),
                CsvField(row.a.UserId?.ToString()),
                CsvField(row.Username),
                CsvField(row.a.Action),
                CsvField(row.a.ResourceType),
                CsvField(row.a.ResourceId),
                CsvField(row.a.Detail)));
        }

        var filename = $"audit_log_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv";
        return File(
            Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(),
            "text/csv; charset=utf-8",
            filename);
    }

    // ── Orchestrator connection settings ──────────────────────────────────────

    [HttpGet("settings/orchestrator")]
    public IActionResult GetOrchestratorSettings(
        [FromServices] ETL_SQL.ReportPortal.Services.OrchestratorSettingsService settings)
    {
        return Ok(new
        {
            ApiUrl    = settings.ApiUrl,
            HasApiKey = !string.IsNullOrEmpty(settings.ApiKey)
        });
    }

    [HttpPut("settings/orchestrator")]
    public async Task<IActionResult> UpdateOrchestratorSettings(
        [FromServices] ETL_SQL.ReportPortal.Services.OrchestratorSettingsService settings,
        [FromBody] Models.UpdateOrchestratorSettingsRequest req)
    {
        settings.Update(req.ApiUrl, req.ApiKey);
        await audit.LogAsync(CurrentUserId, "UPDATE_ORCHESTRATOR_SETTINGS", "System", null, req.ApiUrl);
        return NoContent();
    }

    // ── Service control ────────────────────────────────────────────────────────

    [HttpPost("service/restart")]
    public async Task<IActionResult> RestartService()
    {
        if (!config.AllowServiceControl)
            return StatusCode(403, new { error = "Portal service control is disabled. Set Portal:AllowServiceControl=true to enable it." });

        await audit.LogAsync(CurrentUserId, "RESTART_PORTAL", "System", null);
        _ = Task.Run(async () =>
        {
            await Task.Delay(250);
            lifetime.StopApplication();
        });
        return Ok(new { message = "Portal restart requested. The external service supervisor must restart the process." });
    }

    [HttpPost("service/shutdown")]
    public async Task<IActionResult> ShutdownService()
    {
        if (!config.AllowServiceControl)
            return StatusCode(403, new { error = "Portal service control is disabled. Set Portal:AllowServiceControl=true to enable it." });

        await audit.LogAsync(CurrentUserId, "SHUTDOWN_PORTAL", "System", null);
        _ = Task.Run(async () =>
        {
            await Task.Delay(250);
            lifetime.StopApplication();
        });
        return Ok(new { message = "Portal shutdown requested." });
    }

    // ── Portal branding settings ──────────────────────────────────────────────

    [HttpGet("settings/branding")]
    public IActionResult GetBrandingSettings(
        [FromServices] ETL_SQL.ReportPortal.Services.PortalBrandingSettingsService branding)
    {
        return Ok(branding.ToDto());
    }

    [HttpPut("settings/branding")]
    public async Task<IActionResult> UpdateBrandingSettings(
        [FromServices] ETL_SQL.ReportPortal.Services.PortalBrandingSettingsService branding,
        [FromBody] Models.UpdatePortalBrandingRequest req)
    {
        try
        {
            branding.Update(req.DisplayName, req.FooterText, req.LogoUrl);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(500, new { Error = ex.Message });
        }

        await audit.LogAsync(CurrentUserId, "UPDATE_PORTAL_BRANDING", "System", null, req.DisplayName);
        return NoContent();
    }

    private static string CsvField(string? value)
    {
        if (value is null) return string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static EffectivePermissionEntryDto? BuildFolderPermissionEntry(Folder folder, ISet<int> groupIds)
    {
        var matching = folder.Acls
            .Where(a => groupIds.Contains(a.GroupId))
            .OrderByDescending(a => a.Permission)
            .ThenBy(a => a.Group.Name)
            .ToList();
        if (matching.Count == 0) return null;

        var effective = matching.First().Permission;
        var sources = matching
            .Where(a => a.Permission == effective)
            .Select(a => $"GROUP {a.Group.Name}")
            .OrderBy(s => s)
            .ToList();

        return new EffectivePermissionEntryDto(
            "Folder",
            folder.Id,
            folder.Name,
            folder.Path,
            effective.ToString(),
            sources);
    }

    private static EffectivePrincipalPermissionDto? BuildPrincipalPermission(
        PortalUser user,
        IEnumerable<FolderAcl> acls)
    {
        var groupIds = user.UserGroups.Select(ug => ug.GroupId).ToHashSet();
        var matching = acls
            .Where(a => groupIds.Contains(a.GroupId))
            .OrderByDescending(a => a.Permission)
            .ThenBy(a => a.Group.Name)
            .ToList();
        if (matching.Count == 0) return null;

        var effective = matching.First().Permission;
        var groups = user.UserGroups.Select(ug => ug.Group.Name).OrderBy(n => n).ToList();
        var sources = matching
            .Where(a => a.Permission == effective)
            .Select(a => $"GROUP {a.Group.Name}")
            .OrderBy(s => s)
            .ToList();

        return new EffectivePrincipalPermissionDto(
            user.Id,
            user.UserName!,
            groups,
            effective.ToString(),
            sources);
    }

    private static string CombinePath(string folderPath, string reportName) =>
        folderPath.EndsWith('/') ? folderPath + reportName : $"{folderPath}/{reportName}";
}
