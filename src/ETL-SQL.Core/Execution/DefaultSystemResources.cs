using System;

namespace ETL_SQL.Core.Execution;
public class DefaultSystemResources : ISystemResources
{
    public long GetAvailableMemoryBytes()
    {
        // In .NET Core / .NET 5+, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes 
        // returns the memory available to the process, which usually tracks 
        // the OS physical memory (or container limit).
        return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    }
}
