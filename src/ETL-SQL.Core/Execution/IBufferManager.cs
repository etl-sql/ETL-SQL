using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Common;

namespace ETL_SQL.Core.Execution;
/// <summary>
/// Contract for the global resource coordinator. 
/// Defined in Core to avoid circular dependencies between Engine and Orchestrator.
/// </summary>
public interface IBufferManager
{
    /// <summary>
    /// Requests a specific amount of memory. 
    /// Returns a disposable that releases the memory when disposed.
    /// </summary>
    Task<IDisposable> ReserveMemoryAsync(string sessionId, long bytes, bool isOverride = false, object? owner = null);

    /// <summary>
    /// Requests a streaming cursor slot.
    /// Returns a disposable that releases the slot when disposed.
    /// </summary>
    Task<IDisposable> AcquireCursorAsync(string sessionId, bool isOverride = false, object? owner = null);

    /// <summary>
    /// Forcefully releases all memory and cursor reservations associated with a session.
    /// Used for 'Zombie Protection' to clean up leaked resources when a script finishes.
    /// </summary>
    void ReleaseAllForSession(string sessionId);

    /// <summary>
    /// Registers a spillable object for global memory reclamation.
    /// </summary>
    void RegisterSpillable(ISpillable spillable);

    /// <summary>
    /// Unregisters a spillable object.
    /// </summary>
    void UnregisterSpillable(ISpillable spillable);

    /// <summary>
    /// Proactively triggers spills on registered objects to free memory.
    /// </summary>
    Task<long> TriggerSpillsUnderPressureAsync(long requiredBytes);
}

/// <summary>
/// Global configuration for resource governance.
/// </summary>
public class BufferManagerOptions
{
    public int MaxGlobalMemoryMB { get; set; } = 2048;
    public int MaxStreamingCursors { get; set; } = 50;
    public int ResourceWaitTimeoutSeconds { get; set; } = 600;
    public int HysteresisMemoryMB { get; set; } = 256;
    public int SystemMemoryFloorMB { get; set; } = LanguageMetadata.DefaultSystemMemoryFloorMB;
}
