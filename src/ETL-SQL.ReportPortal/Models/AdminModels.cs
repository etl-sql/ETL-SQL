namespace ETL_SQL.ReportPortal.Models;

public record CreateUserRequest(
    string Username,
    string Email,
    string Password,
    string Role,
    string? FirstName,
    string? LastName);

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
    IList<string> Groups);

public record UpdateUserRequest(
    string? Email,
    string? FirstName,
    string? LastName,
    string? Role,
    bool? IsActive);

public record CreateGroupRequest(string Name, string? Description);
public record UpdateGroupRequest(string? Name, string? Description);

public record GroupDto(int Id, string Name, string? Description, int MemberCount);

public record AddUserToGroupRequest(string? Username, int? UserId);

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
