# ETL-SQL Scale Certification Report

Generated: 2026-08-20 11:03:00  |  Tier: **Standard**  |  Row scale: **10x**  |  Samples: **3**

## Results

| Scenario | Samples | Rows | Rows/s | Elapsed (ms) | Spill Write | Peak WS (MB) | Private (MB) | Heap (MB) | Allocated (MB) | CPU % | GC Pause (ms) | Bound (MB) | Pass |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| CsvIngest_500000 | 3 | 500000 | 1831501.8 | 273 | 0 | 960.5 | 1043.1 | 475.8 | 531.4 | 19.4 | 125.3 | 4096 | OK |
| CubeGroupingSets_500000_10x5 | 3 | 500000 | 96880.4 | 5161 | 224000000 | 1358.4 | 1426.6 | 677.3 | 4206.2 | 8 | 245 | 4096 | OK |
| ExternalAggregate_1000000_10grps | 3 | 1000000 | 474383.3 | 2108 | 80000000 | 664 | 688.6 | 303.7 | 1118 | 7.2 | 138.7 | 4096 | OK |
| ExternalJoin_500000_equality | 3 | 500000 | 151607 | 3298 | 88000000 | 873.2 | 967.7 | 385.6 | 3146 | 10.3 | 340.2 | 4096 | OK |
| ExternalSort_500000_DESC | 3 | 500000 | 94768.8 | 5276 | 119997440 | 533.9 | 541.7 | 208.8 | 2081 | 6.2 | 132.2 | 4096 | OK |
| ParquetRoundTrip_500000 | 3 | 500000 | 1748251.7 | 286 | 0 | 969.2 | 1052.2 | 459.2 | 456.3 | 14.4 | 106.3 | 4096 | OK |
| ReportDatasetSnapshotReload_500000 | 3 | 500000 | 572737.7 | 873 | 0 | 1219.9 | 1301.9 | 867.9 | 1520.5 | 12.7 | 185 | 4096 | OK |
| ScalarSubqueryCache_500000_1000keys | 3 | 500000 | 253036.4 | 1976 | 40000000 | 1439.5 | 1687.2 | 720.8 | 1568.7 | 7.2 | 213 | 4096 | OK |
| SpillCleanupFailure_500000 | 3 | 500000 | 26315789.5 | 19 | 331776 | 1361.7 | 1604.8 | 662.4 | 15.3 | 35.1 | 133.1 | 4096 | OK |
| SpillCleanupSuccess_500000 | 3 | 500000 | 1712328.8 | 292 | 16257024 | 1392.1 | 1640.1 | 719.1 | 333.6 | 19.6 | 154.4 | 4096 | OK |
| StreamingSelect_1000000_cap50000 | 3 | 1000000 | 127551 | 7840 | 0 | 761.4 | 859.4 | 240.3 | 66.5 | 0.7 | 72.3 | 4096 | OK |
| TempTableSpill_500000_SELECT_INTO | 3 | 500000 | 1377410.5 | 363 | 16257024 | 792.4 | 894.4 | 333.1 | 333.2 | 18.4 | 88.6 | 4096 | OK |
| WindowFunction_ROW_NUMBER_500000 | 3 | 500000 | 126774.8 | 3944 | 112000000 | 923.8 | 1016.1 | 377.1 | 3421.5 | 7.3 | 230.2 | 4096 | OK |

## Environment

- OS: Microsoft Windows 11 Home 10.0.26200
- CPU: Intel(R) Core(TM) Ultra 9 275HX (24 logical cores)
- RAM: 31.4 GB
- Disk: NVMe MTFDKBA1T0QGN-1BN1AABGA, 953.9 GB; workspace free 291.3 GB
- Runtime: .NET 10.0.11, X64, Release, server GC enabled: True
- Engine memory grant: 2048 MB
- Commit: 5c2b4cff43acc765a275b2ea94213fd16b2d05c7 (release/v0.18.0); dirty: True
- Source fingerprint: 058f0a6cf1c10fb2c1ba7bc3d4c100bed72b5ff4c7b292b69cefde53613f771b
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
