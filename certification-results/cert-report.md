# ETL-SQL Scale Certification Report

Generated: 2026-09-06 13:20:14  |  Tier: **Standard**  |  Row scale: **10x**  |  Samples: **3**

## Results

| Scenario | Samples | Rows | Rows/s | Elapsed (ms) | Spill Write | Peak WS (MB) | Private (MB) | Heap (MB) | Allocated (MB) | CPU % | GC Pause (ms) | Bound (MB) | Pass |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| CsvIngest_500000 | 3 | 500000 | 1689189.2 | 296 | 0 | 1051.9 | 1233.3 | 536.8 | 532.1 | 19.6 | 151.3 | 4096 | OK |
| CubeGroupingSets_500000_10x5 | 3 | 500000 | 109721.3 | 4557 | 224000000 | 1399 | 1622 | 719.1 | 4205.5 | 8.5 | 271.1 | 4096 | OK |
| ExternalAggregate_1000000_10grps | 3 | 1000000 | 534759.4 | 1870 | 80000000 | 718.9 | 731.1 | 431.6 | 1118.3 | 6.3 | 109.1 | 4096 | OK |
| ExternalJoin_500000_equality | 3 | 500000 | 168293.5 | 2971 | 88000000 | 931.9 | 1018.6 | 373.4 | 3147.8 | 9.2 | 338.3 | 4096 | OK |
| ExternalSort_500000_DESC | 3 | 500000 | 101605.4 | 4921 | 119997440 | 530 | 556.5 | 277 | 2081.6 | 6.1 | 229.6 | 4096 | OK |
| ParquetRoundTrip_500000 | 3 | 500000 | 1845018.5 | 271 | 0 | 1034.1 | 1241.9 | 499.7 | 456.1 | 17.5 | 105.1 | 4096 | OK |
| ReportDatasetSnapshotReload_500000 | 3 | 500000 | 647668.4 | 772 | 0 | 1316.7 | 1525.4 | 719.7 | 1522.4 | 15.6 | 202.4 | 4096 | OK |
| ScalarSubqueryCache_500000_1000keys | 3 | 500000 | 305810.4 | 1635 | 40000000 | 1429.8 | 1650.7 | 727.4 | 1569.3 | 9.1 | 226.8 | 4096 | OK |
| SpillCleanupFailure_500000 | 3 | 500000 | 29411764.7 | 17 | 331776 | 1360 | 1570.4 | 673.3 | 16 | 35.1 | 134.8 | 4096 | OK |
| SpillCleanupSuccess_500000 | 3 | 500000 | 2232142.9 | 224 | 16257024 | 1384.6 | 1596.4 | 673.7 | 334.5 | 21.3 | 147.1 | 4096 | OK |
| StreamingSelect_1000000_cap50000 | 3 | 1000000 | 134589.5 | 7430 | 0 | 845.4 | 923.7 | 272.7 | 67.2 | 0.6 | 73.6 | 4096 | OK |
| TempTableSpill_500000_SELECT_INTO | 3 | 500000 | 1562500 | 320 | 16257024 | 931.9 | 1018.6 | 381.1 | 334.4 | 13.9 | 100.1 | 4096 | OK |
| WindowFunction_ROW_NUMBER_500000 | 3 | 500000 | 146886 | 3404 | 112000000 | 1048 | 1081.5 | 467.8 | 3422.1 | 8.2 | 211.5 | 4096 | OK |

## Environment

- OS: Microsoft Windows 11 Home 10.0.26200
- CPU: Intel(R) Core(TM) Ultra 9 275HX (24 logical cores)
- RAM: 31.4 GB
- Disk: NVMe MTFDKBA1T0QGN-1BN1AABGA, 953.9 GB; workspace free 202.3 GB
- Runtime: .NET 10.0.11, X64, Release, server GC enabled: True
- Engine memory grant: 2048 MB
- Commit: 9ba3ac21a1bd3c5c89666d85f64f73f99e4125ae (release/v0.19.0); dirty: True
- Source fingerprint: df98968838740d9881fe3ea1afeea270c1e8b5ac7b71fd7969e9d2f6c094095a
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
