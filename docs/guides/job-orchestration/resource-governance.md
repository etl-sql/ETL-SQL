# Resource Governance

## 11. Resource Governance

To prevent Out-Of-Memory (OOM) errors and database connection exhaustion in multi-user environments, the Orchestrator employs a **Buffer Manager**.

### 11.1 Resource Queuing (FIFO)
When global limits for RAM (`MaxGlobalMemoryMB`) or Database Cursors (`MaxStreamingCursors`) are reached, new requests are placed in a **First-In, First-Out (FIFO)** queue. 

- **Graceful Wait**: The engine will block and wait for resources to become available.
- **Visual Feedback**: Every minute, the engine prints a status update to the console or session log: `Waiting for resources... (T-4 minutes remaining)`. This allows operators to differentiate between a hung process and a resource-constrained wait.
- **Timeout**: If the resource is not granted within the configured `ResourceWaitTimeoutSeconds` (default 10 minutes), the script fails with a `TimeoutException`.

### 11.2 Hysteresis (Memory Cooldown)
To prevent "resource thrashing" (where the engine constantly starts and immediately stalls tasks as tiny amounts of memory fluctuate), the Buffer Manager employs a **Hysteresis Threshold**.

Once the global memory limit is hit and the engine enters the "Exhausted" state:
1. All new memory requests are queued.
2. Queue processing is **suspended** until memory usage drops below a safe threshold.
3. Safe threshold = `MaxGlobalMemoryMB - HysteresisMemoryMB`.
4. This ensures that when the engine resumes, it has enough room to process at least a few full batches without immediately re-entering the exhausted state.

### 11.3 Policy Overrides
Users can bypass global resource governors using `SET` commands (e.g., `SET MAX_MEMORY = 4096`). 

> [!WARNING]
> **Accountability**: Any resource request that exceeds the global policy via a `SET` command is logged with a `[POLICY_OVERRIDE]` tag in the central AppLog. This allows administrators to trace system instability back to specific user-initiated overrides.

---

