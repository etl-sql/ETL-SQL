using System.Threading.Tasks;

namespace ETL_SQL.Core.Execution;
/// <summary>
/// Defines an interface for data structures that can be proactively spilled to disk
/// to reclaim system memory during global resource pressure.
/// </summary>
public interface ISpillable
{
    /// <summary>
    /// Gets the approximate memory usage in bytes of the in-memory portion of this object.
    /// </summary>
    long MemoryUsageBytes { get; }

    /// <summary>
    /// Proactively flushes in-memory data to the SpillStore.
    /// </summary>
    /// <returns>True if any memory was reclaimed; false if already spilled or not applicable.</returns>
    Task<bool> SpillAsync();

    /// <summary>
    /// A human-readable identifier for logging (e.g. "#tempTableX" or "JoinBuffer_01").
    /// </summary>
    string SpillToken { get; }
}
