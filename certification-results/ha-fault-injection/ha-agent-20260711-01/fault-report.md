# HA Fault-Injection Report

Run id: `ha-agent-20260711-01`
Mode: `CiSmoke`
Status: **Passed**
Runner: `NativeBoundedFaultInjectionCiSmoke`
Certification level: `CiSmokeEvidence`

| Fault | Category | Status | Cleanup |
| :--- | :--- | :--- | :--- |
| DiskLowSpaceBeforeExtentWrite | disk | Passed | Passed |
| DiskFullDuringExtentWrite | disk | Passed | Passed |
| SlowDiskWriteAndRead | disk-latency | Passed | Passed |
| CorruptExtentBeforeRead | corruption | Passed | Passed |
| IncompleteExtentAfterCrash | crash-recovery | Passed | Passed |
| WorkerProcessCrashMidJob | crash-recovery | Passed | Passed |
| PortalNodeLossWithActiveSession | node-loss | Passed | Passed |
| OrchestratorLeaderLossDuringSchedule | node-loss | Passed | Passed |
| PostgresOutageBrief | database-outage | Passed | Passed |
| TempRootExhaustion | filesystem | Passed | Passed |
