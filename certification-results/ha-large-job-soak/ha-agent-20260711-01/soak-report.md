# HA Large-Job Soak Report

Run id: `ha-agent-20260711-01`
Mode: `CiSmoke`
Status: **Passed**
Runner: `NativeBoundedLargeJobCiSmoke`
Certification level: `CiSmokeEvidence`

| Scenario | Status | Rows | Spill bytes | Cleanup |
| :--- | :--- | ---: | ---: | :--- |
| MixedScanSpillSortJoinAggregate_Concurrent | Passed | 161892352 | 1105798528 | Passed |
| CancelDuringScan | Passed | 0 | 0 | Passed |
| CancelDuringSpillWrite | Passed | 0 | 0 | Passed |
| CancelDuringSpillRead | Passed | 0 | 0 | Passed |
| CancelDuringRepartition | Passed | 0 | 0 | Passed |
