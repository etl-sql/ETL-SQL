using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Explains what one identity can reach and why, across every authority that composes into an
/// answer: roles, groups, folder and report ACLs, shared-connection grants, Studio capability, and
/// row-level security.
///
/// Those authorities are individually queryable already, which is exactly the problem — reconstructing
/// "why can this person open that report, and what would they see in it?" meant checking five
/// surfaces and holding the composition in your head. Getting it wrong in the safe direction wastes
/// an afternoon; getting it wrong in the other direction is how someone keeps access nobody meant
/// them to have.
///
/// <b>It never returns data.</b> Row-level security is explained by naming the identity the report
/// filters on and the values that would be bound for this user — never by running the report. A tool
/// for auditing who can see data must not itself become a way to see it.
/// </summary>
public sealed class AccessSimulationService(
    PortalDbContext db,
    UserManager<PortalUser> users,
    FolderPermissionService folderPermissions,
    DatasetPermissionService datasetPermissions,
    PortalConnectionCatalogService connections,
    StudioAuthorizationService studio,
    StudioCapabilityStore studioCapabilities,
    PortalConfig config,
    DatasetTenantScope datasetScope)
{
    public async Task<AccessSimulationDto?> SimulateAsync(
        int userId, int? reportId, int? datasetId, CancellationToken ct = default)
    {
        var user = await users.Users
            .Include(u => u.UserGroups).ThenInclude(ug => ug.Group)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return null;

        var roles = (await users.GetRolesAsync(user)).OrderBy(r => r, StringComparer.Ordinal).ToList();
        var groupIds = user.UserGroups.Select(ug => ug.GroupId).ToHashSet();
        var groupNames = user.UserGroups.Select(ug => ug.Group.Name)
            .OrderBy(n => n, StringComparer.Ordinal).ToList();
        var isAdmin = roles.Contains("Admin", StringComparer.OrdinalIgnoreCase);

        var identity = new AccessSimulationIdentityDto(
            user.Id, user.UserName!, user.IsActive, isAdmin, roles, groupNames);

        return new AccessSimulationDto(
            identity,
            await BuildStudioAsync(roles, userId, ct),
            await BuildConnectionsAsync(user, isAdmin, ct),
            reportId is int rid ? await BuildReportAsync(rid, user, groupIds, isAdmin, ct) : null,
            datasetId is int did ? await BuildDatasetAsync(did, userId, isAdmin, groupIds, ct) : null);
    }

    private async Task<AccessSimulationStudioDto> BuildStudioAsync(
        IReadOnlyList<string> roles, int userId, CancellationToken ct)
    {
        if (studio.Mode == StudioDeploymentMode.Disabled)
            return new AccessSimulationStudioDto("Disabled", [], [], []);

        var fromRoles = studio.EffectiveCapabilitiesForRoles(roles);
        var fromGroups = await studioCapabilities.ResolveForUserAsync(userId, ct);
        var combined = fromRoles.Concat(fromGroups)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();

        // Reported separately because the remedy differs: a role-mapped capability changes in
        // configuration, a group-granted one changes in the Portal.
        return new AccessSimulationStudioDto(studio.Mode.ToString(), combined, fromRoles, fromGroups);
    }

    private async Task<IReadOnlyList<AccessSimulationConnectionDto>> BuildConnectionsAsync(
        PortalUser user, bool isAdmin, CancellationToken ct)
    {
        var identity = new ExecutionIdentity
        {
            EffectiveUser = user.UserName!,
            EffectiveUserId = user.Id,
            RealUser = user.UserName!,
            IsAdmin = isAdmin
        };

        var usable = new HashSet<string>(
            await connections.ListUsableAliasesAsync(identity, ct), StringComparer.OrdinalIgnoreCase);

        return
        [
            .. (await connections.ListAsync(ct))
                .Select(summary => new AccessSimulationConnectionDto(
                    summary.Alias,
                    summary.ConnectorType,
                    summary.Disabled,
                    Usable: !summary.Disabled && usable.Contains(summary.Alias),
                    Reason: summary.Disabled ? "Connection is disabled."
                        : usable.Contains(summary.Alias)
                            ? isAdmin ? "Administrator." : "Granted through a group, or unrestricted."
                            : "No group grant."))
        ];
    }

    private async Task<AccessSimulationReportDto?> BuildReportAsync(
        int reportId, PortalUser user, ISet<int> groupIds, bool isAdmin, CancellationToken ct)
    {
        var report = await db.Reports
            .Include(r => r.Folder)
            .FirstOrDefaultAsync(r => r.Id == reportId && !r.IsDeleted, ct);
        if (report is null) return null;

        // Both the answer and its explanation come from FolderPermissionService, so this can never
        // disagree with the enforcement it is describing.
        var effective = await folderPermissions.GetEffectiveReportPermissionAsync(
            report, user.Id, groupIds, isAdmin);
        var sources = await folderPermissions.DescribeReportGrantsAsync(report, user.Id, groupIds, isAdmin);

        return new AccessSimulationReportDto(
            report.Id,
            report.Name,
            report.Folder?.Path ?? "",
            effective?.ToString(),
            sources,
            CanView: effective is not null,
            CanExecute: effective.AtLeast(FolderPermission.Execute),
            CanManage: effective.AtLeast(FolderPermission.Manage),
            RowLevelSecurity: await BuildRowLevelSecurityAsync(report, user, isAdmin, ct));
    }

    /// <summary>
    /// Names the identity the report filters on and the values that would be bound — and stops
    /// there. Running the report to find out what the user would see is the one thing this must
    /// never do.
    /// </summary>
    private async Task<AccessSimulationRlsDto> BuildRowLevelSecurityAsync(
        Report report, PortalUser user, bool isAdmin, CancellationToken ct)
    {
        string? scriptText = null;
        if (PortalPathGuard.TryResolveScript(
                config, datasetScope.TenantId, report.ScriptPath, out var resolved)
            && File.Exists(resolved))
        {
            try { scriptText = await File.ReadAllTextAsync(resolved, ct); }
            catch (IOException) { /* unreadable script explains as not-scanned below */ }
        }

        if (scriptText is null)
        {
            return new AccessSimulationRlsDto(
                IdentitySensitive: null, [], null, null,
                Explanation: "The report script could not be read, so identity references were not scanned.");
        }

        var references = RowLevelSecurityScan.IdentityReferences(scriptText);
        if (references.Count == 0)
        {
            return new AccessSimulationRlsDto(
                IdentitySensitive: false, [], null, null,
                Explanation: "The script references no identity, so every viewer sees the same rows.");
        }

        var bypasses = isAdmin && config.Security.AdminBypassRowLevelSecurity;
        return new AccessSimulationRlsDto(
            IdentitySensitive: true,
            references,
            BoundUser: user.UserName,
            BoundGroups: user.UserGroups.Select(ug => ug.Group.Name)
                .OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            Explanation: bypasses
                ? $"Filtered by {string.Join(", ", references)}, but this identity is an administrator "
                  + "and the deployment lets administrators bypass row-level security."
                : $"Filtered by {string.Join(", ", references)} against this identity. Rows are not "
                  + "shown here — run the report as the user to see them.");
    }

    private async Task<AccessSimulationDatasetDto?> BuildDatasetAsync(
        int datasetId, int userId, bool isAdmin, ISet<int> groupIds, CancellationToken ct)
    {
        var dataset = await datasetScope.Query(db)
            .Include(d => d.OwningReport)
            .Include(d => d.Acls)
            .Include(d => d.UserAcls)
            .FirstOrDefaultAsync(d => d.Id == datasetId, ct);
        if (dataset is null) return null;

        var permission = await datasetPermissions.GetEffectivePermissionAsync(dataset, userId, isAdmin, groupIds);

        var sources = new List<string>();
        if (isAdmin) sources.Add("Administrator role");
        if (dataset.UserAcls.Any(acl => acl.UserId == userId))
            sources.Add("Direct user grant");
        if (dataset.Acls.Any(acl => groupIds.Contains(acl.GroupId)))
            sources.Add("Group grant");
        if (dataset.AccessLevel == DatasetAccessLevel.Public)
            sources.Add("Dataset is Public");

        return new AccessSimulationDatasetDto(
            dataset.Id,
            dataset.Name,
            dataset.AccessLevel.ToString(),
            permission?.ToString(),
            sources.Count == 0 ? ["No grant"] : sources);
    }


}
