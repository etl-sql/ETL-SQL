# ETL-SQL Scale Certification Report

Generated: 2026-08-20 18:09:07  |  Tier: **Standard**  |  Row scale: **10x**  |  Samples: **3**

## Results

| Scenario | Samples | Rows | Rows/s | Elapsed (ms) | Spill Write | Peak WS (MB) | Private (MB) | Heap (MB) | Allocated (MB) | CPU % | GC Pause (ms) | Bound (MB) | Pass |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| CsvIngest_500000 | 3 | 500000 | 1718213.1 | 291 | 0 | 966.4 | 1050.2 | 473.5 | 531.4 | 21.4 | 121.5 | 4096 | OK |
| CubeGroupingSets_500000_10x5 | 3 | 500000 | 114889.7 | 4352 | 224000000 | 1348 | 1416.5 | 717.4 | 4210 | 8.6 | 239.4 | 4096 | OK |
| ExternalAggregate_1000000_10grps | 3 | 1000000 | 561482.3 | 1781 | 80000000 | 685.9 | 700.8 | 298.5 | 1118 | 8.7 | 149.1 | 4096 | OK |
| ExternalJoin_500000_equality | 3 | 500000 | 180310.1 | 2773 | 88000000 | 857.2 | 956.2 | 346.8 | 3149.5 | 9.9 | 327.9 | 4096 | OK |
| ExternalSort_500000_DESC | 3 | 500000 | 106974.8 | 4674 | 119997440 | 523.4 | 486.4 | 204.8 | 2080.9 | 6.2 | 138.7 | 4096 | OK |
| ParquetRoundTrip_500000 | 3 | 500000 | 1893939.4 | 264 | 0 | 993.9 | 1079.4 | 464.5 | 455.6 | 10.2 | 106.5 | 4096 | OK |
| ReportDatasetSnapshotReload_500000 | 3 | 500000 | 668449.2 | 748 | 0 | 1233.8 | 1316.5 | 855.9 | 1521.3 | 13.5 | 200.7 | 4096 | OK |
| ScalarSubqueryCache_500000_1000keys | 3 | 500000 | 319488.8 | 1565 | 40000000 | 1395 | 1635.5 | 719.4 | 1569 | 9 | 209.8 | 4096 | OK |
| SpillCleanupFailure_500000 | 3 | 500000 | 33333333.3 | 15 | 331776 | 1335 | 1574.8 | 659.6 | 15.3 | 35.5 | 131.6 | 4096 | OK |
| SpillCleanupSuccess_500000 | 3 | 500000 | 2183406.1 | 229 | 16257024 | 1351 | 1590.7 | 660.7 | 333.5 | 23.1 | 153.4 | 4096 | OK |
| StreamingSelect_1000000_cap50000 | 3 | 1000000 | 217108.1 | 4606 | 0 | 760.4 | 854.3 | 225.2 | 66.2 | 0.5 | 63.5 | 4096 | OK |
| TempTableSpill_500000_SELECT_INTO | 3 | 500000 | 1628664.5 | 307 | 16257024 | 822 | 920.9 | 327.7 | 333.2 | 14.5 | 77.5 | 4096 | OK |
| WindowFunction_ROW_NUMBER_500000 | 3 | 500000 | 154750.9 | 3231 | 112000000 | 936.9 | 1028.5 | 490.1 | 3421.7 | 7 | 224.7 | 4096 | OK |

## Environment

- OS: Microsoft Windows 11 Home 10.0.26200
- CPU: Intel(R) Core(TM) Ultra 9 275HX (24 logical cores)
- RAM: 31.4 GB
- Disk: NVMe MTFDKBA1T0QGN-1BN1AABGA, 953.9 GB; workspace free 284.6 GB
- Runtime: .NET 10.0.11, X64, Release, server GC enabled: True
- Engine memory grant: 2048 MB
- Commit: 4084ea83833d44cf3d406014e4ca3fbd8d22fda1 (release/v0.18.0); dirty: True
- Source fingerprint: 5736b4d918fb599a2695ff9052e39e2b8466f5499899dd9d81e805a0850d83bd
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
