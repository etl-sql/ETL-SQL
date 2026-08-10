using System.Security.Claims;
using System.Text;
using ETL_SQL.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace ETL_SQL.Portal.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController(
    UserManager<PortalUser> userManager,
    PortalDbContext db,
    AuditService audit,
    SecuritySessionService securitySessions,
    PortalConfig config,
    SubscriptionDeliveryStatusService deliveryStatus,
    DatasetAtRestKeyRotationService datasetKeyRotation,
    OrchestratorDbLocator orchestratorDb,
    IOrchestratorStoreFactory orchestratorStoreFactory,
    IHostApplicationLifetime lifetime) : ControllerBase
{
    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>True when this request is authenticated by a service account token, not a person.</summary>
    private bool IsServiceIdentity =>
        User.FindFirstValue(TokenService.IdentityTypeClaim) == TokenService.ServiceIdentityType;

    /// <summary>
    /// Creating or promoting an <c>Admin</c> is the one identity operation that requires a human.
    /// A service token that could mint admins could grant itself — through a new account — every
    /// capability the <c>admin.identity</c> scope deliberately withholds (backup, export,
    /// key rotation, shutdown), which would make the narrow scope meaningless. Demotion is
    /// deliberately still allowed: revoking an admin during an incident should not need a browser.
    /// </summary>
    private IActionResult? DenyIfTokenIsElevatingToAdmin(string? requestedRole)
    {
        if (!IsServiceIdentity) return null;
        if (!string.Equals(requestedRole, "Admin", StringComparison.OrdinalIgnoreCase)) return null;

        return StatusCode(StatusCodes.Status403Forbidden, new
        {
            error = "admin_elevation_requires_interactive_user",
            message = "A service account cannot create or promote an Admin. Perform this change as a signed-in administrator."
        });
    }

    private async Task<UserDto> ToUserDtoAsync(PortalUser user, CancellationToken cancellationToken = default) =>
        (await ToUserDtosAsync([user], cancellationToken)).Single();

    private async Task<List<UserDto>> ToUserDtosAsync(
        IReadOnlyCollection<PortalUser> users,
        CancellationToken cancellationToken = default)
    {
        var userIds = users.Select(u => u.Id).ToArray();
        if (userIds.Length == 0) return [];

        var roleRows = await (from ur in db.UserRoles
                              join r in db.Roles on ur.RoleId equals r.Id
                              where userIds.Contains(ur.UserId)
                              select new { ur.UserId, RoleName = r.Name! })
            .AsNoTracking()
            .OrderBy(row => row.RoleName)
            .ToListAsync(cancellationToken);

        var groupRows = await db.UserGroups
            .AsNoTracking()
            .Where(ug => userIds.Contains(ug.UserId))
            .Select(ug => new { ug.UserId, GroupName = ug.Group.Name })
            .OrderBy(row => row.GroupName)
            .ToListAsync(cancellationToken);

        var rolesByUser = roleRows
            .GroupBy(row => row.UserId)
            .ToDictionary(group => group.Key, group => (IList<string>)group.Select(row => row.RoleName).ToList());
        var groupsByUser = groupRows
            .GroupBy(row => row.UserId)
            .ToDictionary(group => group.Key, group => (IList<string>)group.Select(row => row.GroupName).ToList());

        return users.Select(user => new UserDto(
            user.Id,
            user.UserName!,
            user.Email,
            user.FirstName,
            user.LastName,
            user.IsActive,
            user.MustChangePassword,
            user.CreatedAt,
            rolesByUser.GetValueOrDefault(user.Id, []),
            groupsByUser.GetValueOrDefault(user.Id, []),
            user.Provider,
            user.Version)).ToList();
    }

    private static GroupDto ToGroupDto(Group group, int memberCount) =>
        new(group.Id, group.Name, group.Description, memberCount, group.Provider, group.AdGroup, group.Version);

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
    public async Task<IActionResult> GetUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = userManager.Users.AsNoTracking().OrderBy(u => u.UserName);
        var total = await query.CountAsync(cancellationToken);
        var users = await userManager.Users
            .AsNoTracking()
            .OrderBy(u => u.UserName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(new PagedResult<UserDto>(
            await ToUserDtosAsync(users, cancellationToken),
            total,
            page,
            pageSize));
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

        int total;
        List<PortalUser> users;

        if (!string.IsNullOrWhiteSpace(q))
        {
            // Filter server-side so the database does the matching and pagination instead of loading the
            // whole users table into memory. ToLower()+Contains translates to LOWER(col) LIKE '%term%',
            // which is case-insensitive on both SQLite and PostgreSQL (the previous
            // Contains(..., StringComparison.OrdinalIgnoreCase) overload is not EF-translatable, so it
            // forced full materialization + client-side filtering/paging).
            var term = q.Trim().ToLower();
            query = query.Where(u =>
                (u.UserName != null && u.UserName.ToLower().Contains(term)) ||
                (u.Email != null && u.Email.ToLower().Contains(term)) ||
                (u.FirstName != null && u.FirstName.ToLower().Contains(term)) ||
                (u.LastName != null && u.LastName.ToLower().Contains(term)));
        }

        total = await query.CountAsync();
        users = await query
            .AsNoTracking()
            .OrderBy(u => u.UserName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var items = await ToUserDtosAsync(users);
        return Ok(new PagedResult<UserDto>(items, total, page, pageSize));
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest req)
    {
        if (DenyIfTokenIsElevatingToAdmin(req.Role) is { } denied) return denied;

        var provider = req.Provider ?? "Local";
        var user = new PortalUser
        {
            UserName = req.Username,
            Email = req.Email,
            FirstName = req.FirstName,
            LastName = req.LastName,
            IsActive = true,
            MustChangePassword = provider != "LDAP",
            Provider = provider
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
                true, user.MustChangePassword, user.CreatedAt, [req.Role], [], user.Provider, user.Version));
    }

    [HttpGet("users/{id:int}")]
    public async Task<IActionResult> GetUser(int id)
    {
        var user = await userManager.Users
            .Include(u => u.UserGroups).ThenInclude(ug => ug.Group)
            .FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();

        var roles = await userManager.GetRolesAsync(user);
        var groups = user.UserGroups.Select(ug => ug.Group.Name).ToList();
        OptimisticConcurrency.SetETag(Response, user.Version);
        return Ok(new UserDto(user.Id, user.UserName!, user.Email, user.FirstName, user.LastName,
            user.IsActive, user.MustChangePassword, user.CreatedAt, roles, groups, user.Provider, user.Version));
    }

    [HttpPut("users/{id:int}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserRequest req)
    {
        if (DenyIfTokenIsElevatingToAdmin(req.Role) is { } denied) return denied;

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null)
            return OptimisticConcurrency.MissingVersion(this);
        if (!OptimisticConcurrency.Prepare(db, user, expectedVersion.Value))
            return OptimisticConcurrency.Conflict(this, await ToUserDtoAsync(user));

        var wasActive = user.IsActive;
        var roleChanged = false;
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (req.Email is not null) user.Email = req.Email;
        if (req.FirstName is not null) user.FirstName = req.FirstName;
        if (req.LastName is not null) user.LastName = req.LastName;
        if (req.IsActive.HasValue) user.IsActive = req.IsActive.Value;

        if (req.Role is not null)
        {
            var currentRoles = await userManager.GetRolesAsync(user);
            roleChanged = currentRoles.Count != 1
                || !string.Equals(currentRoles[0], req.Role, StringComparison.OrdinalIgnoreCase);
            if (roleChanged)
            {
                await userManager.RemoveFromRolesAsync(user, currentRoles);
                await userManager.AddToRoleAsync(user, req.Role);
            }
        }

        // Staged before the final save so the mutation and its audit row share the transaction.
        audit.Stage(CurrentUserId, "UPDATE_USER", "User", id.ToString());
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            await transaction.RollbackAsync();
            if (result.Errors.Any(error => error.Code == "ConcurrencyFailure"))
            {
                await db.Entry(user).ReloadAsync();
                return OptimisticConcurrency.Conflict(this, await ToUserDtoAsync(user));
            }
            return BadRequest(new { errors = result.Errors.Select(error => error.Description) });
        }
        await transaction.CommitAsync();
        if (roleChanged || (req.IsActive.HasValue && req.IsActive.Value != wasActive))
        {
            await securitySessions.InvalidateUserAsync(id);
            if (roleChanged || req.IsActive == false)
                await securitySessions.RevokeAnonymousCapabilitiesAsync([id]);
        }
        OptimisticConcurrency.SetETag(Response, user.Version);
        return Ok(await ToUserDtoAsync(user));
    }

    [HttpPost("users/bulk-status")]
    public async Task<IActionResult> BulkUpdateUserStatus([FromBody] BulkUserStatusRequest req)
    {
        var items = (req.Users ?? []).GroupBy(item => item.Id).Select(group => group.First()).ToList();
        if (items.Count == 0) return BadRequest(new { error = "Select at least one user." });

        var results = new List<BulkMutationResult>();
        var updatedIds = new List<int>();
        // Fetch all targeted users in one query (was an N+1 — one round-trip per item). Entities stay
        // tracked; the per-user SaveChangesAsync below is intentional so one optimistic-concurrency
        // conflict does not fail the whole batch.
        var requestedIds = items.Select(item => item.Id).ToList();
        var usersById = await db.Users.Where(u => requestedIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id);
        foreach (var item in items)
        {
            if (!usersById.TryGetValue(item.Id, out var user))
            {
                results.Add(new(item.Id, "NotFound"));
                continue;
            }
            if (!OptimisticConcurrency.Prepare(db, user, item.Version))
            {
                results.Add(new(item.Id, "Conflict", user.Version));
                db.Entry(user).State = EntityState.Unchanged;
                continue;
            }

            user.IsActive = req.IsActive;
            try
            {
                await db.SaveChangesAsync();
                results.Add(new(item.Id, "Updated", user.Version));
                updatedIds.Add(user.Id);
            }
            catch (DbUpdateConcurrencyException)
            {
                await db.Entry(user).ReloadAsync();
                results.Add(new(item.Id, "Conflict", user.Version));
            }
        }

        await securitySessions.InvalidateUsersAsync(updatedIds);
        if (!req.IsActive)
            await securitySessions.RevokeAnonymousCapabilitiesAsync(updatedIds);
        await audit.LogAsync(CurrentUserId, "BULK_UPDATE_USER_STATUS", "User", null,
            $"{updatedIds.Count} users set active={req.IsActive}");
        return Ok(new { Updated = updatedIds.Count, Results = results });
    }

    [HttpDelete("users/{id:int}")]
    public async Task<IActionResult> DeleteUser(
        int id, [FromQuery] bool cascade = false, [FromQuery] int? reassignTo = null)
    {
        var user = await userManager.Users
            .Include(u => u.Subscriptions.Where(s => s.IsActive))
            .FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null)
            return OptimisticConcurrency.MissingVersion(this);
        if (!OptimisticConcurrency.Prepare(db, user, expectedVersion.Value))
            return OptimisticConcurrency.Conflict(this, await ToUserDtoAsync(user));

        if (user.Subscriptions.Any() && !cascade)
            return Conflict(new { error = "User has active subscriptions. Use ?cascade=true." });

        // Everything below — ownership transfer, token revocation, membership removal, the
        // Identity delete, and the staged audit rows — commits or fails as one transaction, so
        // the delete cannot succeed without its durable audit events (P1.6).
        await using var transaction = await db.Database.BeginTransactionAsync();

        // Durable shared resources (folders, reports, datasets) must be explicitly reassigned
        // before their owner disappears — otherwise ownership dangles and PRIVATE datasets
        // become reachable only by administrators.
        var ownedFolders = await db.Folders.CountAsync(f => f.OwnerId == id);
        var ownedReports = await db.Reports.CountAsync(r => r.CreatedBy == id);
        var ownedDatasets = await db.Datasets.CountAsync(d => d.CreatedBy == id);
        if (ownedFolders + ownedReports + ownedDatasets > 0)
        {
            if (reassignTo is null)
                return Conflict(new
                {
                    error = "User owns durable resources. Supply ?reassignTo=<userId> to transfer ownership.",
                    ownedFolders,
                    ownedReports,
                    ownedDatasets
                });

            var target = await db.Users.FirstOrDefaultAsync(u => u.Id == reassignTo && u.IsActive);
            if (target is null || target.Id == id)
                return BadRequest(new { error = "reassignTo must identify a different, active user." });

            // Bump versions so any concurrent edit holding the old version conflicts cleanly.
            await db.Folders.Where(f => f.OwnerId == id).ExecuteUpdateAsync(s => s
                .SetProperty(f => f.OwnerId, target.Id)
                .SetProperty(f => f.Version, f => f.Version + 1));
            await db.Reports.Where(r => r.CreatedBy == id).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.CreatedBy, target.Id)
                .SetProperty(r => r.Version, r => r.Version + 1));
            // Capture before the update, because afterwards these rows no longer match `id`.
            var transferredDatasetIds = await db.Datasets
                .Where(d => d.CreatedBy == id)
                .Select(d => d.Id)
                .ToListAsync();

            await db.Datasets.Where(d => d.CreatedBy == id).ExecuteUpdateAsync(s => s
                .SetProperty(d => d.CreatedBy, (int?)target.Id)
                .SetProperty(d => d.Version, d => d.Version + 1));

            // Dataset access follows grants, not CreatedBy, so transferring ownership has to move
            // the grant as well — otherwise the new owner inherits the record and none of the
            // access. The departing user's own grants disappear with their row (cascade), which is
            // the point: that is what makes deprovisioning actually revoke.
            if (transferredDatasetIds.Count > 0)
            {
                var alreadyGranted = await db.DatasetUserAcls
                    .Where(a => a.UserId == target.Id && transferredDatasetIds.Contains(a.DatasetId))
                    .Select(a => a.DatasetId)
                    .ToListAsync();

                foreach (var datasetId in transferredDatasetIds.Except(alreadyGranted))
                {
                    db.DatasetUserAcls.Add(new DatasetUserAcl
                    {
                        DatasetId = datasetId,
                        UserId = target.Id,
                        Permission = DatasetPermission.Owner
                    });
                }

                await db.SaveChangesAsync();
            }

            audit.Stage(CurrentUserId, "TRANSFER_OWNERSHIP", "User", id.ToString(),
                $"{ownedFolders} folder(s), {ownedReports} report(s), {ownedDatasets} dataset(s) → {target.UserName}");
        }

        // Personal artifacts (subscriptions, alerts, saved views, favorites, share/embed
        // capabilities, refresh tokens) die with the user. Their Orchestrator jobs and generated
        // trigger scripts are cleaned up after the commit (below); startup reconciliation
        // remains the recovery path if that cleanup is interrupted.
        var subscriptions = await db.Subscriptions
            .Include(s => s.Report)
            .Where(s => s.UserId == id)
            .Select(s => new { s.Id, ReportName = (string?)s.Report!.Name, s.ScriptPath })
            .ToListAsync();

        // Revoke all tokens and remove group memberships
        var tokens = await db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null).ToListAsync();
        foreach (var t in tokens) t.RevokedAt = DateTime.UtcNow;

        var memberships = await db.UserGroups.Where(ug => ug.UserId == id).ToListAsync();
        db.UserGroups.RemoveRange(memberships);

        audit.Stage(CurrentUserId, "DELETE_USER", "User", id.ToString(), user.UserName);
        await db.SaveChangesAsync();
        var deleteResult = await userManager.DeleteAsync(user);
        if (!deleteResult.Succeeded)
        {
            await transaction.RollbackAsync();
            if (deleteResult.Errors.Any(error => error.Code == "ConcurrencyFailure"))
                return Conflict(new { error = "The user changed after it was read." });
            return BadRequest(new { errors = deleteResult.Errors.Select(error => error.Description) });
        }
        await transaction.CommitAsync();

        // Post-commit cleanup of external artifacts (other SQLite DB + files): never performed
        // for a rolled-back delete, and healed by startup reconciliation if interrupted.
        if (subscriptions.Count > 0)
        {
            var orchDbPath = orchestratorDb.Resolve();
            IJobHistoryStore? jobStore = null;
            if (orchestratorStoreFactory.Provider == DatabaseProvider.Postgres || orchDbPath is not null)
            {
                jobStore = orchestratorStoreFactory.Create(orchDbPath);
                await jobStore.InitializeAsync();
            }

            foreach (var sub in subscriptions)
            {
                if (jobStore is not null)
                    await jobStore.DeleteJobAsync(SubscriptionOrchestration.JobName(sub.Id, sub.ReportName));
                if (!string.IsNullOrWhiteSpace(sub.ScriptPath)
                    && PortalPathGuard.TryResolveScript(config, sub.ScriptPath, out var script)
                    && System.IO.File.Exists(script))
                    System.IO.File.Delete(script);
            }
        }

        return NoContent();
    }

    // ── Admin password reset ──────────────────────────────────────────────────

    [HttpPost("users/{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, [FromBody] ResetPasswordRequest req)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null)
            return OptimisticConcurrency.MissingVersion(this);
        if (!OptimisticConcurrency.Prepare(db, user, expectedVersion.Value))
            return OptimisticConcurrency.Conflict(this, await ToUserDtoAsync(user));

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, req.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(error => error.Code == "ConcurrencyFailure"))
            {
                db.ChangeTracker.Clear();
                return OptimisticConcurrency.Conflict(
                    this, await ToUserDtoAsync((await userManager.FindByIdAsync(id.ToString()))!));
            }
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
        }

        user.MustChangePassword = true;
        // Staged so the flag write and the audit row share the Identity update's commit.
        audit.Stage(CurrentUserId, "RESET_PASSWORD", "User", id.ToString());
        await userManager.UpdateAsync(user);
        await securitySessions.InvalidateUserAsync(id);
        OptimisticConcurrency.SetETag(Response, user.Version);
        return NoContent();
    }

    [HttpPost("users/{id:int}/revoke-tokens")]
    public async Task<IActionResult> RevokeTokens(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null)
            return OptimisticConcurrency.MissingVersion(this);
        if (!OptimisticConcurrency.Prepare(db, user, expectedVersion.Value))
            return OptimisticConcurrency.Conflict(this, await ToUserDtoAsync(user));
        audit.Stage(CurrentUserId, "REVOKE_TOKENS", "User", id.ToString());
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return OptimisticConcurrency.Conflict(
                this, await ToUserDtoAsync((await db.Users.FindAsync(id))!));
        }
        await securitySessions.InvalidateUserAsync(id);
        OptimisticConcurrency.SetETag(Response, user.Version);
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
        await securitySessions.InvalidateUserAsync(id);
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
                g.UserGroups.Count, g.Provider, g.AdGroup, g.Version))
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
                g.UserGroups.Count, g.Provider, g.AdGroup, g.Version))
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
            new GroupDto(group.Id, group.Name, group.Description, 0, group.Provider, group.AdGroup, group.Version));
    }

    [HttpGet("groups/{id:int}")]
    public async Task<IActionResult> GetGroup(int id)
    {
        var group = await db.Groups
            .Where(g => g.Id == id)
            .Select(g => new GroupDto(g.Id, g.Name, g.Description, g.UserGroups.Count, g.Provider, g.AdGroup, g.Version))
            .FirstOrDefaultAsync();
        if (group is null) return NotFound();
        OptimisticConcurrency.SetETag(Response, group.Version);
        return Ok(group);
    }

    [HttpPut("groups/{id:int}")]
    public async Task<IActionResult> UpdateGroup(int id, [FromBody] UpdateGroupRequest req)
    {
        var group = await db.Groups.FindAsync(id);
        if (group is null) return NotFound();
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null)
            return OptimisticConcurrency.MissingVersion(this);
        if (!OptimisticConcurrency.Prepare(db, group, expectedVersion.Value))
            return OptimisticConcurrency.Conflict(
                this, ToGroupDto(group, await db.UserGroups.CountAsync(value => value.GroupId == id)));

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

        audit.Stage(CurrentUserId, "UPDATE_GROUP", "Group", id.ToString());
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await db.Entry(group).ReloadAsync();
            return OptimisticConcurrency.Conflict(
                this, ToGroupDto(group, await db.UserGroups.CountAsync(value => value.GroupId == id)));
        }
        OptimisticConcurrency.SetETag(Response, group.Version);
        return Ok(ToGroupDto(group, await db.UserGroups.CountAsync(value => value.GroupId == id)));
    }

    [HttpDelete("groups/{id:int}")]
    public async Task<IActionResult> DeleteGroup(int id, [FromQuery] bool cascade = false)
    {
        var group = await db.Groups
            .Include(g => g.UserGroups)
            .Include(g => g.FolderAcls)
            .FirstOrDefaultAsync(g => g.Id == id);
        if (group is null) return NotFound();
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null)
            return OptimisticConcurrency.MissingVersion(this);
        if (!OptimisticConcurrency.Prepare(db, group, expectedVersion.Value))
            return OptimisticConcurrency.Conflict(this, ToGroupDto(group, group.UserGroups.Count));

        bool hasEntries = group.UserGroups.Any() || group.FolderAcls.Any();
        if (hasEntries && !cascade)
            return Conflict(new { error = "Group has members or ACL entries. Use ?cascade=true." });

        var affectedUserIds = group.UserGroups.Select(ug => ug.UserId).ToList();
        db.UserGroups.RemoveRange(group.UserGroups);
        db.FolderAcls.RemoveRange(group.FolderAcls);
        db.Groups.Remove(group);
        audit.Stage(CurrentUserId, "DELETE_GROUP", "Group", id.ToString(), group.Name);
        await db.SaveChangesAsync();
        await securitySessions.InvalidateUsersAsync(affectedUserIds);
        return NoContent();
    }

    [HttpPost("groups/bulk-delete")]
    public async Task<IActionResult> BulkDeleteGroups([FromBody] BulkDeleteGroupsRequest req)
    {
        var items = (req.Groups ?? []).GroupBy(item => item.Id).Select(group => group.First()).ToList();
        if (items.Count == 0) return BadRequest(new { error = "Select at least one group." });

        var results = new List<BulkMutationResult>();
        var affectedUserIds = new HashSet<int>();
        var deleted = 0;
        // Load every targeted group (with the navigations the delete touches) in one split query — was
        // an N+1 that re-queried each group inside the loop. Entities stay tracked; the per-group
        // SaveChangesAsync below is intentional so one optimistic-concurrency conflict does not fail the
        // whole batch. AsSplitQuery avoids a cartesian explosion across the two collection includes.
        var requestedIds = items.Select(item => item.Id).ToList();
        var groupsById = await db.Groups
            .Include(value => value.UserGroups)
            .Include(value => value.FolderAcls)
            .Where(value => requestedIds.Contains(value.Id))
            .AsSplitQuery()
            .ToDictionaryAsync(value => value.Id);
        foreach (var item in items)
        {
            if (!groupsById.TryGetValue(item.Id, out var group))
            {
                results.Add(new(item.Id, "NotFound"));
                continue;
            }
            if (!req.Cascade && (group.UserGroups.Any() || group.FolderAcls.Any()))
            {
                results.Add(new(item.Id, "Blocked", group.Version, "Group has members or ACL entries."));
                continue;
            }
            if (!OptimisticConcurrency.Prepare(db, group, item.Version))
            {
                results.Add(new(item.Id, "Conflict", group.Version));
                continue;
            }

            foreach (var userId in group.UserGroups.Select(value => value.UserId))
                affectedUserIds.Add(userId);
            db.UserGroups.RemoveRange(group.UserGroups);
            db.FolderAcls.RemoveRange(group.FolderAcls);
            db.Groups.Remove(group);
            try
            {
                await db.SaveChangesAsync();
                results.Add(new(item.Id, "Deleted"));
                deleted++;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Detach only this group's graph: after a failed save it stays tracked as Deleted and
                // would otherwise leak into the next group's SaveChanges. Clearing the whole
                // ChangeTracker (the old single-query approach) would detach the rest of the batch.
                db.Entry(group).State = EntityState.Detached;
                foreach (var membership in group.UserGroups) db.Entry(membership).State = EntityState.Detached;
                foreach (var acl in group.FolderAcls) db.Entry(acl).State = EntityState.Detached;
                var currentVersion = await db.Groups
                    .Where(value => value.Id == item.Id)
                    .Select(value => (long?)value.Version)
                    .FirstOrDefaultAsync();
                results.Add(new(item.Id, currentVersion is null ? "NotFound" : "Conflict", currentVersion));
            }
        }
        await securitySessions.InvalidateUsersAsync(affectedUserIds);
        await audit.LogAsync(CurrentUserId, "BULK_DELETE_GROUPS", "Group", null, $"{deleted} groups deleted");
        return Ok(new { Deleted = deleted, Results = results });
    }

    // ── Departmental environments ────────────────────────────────────────────

    /// <summary>
    /// Generates a deployment plan for a departmental environment: every isolated resource, port,
    /// and key requirement derived from the environment id.
    ///
    /// The Portal generates plans and never applies them. Creating databases, accounts, key rings
    /// and endpoints belongs to a separately authorized deployment plane, because an environment
    /// able to provision another is not isolated from it. The plan is also secret-free: keys appear
    /// as requirements at named configuration keys and are never generated here.
    /// </summary>
    [HttpGet("environments/plan")]
    public IActionResult GetEnvironmentPlan(
        [FromServices] EnvironmentPlanService environments,
        [FromQuery] string environmentId,
        [FromQuery] int portBase = 5000)
    {
        if (!EnvironmentPlanService.IsValidEnvironmentId(environmentId))
        {
            return BadRequest(new
            {
                error = "environmentId must be a short lowercase DNS-safe token (a-z, 0-9, hyphen), "
                    + "because it becomes hostnames, account names, and paths."
            });
        }

        return Ok(environments.GeneratePlan(environmentId, portBase));
    }

    /// <summary>
    /// Checks a proposed environment against what this Portal can see — its own environment, the
    /// environments named for fleet visibility, and the machine registry. Any shared resource is a
    /// collision rather than a warning: sharing one is enough to break isolation.
    /// </summary>
    [HttpPost("environments/validate")]
    public async Task<IActionResult> ValidateEnvironmentPlan(
        [FromServices] EnvironmentPlanService environments,
        [FromBody] ValidateEnvironmentPlanRequest request,
        CancellationToken ct)
    {
        if (!EnvironmentPlanService.IsValidEnvironmentId(request.EnvironmentId ?? ""))
            return BadRequest(new { error = "environmentId must be a short lowercase DNS-safe token." });

        var plan = environments.GeneratePlan(request.EnvironmentId!, request.PortBase ?? 0);
        var validation = await environments.ValidateAsync(plan, ct);

        await audit.LogAsync(CurrentUserId, "VALIDATE_ENVIRONMENT_PLAN", "Environment",
            request.EnvironmentId,
            validation.IsValid ? "no collisions" : $"{validation.Collisions.Count} collision(s)");

        return Ok(validation);
    }

    /// <summary>
    /// This environment measured against the isolation contract, and the link to read-only fleet
    /// status. Resources the process cannot observe from inside — a shared database login, a shared
    /// OS account — are reported as unknown rather than assumed isolated.
    /// </summary>
    [HttpGet("environments/current")]
    public IActionResult GetCurrentEnvironment([FromServices] EnvironmentPlanService environments) =>
        Ok(environments.DescribeCurrent());

    // ── Support bundle ───────────────────────────────────────────────────────

    /// <summary>
    /// The support bundle as a reviewable document: every section an operator would send, with the
    /// redaction note and — explicitly — what it leaves out. A support artifact that does not say
    /// what it omitted invites the assumption that it omitted nothing.
    ///
    /// Reviewing is the whole point of splitting this from the download. The CLI's
    /// <c>admin support-bundle</c> remains the recovery path for when the Portal is unavailable; it
    /// reads files and host configuration this cannot.
    /// </summary>
    [HttpGet("support-bundle/review")]
    public async Task<IActionResult> ReviewSupportBundle(
        [FromServices] PortalSupportBundleService bundle, CancellationToken ct)
    {
        var content = await bundle.BuildAsync(ct);
        await audit.LogAsync(CurrentUserId, "REVIEW_SUPPORT_BUNDLE", "System", null,
            $"{content.Sections.Count} section(s); content={content.ContentHash}");
        return Ok(content);
    }

    /// <summary>
    /// Downloads the bundle. Pass <paramref name="acknowledgedContent"/> with the hash from
    /// <c>support-bundle/review</c> to make the review binding: a stale hash means the deployment
    /// changed after it was reviewed, and the download is refused rather than handing over something
    /// other than what was approved.
    /// </summary>
    [HttpGet("support-bundle")]
    public async Task<IActionResult> DownloadSupportBundle(
        [FromServices] PortalSupportBundleService bundle,
        [FromQuery] string? acknowledgedContent = null,
        CancellationToken ct = default)
    {
        var content = await bundle.BuildAsync(ct);

        if (!string.IsNullOrWhiteSpace(acknowledgedContent)
            && !string.Equals(acknowledgedContent, content.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            await audit.LogAsync(CurrentUserId, "DOWNLOAD_SUPPORT_BUNDLE_REFUSED", "System", null,
                $"acknowledged {acknowledgedContent} is stale; current is {content.ContentHash}");
            return Conflict(new
            {
                error = "The deployment changed after the bundle was reviewed.",
                acknowledgedContent,
                currentContent = content.ContentHash
            });
        }

        await audit.LogAsync(CurrentUserId, "DOWNLOAD_SUPPORT_BUNDLE", "System", null,
            $"content={content.ContentHash}; "
            + (string.IsNullOrWhiteSpace(acknowledgedContent) ? "no review acknowledged" : "review acknowledged"));

        var json = System.Text.Json.JsonSerializer.Serialize(content,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        return File(Encoding.UTF8.GetBytes(json), "application/json",
            $"etl-sql-portal-support-{DateTime.UtcNow:yyyyMMdd_HHmm}.json");
    }

    // ── Dataset at-rest key posture ──────────────────────────────────────────

    /// <summary>
    /// Dataset at-rest key inventory, rotation preflight, verification, and rollback guidance.
    ///
    /// Rotation itself is <c>POST datasets/rotate-at-rest-key</c> and reports what it did. This is
    /// everything around it: which key version each cache is on, what would rotate, and — the part
    /// worth knowing beforehand — which datasets <em>cannot</em>, because a cache encrypted under a
    /// version whose key is no longer configured can be neither rotated nor read.
    ///
    /// Key versions are non-secret identifiers and are named. Key material never appears: a key is
    /// reported as configured or not.
    /// </summary>
    [HttpGet("datasets/at-rest-key/posture")]
    public async Task<IActionResult> GetDatasetKeyPosture(
        [FromServices] DatasetKeyPostureService posture, CancellationToken ct) =>
        Ok(await posture.BuildAsync(ct));

    // ── Secret and connection posture ────────────────────────────────────────

    /// <summary>
    /// Secrets and shared connections resolved against each other: which connections reference which
    /// secrets, which references do not resolve, when each secret was last rotated, and which the
    /// target of a promotion would have to be given.
    ///
    /// The failure this exists for is invisible on either page alone — a connection referencing a
    /// secret that was renamed, disabled, or never created looks healthy in both lists and fails the
    /// first time something runs. No secret value is read to build it.
    /// </summary>
    [HttpGet("credentials/posture")]
    public async Task<IActionResult> GetCredentialPosture(
        [FromServices] CredentialPostureService posture, CancellationToken ct) =>
        Ok(await posture.BuildAsync(ct));

    // ── Recovery and host-identity posture ───────────────────────────────────

    /// <summary>
    /// Backup freshness, restore-drill evidence, and host enrolment consistency — read-only.
    ///
    /// Backup custody, the restore itself, and host enrolment all stay outside the running Portal:
    /// they own key material and an OS-protected bootstrap that the Portal deliberately does not
    /// have. What the Portal can do is notice the evidence is missing, stale, or inconsistent, and
    /// say what to run to fix it.
    /// </summary>
    [HttpGet("operations/posture")]
    public async Task<IActionResult> GetOperationsPosture(
        [FromServices] OperationsPostureService posture, CancellationToken ct) =>
        Ok(await posture.BuildAsync(ct));

    // ── Audit collector health ───────────────────────────────────────────────

    /// <summary>
    /// Durable remote audit delivery, for an operator mid-incident: queue depth and age, terminal
    /// failures, last attempt and last success, the thresholds a reading is compared against, and
    /// whether the fail-closed policy is currently refusing mutations. The collector endpoint is
    /// reported without its query string, which can carry a token.
    /// </summary>
    [HttpGet("audit/collector")]
    public async Task<IActionResult> GetAuditCollectorHealth(
        [FromServices] AuditCollectorHealthService health, CancellationToken ct) =>
        Ok(await health.BuildAsync(ct));

    /// <summary>
    /// Posts a synthetic event to the configured collector through the real delivery path and
    /// reports the outcome. The probe carries no audit content, so a misconfigured endpoint receives
    /// nothing of consequence — and the attempt is itself audited, because reaching out to a
    /// configured external endpoint on demand is an operator action worth recording.
    /// </summary>
    [HttpPost("audit/collector/test-delivery")]
    public async Task<IActionResult> TestAuditCollectorDelivery(
        [FromServices] AuditOutboxTransportService transport, CancellationToken ct)
    {
        var result = await transport.SendTestDeliveryAsync(ct);
        await audit.LogAsync(CurrentUserId, "TEST_AUDIT_COLLECTOR_DELIVERY", "AuditCollector",
            result.Endpoint, result.Delivered ? "delivered" : $"failed: {result.Error}");
        return Ok(result);
    }

    // ── Identity-provider diagnostics ────────────────────────────────────────

    /// <summary>
    /// Identity-provider health: OIDC reachability and configuration findings, LDAP configuration,
    /// what claim value each provider-managed group expects, whether federated users are landing in
    /// groups, and whether anyone could still administer this Portal with the provider unreachable.
    /// Configured secrets appear as presence flags, never values.
    /// </summary>
    [HttpGet("identity/diagnostics")]
    public async Task<IActionResult> GetIdentityDiagnostics(
        [FromServices] IdentityDiagnosticsService diagnostics, CancellationToken ct) =>
        Ok(await diagnostics.BuildAsync(ct));

    /// <summary>
    /// Resolves claim values against the configured group mappings without anyone signing in, so a
    /// mapping can be checked before a user discovers it is wrong by not having the access they
    /// expected. Reads only; grants nothing.
    /// </summary>
    [HttpPost("identity/diagnostics/group-mapping-test")]
    public async Task<IActionResult> TestGroupMapping(
        [FromBody] GroupMappingTestRequest request,
        [FromServices] IdentityDiagnosticsService diagnostics,
        CancellationToken ct) =>
        Ok(await diagnostics.TestGroupMappingAsync(request.ClaimValues ?? [], ct));

    // ── Access simulator ─────────────────────────────────────────────────────

    /// <summary>
    /// Explains one identity's access across every authority that composes into it: roles, groups,
    /// folder and report ACLs, shared-connection grants, Studio capability, and row-level security.
    ///
    /// It answers <em>why</em>, and it never returns data. Row-level security is explained by naming
    /// the identity the report filters on and the values that would be bound for this user; the
    /// report is not run. A tool for auditing who can see data must not become a way to see it.
    /// </summary>
    [HttpGet("access-simulator/user/{userId:int}")]
    public async Task<IActionResult> SimulateAccess(
        int userId,
        [FromServices] AccessSimulationService simulator,
        [FromQuery] int? reportId = null,
        [FromQuery] int? datasetId = null,
        CancellationToken ct = default)
    {
        var simulation = await simulator.SimulateAsync(userId, reportId, datasetId, ct);
        if (simulation is null) return NotFound();

        // Reading someone else's effective access is itself a privileged act.
        await audit.LogAsync(CurrentUserId, "SIMULATE_ACCESS", "User", userId.ToString(),
            reportId is null && datasetId is null
                ? "identity only"
                : $"report={reportId?.ToString() ?? "-"}; dataset={datasetId?.ToString() ?? "-"}");

        return Ok(simulation);
    }

    // ── Studio capabilities granted to a group ───────────────────────────────

    [HttpGet("groups/{id:int}/studio-capabilities")]
    public async Task<IActionResult> GetGroupStudioCapabilities(
        int id, [FromServices] StudioCapabilityStore capabilities, CancellationToken ct)
    {
        if (!await db.Groups.AnyAsync(g => g.Id == id, ct)) return NotFound();
        return Ok(new GroupStudioCapabilitiesDto(
            id,
            await capabilities.ResolveForGroupAsync(id, ct),
            [.. StudioCapabilities.All.OrderBy(value => value, StringComparer.Ordinal)]));
    }

    /// <summary>
    /// Replaces a group's Studio capabilities.
    ///
    /// Capabilities are carried as token claims, so a change here does not reach a session that
    /// already exists — members are signed out, exactly as an ACL change does, rather than keeping
    /// authority the administrator has just taken away.
    /// </summary>
    [HttpPut("groups/{id:int}/studio-capabilities")]
    public async Task<IActionResult> SetGroupStudioCapabilities(
        int id,
        [FromBody] SetGroupStudioCapabilitiesRequest request,
        [FromServices] StudioCapabilityStore capabilities,
        CancellationToken ct)
    {
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null) return NotFound();

        var unknown = await capabilities.ReplaceForGroupAsync(id, request.Capabilities ?? [], ct);
        if (unknown.Count > 0)
        {
            return BadRequest(new
            {
                error = "Unknown Studio capability.",
                unknown,
                allowed = StudioCapabilities.All.OrderBy(value => value, StringComparer.Ordinal)
            });
        }

        var granted = await capabilities.ResolveForGroupAsync(id, ct);
        audit.Stage(CurrentUserId, "SET_GROUP_STUDIO_CAPABILITIES", "Group", id.ToString(),
            $"{group.Name} -> {(granted.Count == 0 ? "none" : string.Join(' ', granted))}");
        await db.SaveChangesAsync(ct);
        await securitySessions.InvalidateGroupMembersAsync(id);

        return Ok(new GroupStudioCapabilitiesDto(
            id, granted, [.. StudioCapabilities.All.OrderBy(value => value, StringComparer.Ordinal)]));
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
            var allUsers = await query.ToListAsync();
            var term = q.Trim();
            var matchedUsers = allUsers.Where(u =>
                (u.UserName != null && u.UserName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (u.Email != null && u.Email.Contains(term, StringComparison.OrdinalIgnoreCase))
            ).ToList();

            var total = matchedUsers.Count;
            var items = matchedUsers
                .OrderBy(u => u.UserName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(u => new GroupMemberDto(u.Id, u.UserName!, u.Email, u.IsActive))
                .ToList();
            return Ok(new PagedResult<GroupMemberDto>(items, total, page, pageSize));
        }
        else
        {
            var total = await query.CountAsync();
            var items = await query
                .OrderBy(u => u.UserName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(u => new GroupMemberDto(u.Id, u.UserName!, u.Email, u.IsActive))
                .ToListAsync();
            return Ok(new PagedResult<GroupMemberDto>(items, total, page, pageSize));
        }
    }

    [HttpPost("groups/{id:int}/members")]
    public async Task<IActionResult> AddMember(int id, [FromBody] AddUserToGroupRequest req)
    {
        var group = await db.Groups.FindAsync(id);
        if (group is null) return NotFound("Group not found");
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null) return OptimisticConcurrency.MissingVersion(this);
        if (!OptimisticConcurrency.Prepare(db, group, expectedVersion.Value))
            return OptimisticConcurrency.Conflict(
                this, ToGroupDto(group, await db.UserGroups.CountAsync(value => value.GroupId == id)));

        PortalUser? user = req.UserId.HasValue
            ? await userManager.FindByIdAsync(req.UserId.Value.ToString())
            : req.Username is not null ? await userManager.FindByNameAsync(req.Username) : null;

        if (user is null) return NotFound("User not found");

        if (await db.UserGroups.AnyAsync(ug => ug.UserId == user.Id && ug.GroupId == id))
            return Conflict(new { error = "User is already a member" });

        db.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = id });
        audit.Stage(CurrentUserId, "ADD_USER_TO_GROUP", "Group", id.ToString(), user.UserName);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return OptimisticConcurrency.Conflict(
                this, ToGroupDto((await db.Groups.FindAsync(id))!,
                    await db.UserGroups.CountAsync(value => value.GroupId == id)));
        }
        await securitySessions.InvalidateUserAsync(user.Id);
        OptimisticConcurrency.SetETag(Response, group.Version);
        return Ok(new { group.Version });
    }

    [HttpPost("groups/{id:int}/members/bulk-add")]
    public async Task<IActionResult> BulkAddMembers(int id, [FromBody] BulkGroupMembershipRequest req)
    {
        var group = await db.Groups.FindAsync(id);
        if (group is null) return NotFound("Group not found");
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null) return OptimisticConcurrency.MissingVersion(this);
        if (!OptimisticConcurrency.Prepare(db, group, expectedVersion.Value))
            return OptimisticConcurrency.Conflict(
                this, ToGroupDto(group, await db.UserGroups.CountAsync(value => value.GroupId == id)));
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
        audit.Stage(CurrentUserId, "BULK_ADD_USERS_TO_GROUP", "Group", id.ToString(),
            $"{validIds.Count} users added");
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return OptimisticConcurrency.Conflict(
                this, ToGroupDto((await db.Groups.FindAsync(id))!,
                    await db.UserGroups.CountAsync(value => value.GroupId == id)));
        }
        await securitySessions.InvalidateUsersAsync(validIds);
        OptimisticConcurrency.SetETag(Response, group.Version);
        return Ok(new { Added = validIds.Count, group.Version });
    }

    [HttpPost("groups/{id:int}/members/bulk-remove")]
    public async Task<IActionResult> BulkRemoveMembers(int id, [FromBody] BulkGroupMembershipRequest req)
    {
        var group = await db.Groups.FindAsync(id);
        if (group is null) return NotFound("Group not found");
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null) return OptimisticConcurrency.MissingVersion(this);
        if (!OptimisticConcurrency.Prepare(db, group, expectedVersion.Value))
            return OptimisticConcurrency.Conflict(
                this, ToGroupDto(group, await db.UserGroups.CountAsync(value => value.GroupId == id)));
        var ids = req.UserIds.Distinct().ToList();
        if (ids.Count == 0) return BadRequest(new { error = "Select at least one user." });

        var memberships = await db.UserGroups
            .Where(ug => ug.GroupId == id && ids.Contains(ug.UserId))
            .ToListAsync();
        db.UserGroups.RemoveRange(memberships);
        audit.Stage(CurrentUserId, "BULK_REMOVE_USERS_FROM_GROUP", "Group", id.ToString(),
            $"{memberships.Count} users removed");
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return OptimisticConcurrency.Conflict(
                this, ToGroupDto((await db.Groups.FindAsync(id))!,
                    await db.UserGroups.CountAsync(value => value.GroupId == id)));
        }
        await securitySessions.InvalidateUsersAsync(memberships.Select(membership => membership.UserId));
        OptimisticConcurrency.SetETag(Response, group.Version);
        return Ok(new { Removed = memberships.Count, group.Version });
    }

    [HttpDelete("groups/{id:int}/members/{userId:int}")]
    public async Task<IActionResult> RemoveMember(int id, int userId)
    {
        var group = await db.Groups.FindAsync(id);
        if (group is null) return NotFound("Group not found");
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null) return OptimisticConcurrency.MissingVersion(this);
        if (!OptimisticConcurrency.Prepare(db, group, expectedVersion.Value))
            return OptimisticConcurrency.Conflict(
                this, ToGroupDto(group, await db.UserGroups.CountAsync(value => value.GroupId == id)));
        var ug = await db.UserGroups.FirstOrDefaultAsync(x => x.GroupId == id && x.UserId == userId);
        if (ug is null) return NotFound();

        db.UserGroups.Remove(ug);
        audit.Stage(CurrentUserId, "REMOVE_USER_FROM_GROUP", "Group", id.ToString());
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return OptimisticConcurrency.Conflict(
                this, ToGroupDto((await db.Groups.FindAsync(id))!,
                    await db.UserGroups.CountAsync(value => value.GroupId == id)));
        }
        await securitySessions.InvalidateUserAsync(userId);
        OptimisticConcurrency.SetETag(Response, group.Version);
        return Ok(new { group.Version });
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
                r.UpdatedAt,
                r.Version
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

        // Studio authority is a separate axis from resource permission: folder Manage does not imply
        // the right to publish, commit, or push. Answering "why can they do that?" from one place
        // means reporting both, plus the deployment mode that can make every capability moot.
        var roles = (await userManager.GetRolesAsync(user)).OrderBy(r => r, StringComparer.Ordinal).ToList();
        var studio = HttpContext.RequestServices.GetRequiredService<StudioAuthorizationService>();

        return Ok(new EffectiveUserPermissionsDto(
            user.Id,
            user.UserName!,
            groupNames,
            folderEntries,
            reports,
            roles,
            studio.Mode.ToString(),
            studio.Mode == StudioDeploymentMode.Disabled ? [] : studio.EffectiveCapabilitiesForRoles(roles)));
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

    // ── Operational metrics (P2.8) ─────────────────────────────────────────────

    /// <summary>Point-in-time operational snapshot: active/queued executions, recent execution and
    /// delivery failure rates, and dataset/snapshot storage usage (admin-only).</summary>
    [HttpGet("metrics/operational")]
    public async Task<IActionResult> GetOperationalMetrics(
        [FromServices] OperationalMetricsService metrics)
        => Ok(await metrics.GetAsync(HttpContext.RequestAborted));

    // ── Usage metrics ────────────────────────────────────────────────────────

    [HttpGet("metrics/usage")]
    public async Task<IActionResult> GetUsageMetrics([FromQuery] int days = 30)
    {
        days = Math.Clamp(days, 1, 366);
        var since = DateTime.UtcNow.AddDays(-days);
        await deliveryStatus.SynchronizeAllAsync();

        var viewLogs = await db.AuditLogs
            .AsNoTracking()
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
            .AsNoTracking()
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
        [FromQuery] int? userId = null,
        [FromQuery] string? resourceType = null,
        [FromQuery] string? resourceId = null)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = db.AuditLogs.AsQueryable();
        if (action is not null) query = query.Where(a => a.Action == action);
        if (userId.HasValue) query = query.Where(a => a.UserId == userId);
        if (!string.IsNullOrWhiteSpace(resourceType)) query = query.Where(a => a.ResourceType == resourceType);
        if (!string.IsNullOrWhiteSpace(resourceId)) query = query.Where(a => a.ResourceId == resourceId);

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
                      a.Action, a.ResourceType, a.ResourceId, a.Timestamp, a.Detail, a.CorrelationId,
                      a.StudioCapability))
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
        if (action is not null) query = query.Where(a => a.Action == action);
        if (userId.HasValue) query = query.Where(a => a.UserId == userId);

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
        sb.AppendLine("Id,Timestamp,UserId,Username,Action,ResourceType,ResourceId,Detail,CorrelationId");
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
                CsvField(row.a.Detail),
                CsvField(row.a.CorrelationId)));
        }

        var filename = $"audit_log_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv";
        return File(
            Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(),
            "text/csv; charset=utf-8",
            filename);
    }

    // ── Configuration export (P1.7) ───────────────────────────────────────────

    /// <summary>
    /// The export plan: what leaves this Portal, what will not, and what must be moved separately —
    /// without the script body.
    ///
    /// The export endpoint already returned all of this in its audit line and none of it to the
    /// caller, so the only way to find out what an export omitted was to read the file. Reviewing
    /// before downloading is the point: the skipped list and the content manifest are the parts that
    /// silently do not arrive at the target.
    /// </summary>
    [HttpGet("configuration/export/plan")]
    public async Task<IActionResult> GetConfigurationExportPlan(
        [FromServices] ConfigurationExportService exporter,
        [FromQuery] string? orchestratorAlias = null)
    {
        var export = await exporter.GenerateAsync(orchestratorAlias, HttpContext.RequestAborted);
        return Ok(new
        {
            tenantExportIdentity = config.TenantId,
            planHash = ComputeExportPlanHash(export, config.TenantId),
            emitted = export.Emitted,
            requiredSecrets = export.RequiredSecrets,
            skipped = export.Skipped,
            contentManifest = export.ContentManifest,
            note = "Secrets are emitted as placeholders and never exported. Items under 'skipped' and "
                + "'contentManifest' do not travel in the script and must be handled separately."
        });
    }

    /// <summary>Generates the declarative configuration bootstrap script (admin-only). Secrets
    /// are emitted as ${...} placeholders and unsupported resources are listed in the summary.
    ///
    /// Pass <paramref name="acknowledgedPlan"/> with the hash from <c>configuration/export/plan</c>
    /// to make review binding: a stale hash means the configuration changed after it was reviewed,
    /// and the download is refused rather than handing over something different from what was
    /// approved. The audit records either the acknowledged plan or that none was.</summary>
    [HttpGet("configuration/export")]
    public async Task<IActionResult> ExportConfiguration(
        [FromServices] ConfigurationExportService exporter,
        [FromQuery] string? orchestratorAlias = null,
        [FromQuery] string? acknowledgedPlan = null)
    {
        var export = await exporter.GenerateAsync(orchestratorAlias, HttpContext.RequestAborted);
        var planHash = ComputeExportPlanHash(export, config.TenantId);

        if (!string.IsNullOrWhiteSpace(acknowledgedPlan)
            && !string.Equals(acknowledgedPlan, planHash, StringComparison.OrdinalIgnoreCase))
        {
            await audit.LogAsync(CurrentUserId, "EXPORT_PORTAL_CONFIGURATION_REFUSED", "System", null,
                $"acknowledged plan {acknowledgedPlan} is stale; current plan is {planHash}");
            return Conflict(new
            {
                error = "The configuration changed after the plan was reviewed.",
                acknowledgedPlan,
                currentPlan = planHash
            });
        }

        await audit.LogAsync(CurrentUserId, "EXPORT_PORTAL_CONFIGURATION", "System", null,
            $"{export.RequiredSecrets.Count} secret placeholder(s), {export.Skipped.Count} skipped item(s), " +
            $"{export.ContentManifest.Count} content item(s); plan={planHash}; " +
            (string.IsNullOrWhiteSpace(acknowledgedPlan) ? "no plan acknowledged" : "plan acknowledged"));
        return File(
            Encoding.UTF8.GetBytes(export.Script),
            "text/plain; charset=utf-8",
            $"portal_bootstrap_{DateTime.UtcNow:yyyyMMdd_HHmm}.etlsql.txt");
    }

    /// <summary>
    /// Identifies exactly what a reviewer saw. Derived from the plan contents rather than the script
    /// text, so cosmetic churn does not invalidate a review while a real change to what would be
    /// promoted always does.
    /// </summary>
    private static string ComputeExportPlanHash(
        ConfigurationExportService.ExportResult export, string? tenantExportIdentity)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            tenantExportIdentity,
            export.Emitted,
            export.RequiredSecrets,
            export.Skipped,
            contentManifest = export.ContentManifest.Select(item => $"{item.Kind}:{item.Logical}:{item.Action}")
        });
        return Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(payload)))[..16].ToLowerInvariant();
    }

    // ── Orchestrator connection settings ──────────────────────────────────────

    [HttpGet("settings/orchestrator")]
    public IActionResult GetOrchestratorSettings(
        [FromServices] ETL_SQL.Portal.Services.OrchestratorSettingsService settings)
    {
        return Ok(new
        {
            ApiUrl = settings.ApiUrl,
            HasApiKey = !string.IsNullOrEmpty(settings.ApiKey)
        });
    }

    [HttpPut("settings/orchestrator")]
    public async Task<IActionResult> UpdateOrchestratorSettings(
        [FromServices] ETL_SQL.Portal.Services.OrchestratorSettingsService settings,
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
        [FromServices] ETL_SQL.Portal.Services.PortalBrandingSettingsService branding)
    {
        return Ok(branding.ToDto());
    }

    [HttpPut("settings/branding")]
    public async Task<IActionResult> UpdateBrandingSettings(
        [FromServices] ETL_SQL.Portal.Services.PortalBrandingSettingsService branding,
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
