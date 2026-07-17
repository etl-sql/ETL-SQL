# SHOW HOST METRICS
Displays host-utilization time series for capacity planning: per node, the last 24 hours of memory load %, CPU %, and free disk (MB) on the state and spill volumes.

## Syntax
```sql
SHOW HOST METRICS ['<nodeId>'] [INTO #table];
```

## Parameters
- **'nodeId'** — Optional. Filters output to a single node. When omitted, shows metrics for all nodes.
- **INTO #table** — Optional. Captures the result set into a temp table for programmatic use.

## Returns
A result set with columns including `NodeId`, `Timestamp`, `MemoryLoadPercent`, `CpuPercent`, `StateDiskFreeMB`, `SpillDiskFreeMB`, ordered newest first.

## Example
```sql
-- View all node metrics
SHOW HOST METRICS;

-- View metrics for a specific node
SHOW HOST METRICS 'node-02';

-- Capacity planning: find nodes low on disk in the last 24h
SHOW HOST METRICS INTO #hm;
SELECT NodeId,
       MIN(StateDiskFreeMB) AS MinStateFreeMB,
       MIN(SpillDiskFreeMB) AS MinSpillFreeMB,
       MAX(MemoryLoadPercent) AS PeakMemPct
FROM #hm
GROUP BY NodeId;
```

## Notes
- Data covers the last 24 hours of heartbeat samples.
- Available only in Orchestrator-managed HA deployments where node heartbeats are active.
- Useful for proactive capacity planning and alerting on resource-constrained nodes.

## References
- [SHOW Commands](README.md)
