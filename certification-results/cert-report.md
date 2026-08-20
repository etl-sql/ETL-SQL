# ETL-SQL Scale Certification Report

Generated: 2026-08-20 09:08:14  |  Tier: **Standard**  |  Row scale: **10x**  |  Samples: **3**

## Results

| Scenario | Samples | Rows | Rows/s | Elapsed (ms) | Spill Write | Peak WS (MB) | Private (MB) | Heap (MB) | Allocated (MB) | CPU % | GC Pause (ms) | Bound (MB) | Pass |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| CsvIngest_500000 | 3 | 500000 | 1712328.8 | 292 | 0 | 955.9 | 1039.5 | 475.8 | 531.4 | 18.8 | 136.5 | 4096 | OK |
| CubeGroupingSets_500000_10x5 | 3 | 500000 | 115526.8 | 4328 | 224000000 | 1363.8 | 1427.9 | 671.3 | 4219.3 | 8.3 | 247.8 | 4096 | OK |
| ExternalAggregate_1000000_10grps | 3 | 1000000 | 564334.1 | 1772 | 80000000 | 655.5 | 668.5 | 336.5 | 1117.6 | 7.7 | 148.9 | 4096 | OK |
| ExternalJoin_500000_equality | 3 | 500000 | 178954.9 | 2794 | 88000000 | 881.8 | 970 | 405.4 | 3150.2 | 10.2 | 321.4 | 4096 | OK |
| ExternalSort_500000_DESC | 3 | 500000 | 107805.1 | 4638 | 119997440 | 497.8 | 527.5 | 209.3 | 2080.9 | 6.3 | 137.8 | 4096 | OK |
| ParquetRoundTrip_500000 | 3 | 500000 | 1901140.7 | 263 | 0 | 987.5 | 1068.9 | 475.8 | 456.1 | 12.9 | 106.1 | 4096 | OK |
| ReportDatasetSnapshotReload_500000 | 3 | 500000 | 670241.3 | 746 | 0 | 1218.3 | 1297.6 | 906.2 | 1521.4 | 14.1 | 193.1 | 4096 | OK |
| ScalarSubqueryCache_500000_1000keys | 3 | 500000 | 318674.3 | 1569 | 40000000 | 1408.8 | 1656.9 | 718.2 | 1568.4 | 8 | 200.6 | 4096 | OK |
| SpillCleanupFailure_500000 | 3 | 500000 | 31250000 | 16 | 331776 | 1357.2 | 1602.3 | 657.8 | 15.3 | 38.3 | 140.8 | 4096 | OK |
| SpillCleanupSuccess_500000 | 3 | 500000 | 2272727.3 | 220 | 16257024 | 1357.2 | 1602.3 | 658.1 | 333.5 | 28.2 | 140 | 4096 | OK |
| StreamingSelect_1000000_cap50000 | 3 | 1000000 | 200441 | 4989 | 0 | 820.9 | 913.6 | 335.7 | 66.2 | 0.6 | 81.4 | 4096 | OK |
| TempTableSpill_500000_SELECT_INTO | 3 | 500000 | 1628664.5 | 307 | 16257024 | 838.2 | 929.1 | 335.7 | 333.2 | 12 | 97.7 | 4096 | OK |
| WindowFunction_ROW_NUMBER_500000 | 3 | 500000 | 155666.3 | 3212 | 112000000 | 939 | 1029.2 | 448.7 | 3421.4 | 8.2 | 238.7 | 4096 | OK |

## Environment

- OS: Microsoft Windows 11 Home 10.0.26200
- CPU: Intel(R) Core(TM) Ultra 9 275HX (24 logical cores)
- RAM: 31.4 GB
- Disk: NVMe MTFDKBA1T0QGN-1BN1AABGA, 953.9 GB; workspace free 292.9 GB
- Runtime: .NET 10.0.11, X64, Release, server GC enabled: True
- Engine memory grant: 2048 MB
- Commit: 08739c50dfefb31cdd6454ed90c73bde9a7c881b (release/v0.18.0); dirty: True
- Source fingerprint: 013d8fd00beed8ef4e823cd9acf3c1def1da99e771bdedc018866aac4951dcf6
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
