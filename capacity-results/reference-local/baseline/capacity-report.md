# ETL-SQL Capacity Test Report

Generated: 2026-06-04T19:58:33.885Z

## Reference Environment

| Field | Value |
| :--- | :--- |
| hostname | ChuckPC |
| platform | win32 10.0.26200 x64 |
| cpuModel | Intel(R) Core(TM) Ultra 9 275HX |
| cpuCount | 24 |
| totalMemoryBytes | 33690271744 |
| nodeVersion | v24.14.0 |
| dotnetVersion | 10.0 |
| diskType | SSD |
| deploymentMode | Release build, local processes, Orchestrator in-process execution |
| databaseLocation | local SSD, separate Portal and Orchestrator SQLite files |
| notes | Local developer-workstation starter baseline. Portal MaxConcurrentReportExecutions=4; Orchestrator JobThrottle MaxConcurrentJobs=4. |

## Portal Results

| Concurrency | Requests/min | Error % | p50 ms | p95 ms | p99 ms | SQLite contention | Pass |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| 1 | 28724 | 0 | 1.55 | 3.47 | 4.69 | 0 | OK |
| 5 | 36792 | 0 | 1.66 | 6.87 | 167.43 | 0 | OK |
| 10 | 36796 | 0 | 1.9 | 14.03 | 340.97 | 0 | OK |
| 20 | 35592 | 0 | 2.18 | 155.99 | 795.36 | 0 | OK |
| 40 | 31024 | 0 | 11.81 | 329.58 | 1733.84 | 0 | OK |
| 80 | 28352 | 0 | 50.13 | 584.19 | 3632.64 | 0 | OK |
| 120 | 30216 | 0 | 71.17 | 823.02 | 4855.51 | 0 | OK |

## Orchestrator Results

| Concurrency | Requests/min | Error % | p50 ms | p95 ms | p99 ms | SQLite contention | Pass |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| 1 | 156 | 0 | 0.86 | 4.17 | 9.49 | 0 | OK |
| 5 | 880 | 0 | 0.53 | 1.06 | 15.91 | 0 | OK |
| 10 | 1632 | 0 | 0.53 | 6.78 | 16.79 | 0 | OK |
| 20 | 3224 | 0 | 0.51 | 16.08 | 189.79 | 0 | OK |
| 40 | 6620 | 0 | 0.44 | 16.1 | 189.22 | 0 | OK |
| 80 | 9736 | 0 | 68.74 | 525.61 | 708.2 | 0 | FAIL |
| 120 | 12144 | 0 | 89 | 841.19 | 917.23 | 0 | FAIL |
