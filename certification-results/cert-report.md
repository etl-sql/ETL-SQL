# ETL-SQL Scale Certification Report

Generated: 2026-08-20 15:39:14  |  Tier: **Standard**  |  Row scale: **10x**  |  Samples: **3**

## Results

| Scenario | Samples | Rows | Rows/s | Elapsed (ms) | Spill Write | Peak WS (MB) | Private (MB) | Heap (MB) | Allocated (MB) | CPU % | GC Pause (ms) | Bound (MB) | Pass |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| CsvIngest_500000 | 3 | 500000 | 1655629.1 | 302 | 0 | 942.8 | 1019.6 | 461 | 531.4 | 16.9 | 127.4 | 4096 | OK |
| CubeGroupingSets_500000_10x5 | 3 | 500000 | 108131.5 | 4624 | 224000000 | 1304.2 | 1367.5 | 720.8 | 4214.8 | 8.4 | 258.9 | 4096 | OK |
| ExternalAggregate_1000000_10grps | 3 | 1000000 | 553097.3 | 1808 | 80000000 | 662.9 | 687.5 | 376.8 | 1117.6 | 7.8 | 130.9 | 4096 | OK |
| ExternalJoin_500000_equality | 3 | 500000 | 174947.5 | 2858 | 88000000 | 865.2 | 958.4 | 337.2 | 3151.5 | 10.1 | 338.9 | 4096 | OK |
| ExternalSort_500000_DESC | 3 | 500000 | 106292.5 | 4704 | 119997440 | 530.8 | 494.8 | 260.7 | 2081.1 | 6.4 | 133.6 | 4096 | OK |
| ParquetRoundTrip_500000 | 3 | 500000 | 1845018.5 | 271 | 0 | 979.9 | 1061.5 | 449.2 | 457.4 | 15.5 | 105.1 | 4096 | OK |
| ReportDatasetSnapshotReload_500000 | 3 | 500000 | 659630.6 | 758 | 0 | 1204.3 | 1280.2 | 867.6 | 1522.5 | 12.5 | 193.1 | 4096 | OK |
| ScalarSubqueryCache_500000_1000keys | 3 | 500000 | 314861.5 | 1588 | 40000000 | 1335.7 | 1585.1 | 817.5 | 1568.9 | 9.9 | 222.2 | 4096 | OK |
| SpillCleanupFailure_500000 | 3 | 500000 | 33333333.3 | 15 | 331776 | 1271.5 | 1513.1 | 652.1 | 15.3 | 37.5 | 128.5 | 4096 | OK |
| SpillCleanupSuccess_500000 | 3 | 500000 | 2232142.9 | 224 | 16257024 | 1290.7 | 1539.6 | 653.1 | 334.1 | 21.8 | 155 | 4096 | OK |
| StreamingSelect_1000000_cap50000 | 3 | 1000000 | 186185.1 | 5371 | 0 | 761.4 | 852.4 | 221.3 | 66.6 | 0.9 | 54.9 | 4096 | OK |
| TempTableSpill_500000_SELECT_INTO | 3 | 500000 | 1607717 | 311 | 16257024 | 808.6 | 903 | 330.7 | 334.2 | 14.1 | 83.1 | 4096 | OK |
| WindowFunction_ROW_NUMBER_500000 | 3 | 500000 | 150829.6 | 3315 | 112000000 | 922.2 | 1006 | 412.2 | 3421.9 | 7.5 | 220.7 | 4096 | OK |

## Environment

- OS: Microsoft Windows 11 Home 10.0.26200
- CPU: Intel(R) Core(TM) Ultra 9 275HX (24 logical cores)
- RAM: 31.4 GB
- Disk: NVMe MTFDKBA1T0QGN-1BN1AABGA, 953.9 GB; workspace free 284.7 GB
- Runtime: .NET 10.0.11, X64, Release, server GC enabled: True
- Engine memory grant: 2048 MB
- Commit: 0e6e691240ecdff0d9817c0e9f12e0ef8bfaa587 (release/v0.18.0); dirty: True
- Source fingerprint: c6c0df99621155685addb01e66c11c3228b1976d22c531a24301169970bf35d3
- Config fingerprint: acd69c240e91629d968545fd90c16a3fe9b3ed1faa50f987de7e3c106ac89266

## Operator Status

| Operator | Execution Mode | Scale Tested | Notes |
| :--- | :--- | :--- | :--- |
| ORDER BY | External Sort (multi-chunk) | 50k rows | Run size scales from 5K to the production 100K cap while preserving multiple runs |
| GROUP BY | External Aggregate | 100k rows | OperatorMemoryGrantMB forced to 1 MB |
| JOIN (equality) | External Hash Join | 50k rows | JoinSpillThreshold forced to 5k |
| SELECT INTO #temp | Temp Table Spill | 50k rows | Retains one configured batch, then validates every spilled extent during COUNT(*) readback |
| SELECT (streaming) | Result Cap | 100k rows | MaxLastResultRows cap enforced at 50k |
| WINDOW ROW_NUMBER | External Window | 50k rows | WindowSpillThreshold forced to 5k |
| CSV ingest | Connector batch read | 50k rows | Row count and checksum certified |
| Parquet round trip | Connector batch write/read | 50k rows | Row count and checksum certified |
| CREATE DATASET snapshot/reload | Query -> Parquet cache -> reload | 50k rows | Row count and checksum certified after cached reload |
| GROUP BY CUBE | External Aggregate grouping-set expansion | 50k rows | Expanded row count, checksum, and spill bytes certified |
| Scalar subquery cache | Correlated subquery LRU cache | 50k rows | Row count, checksum, and exact hit/miss counts certified |
| Spill cleanup after success | Non-persistent temp-table spill lifecycle | 50k rows | Spill directory removed after evaluator disposal |
| Spill cleanup after failure | Non-persistent temp-table spill lifecycle | 50k rows | Forced source failure still removes spill directory after evaluator disposal |
