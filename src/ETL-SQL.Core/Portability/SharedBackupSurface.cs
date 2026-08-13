using System.Collections.Generic;

namespace ETL_SQL.Core.Portability;

public enum SurfaceHost
{
    Portal,
    Orchestrator,
    Artifact
}

public enum SurfacePartitionMode
{
    DirectTenantColumn,
    TenantRootJoin,
    TenantPrefix,
    Global,
    Tombstone
}

public enum SurfaceBackupDisposition
{
    Required,
    OptionalContent,
    Excluded
}

public enum SurfaceRestoreDisposition
{
    RestoreDisabled,
    Rebind,
    Rebuild,
    Exclude
}

public enum SurfaceDeletionDisposition
{
    Delete,
    Tombstone,
    Retain
}

public sealed record SharedBackupSurface
{
    public required string SurfaceId { get; init; }
    public required SurfaceHost Host { get; init; }
    public required string PhysicalName { get; init; }
    public required SurfacePartitionMode PartitionMode { get; init; }
    public required string AuthoritativeRoot { get; init; }
    public required SurfaceBackupDisposition BackupDisposition { get; init; }
    public required SurfaceRestoreDisposition RestoreDisposition { get; init; }
    public required SurfaceDeletionDisposition DeletionDisposition { get; init; }
    public required string ExclusionReason { get; init; }
}
