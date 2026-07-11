# HA Fault-Injection Plan

Run id: `ha-agent-20260711-01`
Mode: `CiSmoke`
Fault count: `10`

| Fault | Category | Injection point | State | Required evidence count |
| :--- | :--- | :--- | :--- | ---: |
| DiskLowSpaceBeforeExtentWrite | disk | pre-spill-write-admission | ReadyForRunner | 5 |
| DiskFullDuringExtentWrite | disk | spill-write | ReadyForRunner | 5 |
| SlowDiskWriteAndRead | disk-latency | spill-write-and-read | ReadyForRunner | 5 |
| CorruptExtentBeforeRead | corruption | pre-spill-read | ReadyForRunner | 5 |
| IncompleteExtentAfterCrash | crash-recovery | spill-write | ReadyForRunner | 5 |
| WorkerProcessCrashMidJob | crash-recovery | orchestrator-job-execution | ReadyForRunner | 5 |
| PortalNodeLossWithActiveSession | node-loss | portal-node | ReadyForRunner | 5 |
| OrchestratorLeaderLossDuringSchedule | node-loss | orchestrator-leader | ReadyForRunner | 5 |
| PostgresOutageBrief | database-outage | postgres-service | ReadyForRunner | 5 |
| TempRootExhaustion | filesystem | temp-root-admission | ReadyForRunner | 5 |
