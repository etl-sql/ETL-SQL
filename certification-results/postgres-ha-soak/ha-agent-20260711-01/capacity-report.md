# ETL-SQL Capacity Test Report

Generated: 2026-07-11T14:58:52.412Z

## Reference Environment

| Field | Value |
| :--- | :--- |
| hostname | ChuckPC |
| platform | win32 10.0.26200 x64 |
| cpuModel | Intel(R) Core(TM) Ultra 9 275HX |
| cpuCount | 24 |
| totalMemoryBytes | 33690333184 |
| nodeVersion | v24.14.0 |
| dotnetVersion | record runtime from container image |
| diskType | record host disk type |
| deploymentMode | PostgreSQL HA soak topology (ha-agent-20260711-01) |
| databaseLocation | PostgreSQL via deploy/docker/docker-compose.ha.yml |
| notes | Materialized from ha-soak-runs/ha-agent-20260711-01/postgres-ha-soak.env. Generated workload contains the local Orchestrator API key; do not commit it. |

## Portal Results

| Concurrency | Requests/min | Error % | p50 ms | p95 ms | p99 ms | SQLite contention | Pass |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| 1 | 542 | 0 | 4.31 | 8.04 | 17.17 | 0 | OK |
| 10 | 5434 | 0 | 4.51 | 7.66 | 9.82 | 0 | OK |
| 25 | 13595 | 0 | 6.01 | 9.89 | 12.14 | 0 | OK |

## Orchestrator Results

| Concurrency | Requests/min | Error % | p50 ms | p95 ms | p99 ms | SQLite contention | Pass |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| 1 | 172 | 0 | 1.97 | 3.06 | 6.69 | 0 | OK |
| 10 | 1684.8 | 0 | 1.51 | 2.72 | 3.48 | 0 | OK |
| 25 | 4214.4 | 0 | 1.46 | 2.67 | 3.52 | 0 | OK |
