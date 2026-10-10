using ETL_SQL.Portal.Data;

namespace ETL_SQL.Portal.Models;

public record CreateFolderRequest(string Name, int? ParentId, string? OwnerUsername = null);
public record UpdateFolderRequest(string? Name, int? ParentId);

public record FolderDto(int Id, int? ParentId, string Name, string Path, List<FolderDto> Children, long Version = 1);

public record FolderAclDto(int GroupId, string GroupName, FolderPermission Permission);
public record FolderPermissionOptionDto(FolderPermission Permission, string Name, int Rank);
public record FolderAclCatalogResponse(
    int FolderId, string FolderName, long Version,
    IReadOnlyList<FolderAclDto> Items, IReadOnlyList<FolderPermissionOptionDto> Permissions);
public record FolderAclGrantResponse(int FolderId, int GroupId, FolderPermission Permission, long Version);
public record FolderAclRevokeResponse(int FolderId, int GroupId, long Version);

public record GrantPermissionRequest(int GroupId, FolderPermission Permission);
