namespace ETL_SQL.Core.Execution;
/// <summary>
/// Abstraction for system resource monitoring to enable unit testing
/// of resource-aware governance logic.
/// </summary>
public interface ISystemResources
{
    /// <summary>
    /// Returns the amount of physical memory currently available to the OS in bytes.
    /// </summary>
    long GetAvailableMemoryBytes();
}
