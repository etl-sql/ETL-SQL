namespace ETL_SQL.Core.Storage;

/// <summary>
/// The logical class of artifact a storage operation targets. Each area maps to its own root in a
/// provider (a directory for the filesystem providers), lets the provider apply area-specific policy
/// (e.g. owner-only permissions and no leasing for <see cref="Keys"/>), and keeps unrelated artifact
/// kinds namespaced apart so a path in one area can never resolve into another.
/// </summary>
public enum ArtifactArea
{
    /// <summary>Published report/ETL scripts (today: <c>Portal:ScriptRootPath</c>).</summary>
    Scripts,

    /// <summary>Rendered report snapshots (today: <c>Portal:SnapshotDirectory</c>).</summary>
    Snapshots,

    /// <summary>Cached dataset materializations, encrypted at rest (today: <c>Portal:DatasetRootPath</c>).</summary>
    Datasets,

    /// <summary>Lookup/map files (today: <c>Portal:MapRootPath</c>).</summary>
    Maps,

    /// <summary>
    /// Sensitive key material (Data Protection key ring, dataset at-rest keys). Providers treat this
    /// area as secret: owner-only permissions on write, and no local-copy leasing.
    /// </summary>
    Keys,
}
