using System;

namespace ETL_SQL.Core.Storage;

/// <summary>
/// Supplies the process-scoped artifact write fence token used by <see cref="FencedArtifactStorage"/>.
/// </summary>
public interface IArtifactWriteFenceTokenProvider
{
    long CurrentToken { get; }
}

/// <summary>
/// Default artifact write fence token provider.
/// </summary>
///
/// <remarks>
/// The token is fixed for the process lifetime and derived from startup UTC ticks. That gives a
/// restarted/replacement node a higher token than an older paused process in the normal HA case, while
/// keeping all writes from one process in the same epoch. The shared database write-epoch table remains
/// the authority that rejects older tokens for a given artifact.
/// </remarks>
public sealed class ProcessArtifactWriteFenceTokenProvider : IArtifactWriteFenceTokenProvider
{
    public long CurrentToken { get; } = DateTime.UtcNow.Ticks;
}
