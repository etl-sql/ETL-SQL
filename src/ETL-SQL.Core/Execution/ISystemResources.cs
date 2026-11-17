namespace ETL_SQL.Core.Execution;

/// <summary>
/// Abstraction for system resource monitoring to enable unit testing
/// of resource-aware governance logic.
/// </summary>
public interface ISystemResources
{
    /// <summary>
    /// Returns the amount of host/container memory headroom currently available in bytes.
    /// </summary>
    long GetAvailableMemoryBytes();

    /// <summary>
    /// Returns the total physical or container memory capacity available to the process in bytes.
    /// </summary>
    long GetTotalMemoryBytes() => GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
}
