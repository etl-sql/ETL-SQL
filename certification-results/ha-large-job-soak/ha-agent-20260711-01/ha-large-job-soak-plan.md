# HA Large-Job Soak Plan

Run id: `ha-agent-20260711-01`
Mode: `CiSmoke`
Duration minutes: `15`

| Scenario | State | Jobs | Cancellation point | Required telemetry count |
| :--- | :--- | ---: | :--- | ---: |
| MixedScanSpillSortJoinAggregate_Concurrent | ReadyForRunner | 3 |  | 9 |
| CancelDuringScan | ReadyForRunner | 1 | scan | 5 |
| CancelDuringSpillWrite | ReadyForRunner | 1 | spill-write | 5 |
| CancelDuringSpillRead | ReadyForRunner | 1 | spill-read | 5 |
| CancelDuringRepartition | ReadyForRunner | 1 | repartition | 6 |
