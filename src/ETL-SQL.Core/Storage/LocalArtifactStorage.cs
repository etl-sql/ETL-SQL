using System.Collections.Generic;

namespace ETL_SQL.Core.Storage;

/// <summary>
/// Local-disk artifact storage — the default provider, equivalent to the filesystem access the Portal
/// used before the abstraction. Each area maps to a directory on a volume local to the node.
/// </summary>
public sealed class LocalArtifactStorage(IReadOnlyDictionary<ArtifactArea, string> roots)
    : FileSystemArtifactStorage(roots);
