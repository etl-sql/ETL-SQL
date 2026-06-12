using ETL_SQL.ReportPortal.Data;

namespace ETL_SQL.ReportPortal.Models;

public record CreateFolderRequest(string Name, int? ParentId);
public record UpdateFolderRequest(string? Name, int? ParentId);

public record FolderDto(int Id, int? ParentId, string Name, string Path, List<FolderDto> Children, long Version = 1);

public record FolderAclDto(int GroupId, string GroupName, FolderPermission Permission);

public record GrantPermissionRequest(int GroupId, FolderPermission Permission);
