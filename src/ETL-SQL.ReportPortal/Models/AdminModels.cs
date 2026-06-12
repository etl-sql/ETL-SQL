namespace ETL_SQL.ReportPortal.Models;

public record CreateUserRequest(
    string Username,
    string Email,
    string? Password,
    string Role,
    string? FirstName,
    string? LastName,
    string? Provider = null);

public record ResetPasswordRequest(string NewPassword);

public record UserDto(
    int Id,
    string Username,
    string? Email,
    string? FirstName,
    string? LastName,
    bool IsActive,
    bool MustChangePassword,
    DateTime CreatedAt,
    IList<string> Roles,
    IList<string> Groups,
    string? Provider = null,
    long Version = 1);

public record UpdateUserRequest(
    string? Email,
    string? FirstName,
    string? LastName,
    string? Role,
    bool? IsActive);

public record VersionedResourceRequest(int Id, long Version);

public record BulkUserStatusRequest(IList<VersionedResourceRequest>? Users, bool IsActive)
{
    public IList<int> UserIds => (Users ?? []).Select(x => x.Id).ToList();
}

public record CreateGroupRequest(string Name, string? Description, string? Provider = null, string? AdGroup = null);
public record UpdateGroupRequest(string? Name, string? Description, string? Provider = null, string? AdGroup = null);

public record GroupDto(int Id, string Name, string? Description, int MemberCount, string? Provider = null, string? AdGroup = null, long Version = 1);
public record GroupMemberDto(int Id, string Username, string? Email, bool IsActive);

public record AddUserToGroupRequest(string? Username, int? UserId);
public record BulkGroupMembershipRequest(IList<int> UserIds);
public record BulkDeleteGroupsRequest(IList<VersionedResourceRequest>? Groups, bool Cascade = false)
{
    public IList<int> GroupIds => (Groups ?? []).Select(x => x.Id).ToList();
}

public record BulkMutationResult(
    int Id,
    string Status,
    long? CurrentVersion = null,
    string? Error = null);

public record AuditLogDto(
    int Id,
    int? UserId,
    string? Username,
    string Action,
    string? ResourceType,
    string? ResourceId,
    DateTime Timestamp,
    string? Detail);

public record PagedResult<T>(IList<T> Items, int Total, int Page, int PageSize);

public record EffectivePermissionEntryDto(
    string ResourceType,
    int ResourceId,
    string Name,
    string Path,
    string Permission,
    IReadOnlyList<string> Sources);

public record EffectiveUserPermissionsDto(
    int UserId,
    string Username,
    IReadOnlyList<string> Groups,
    IReadOnlyList<EffectivePermissionEntryDto> Folders,
    IReadOnlyList<EffectivePermissionEntryDto> Reports);

public record EffectivePrincipalPermissionDto(
    int UserId,
    string Username,
    IReadOnlyList<string> Groups,
    string Permission,
    IReadOnlyList<string> Sources);

public record PortalUsageMetricsDto(
    int TotalViews,
    int UniqueViewers,
    int ReportsViewed,
    int RefreshFailureCount,
    double? AverageRefreshDurationMs,
    int SubscriptionDeliveryFailureCount,
    IReadOnlyList<ReportUsageMetricDto> Reports);

public record ReportUsageMetricDto(
    int ReportId,
    string ReportName,
    string FolderPath,
    int ViewCount,
    int UniqueViewers,
    DateTime? LastViewedAt,
    string? LastRefreshStatus,
    long? LastRefreshDurationMs,
    string? LastRefreshError,
    int SubscriptionFailureCount);
