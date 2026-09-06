# ETL-SQL Scale Certification Report

Generated: 2026-09-06 14:01:23  |  Tier: **Standard**  |  Row scale: **10x**  |  Samples: **3**

## Results

| Scenario | Samples | Rows | Rows/s | Elapsed (ms) | Spill Write | Peak WS (MB) | Private (MB) | Heap (MB) | Allocated (MB) | CPU % | GC Pause (ms) | Bound (MB) | Pass |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| CsvIngest_500000 | 3 | 500000 | 1748251.7 | 286 | 0 | 994.9 | 1232.3 | 513.3 | 532.1 | 22 | 155.1 | 4096 | OK |
| CubeGroupingSets_500000_10x5 | 3 | 500000 | 109241.9 | 4577 | 224000000 | 1422 | 1634.7 | 728.7 | 4208.7 | 8.9 | 249.7 | 4096 | OK |
| ExternalAggregate_1000000_10grps | 3 | 1000000 | 520291.4 | 1922 | 80000000 | 743.5 | 751 | 295.9 | 1118.4 | 7.7 | 128.5 | 4096 | OK |
| ExternalJoin_500000_equality | 3 | 500000 | 167336 | 2988 | 88000000 | 881.5 | 1139.4 | 401.8 | 3143.4 | 10.7 | 347 | 4096 | OK |
| ExternalSort_500000_DESC | 3 | 500000 | 105641.2 | 4733 | 119997440 | 565.2 | 519.9 | 227.9 | 2081.6 | 6.2 | 119 | 4096 | OK |
| ParquetRoundTrip_500000 | 3 | 500000 | 1865671.6 | 268 | 0 | 1011.3 | 1244.5 | 488.7 | 455.8 | 10.1 | 104.1 | 4096 | OK |
| ReportDatasetSnapshotReload_500000 | 3 | 500000 | 659630.6 | 758 | 0 | 1306.8 | 1533.1 | 753.7 | 1522.4 | 10.1 | 193.2 | 4096 | OK |
| ScalarSubqueryCache_500000_1000keys | 3 | 500000 | 302480.3 | 1653 | 40000000 | 1428.9 | 1642.6 | 741.4 | 1569.3 | 8.4 | 223.8 | 4096 | OK |
| SpillCleanupFailure_500000 | 3 | 500000 | 29411764.7 | 17 | 331776 | 1358.2 | 1569.4 | 680.3 | 15.9 | 28.2 | 145.9 | 4096 | OK |
| SpillCleanupSuccess_500000 | 3 | 500000 | 2252252.3 | 222 | 16257024 | 1392.4 | 1603.8 | 681.1 | 333.9 | 23.6 | 156.2 | 4096 | OK |
| StreamingSelect_1000000_cap50000 | 3 | 1000000 | 139489.5 | 7169 | 0 | 816.5 | 1070.6 | 344.6 | 67.2 | 0.8 | 78.2 | 4096 | OK |
| TempTableSpill_500000_SELECT_INTO | 3 | 500000 | 1602564.1 | 312 | 16257024 | 826.5 | 1078.7 | 355.9 | 334.4 | 11 | 96.6 | 4096 | OK |
| WindowFunction_ROW_NUMBER_500000 | 3 | 500000 | 147536.1 | 3389 | 112000000 | 1005.1 | 1255.1 | 481.9 | 3422.2 | 8.1 | 217.4 | 4096 | OK |

## Environment

- OS: Microsoft Windows 11 Home 10.0.26200
- CPU: Intel(R) Core(TM) Ultra 9 275HX (24 logical cores)
- RAM: 31.4 GB
- Disk: NVMe MTFDKBA1T0QGN-1BN1AABGA, 953.9 GB; workspace free 202.2 GB
- Runtime: .NET 10.0.11, X64, Release, server GC enabled: True
- Engine memory grant: 2048 MB
- Commit: c6f04b1a4172a9854e888ed3a09043acd77e758a (release/v0.19.0); dirty: True
- Source fingerprint: 5195cb87bd4f24bece7122213f2bce94ffc4a884db4d9647c07504890cd3d30c
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
