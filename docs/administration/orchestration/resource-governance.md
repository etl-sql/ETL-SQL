# Resource Governance

To prevent Out-Of-Memory (OOM) errors and database connection exhaustion in multi-user environments, the Orchestrator and Execution Engine share one coordinator (**`BufferManager`** implementing both `IBufferManager` and `IMemoryGrantArbiter`). Both async session reservations and synchronous external-operator leases (`Sort`, `Window`, `Join`, `Aggregate`, `Distinct`, `TopN`, `#temp`) draw from the same byte counter without double counting.

## Precedence & Compatibility

- **`Engine:TotalMemoryGrantMB >= 0`**: Explicit engine ceiling takes precedence (`0` disables the global ceiling while preserving any active `SystemMemoryFloorMB`; positive values override `MaxGlobalMemoryMB`).
- **`Engine:TotalMemoryGrantMB < 0` or unset (`auto`)**: Resolves to `min(auto80PercentOfRam, Orchestration:ResourceManagement:MaxGlobalMemoryMB)` when `MaxGlobalMemoryMB > 0`.
- **`Engine:OperatorMemoryGrantMB`** (or `SET OPERATOR_MEMORY_GRANT = n`): Bounds a single operator's in-memory buffer by `min(OperatorMemoryGrantMB, coordinatorCeiling)`.
- **`SystemMemoryFloorMB` (default `512` MB)**: `DefaultSystemResources` measures **current host/container free memory headroom** (Windows `GlobalMemoryStatusEx` + job limits; Linux `/proc/meminfo` `MemAvailable` + cgroups v1/v2) rather than static total RAM capacity. Because free headroom on 2–8 GB hosts and containers can sit below 4 GB while idle, the default floor moved from `4096` MB to `512` MB (set explicitly to `4096` to keep the legacy server floor, or `0` to disable).

## Resource Queuing (FIFO) & Operator Spill

When global limits for RAM or Database Cursors (`MaxStreamingCursors`) are reached, or when live host/container headroom drops below `SystemMemoryFloorMB`:

- **Async reservations (`ReserveMemoryAsync`)**: New callers wait in a **FIFO** queue and resume automatically once released reservations clear hysteresis or host headroom recovers above the floor.
- **External operators (`IMemoryGrantLease`)**: Never block while holding buffers needed for downstream progress; `RegisterAndCheckSpill` signals pressure immediately so the operator spills to disk and frees its reservation.
- **Visual Feedback**: Waiting requests log progress updates (`Session ... still waiting for ...`).
- **Timeout**: If a queued wait is not granted within `ResourceWaitTimeoutSeconds` (default `600` seconds), it fails with `TimeoutException`.

## Hysteresis (Memory Cooldown)
To prevent resource thrashing when the global limit is hit:

1. Entering exhaustion triggers proactive spills on registered spillable buffers (`InMemoryDataSource`) and pauses new admissions.
2. Safe threshold = `max(EffectiveMaxMemory - HysteresisMemoryMB, EffectiveMaxMemory / 2)`.
3. Admission resumes when active usage + the requested allocation fit under the safe threshold (or when all reservations drain to zero).

## Policy Overrides
Users can bypass global resource governors using `SET` commands (e.g., `SET MAX_MEMORY = 4096`). 

> [!WARNING]
> **Accountability**: Any resource request that exceeds the global policy via a `SET` command is logged with a `[POLICY_OVERRIDE]` tag in the central AppLog. This allows administrators to trace system instability back to specific user-initiated overrides.

---
