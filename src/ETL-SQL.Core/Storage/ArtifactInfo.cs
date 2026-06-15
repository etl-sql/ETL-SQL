using System;

namespace ETL_SQL.Core.Storage;

/// <summary>
/// Metadata for a stored artifact. <see cref="Path"/> is always the artifact's location relative to
/// its <see cref="ArtifactArea"/> root, normalized to forward slashes (provider-agnostic), so the same
/// value round-trips through every <see cref="IArtifactStorage"/> operation regardless of the backing
/// store's native separator.
/// </summary>
public readonly record struct ArtifactInfo(string Path, long Length, DateTimeOffset LastModifiedUtc);
