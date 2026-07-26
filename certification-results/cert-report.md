# ETL-SQL Scale Certification Report

Generated: 2026-07-25 19:31:20  |  Tier: **Standard**  |  Row scale: **10x**  |  Samples: **3**

## Results

| Scenario | Samples | Rows | Rows/s | Elapsed (ms) | Spill Write | Peak WS (MB) | Private (MB) | Heap (MB) | Allocated (MB) | CPU % | GC Pause (ms) | Bound (MB) | Pass |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| CsvIngest_500000 | 3 | 500000 | 1736111.1 | 288 | 0 | 919.2 | 996.1 | 438.8 | 522.7 | 19.8 | 143.3 | 4096 | OK |
| CubeGroupingSets_500000_10x5 | 3 | 500000 | 114836.9 | 4354 | 224000000 | 1302.7 | 1373.5 | 637.6 | 4163.9 | 8.4 | 230.6 | 4096 | OK |
| ExternalAggregate_1000000_10grps | 3 | 1000000 | 556483 | 1797 | 80000000 | 587.8 | 621.8 | 228.8 | 1094 | 6.8 | 143.4 | 4096 | OK |
| ExternalJoin_500000_equality | 3 | 500000 | 173671.4 | 2879 | 88000000 | 842.1 | 943.7 | 332.7 | 3044.1 | 8.7 | 342.8 | 4096 | OK |
| ExternalSort_500000_DESC | 3 | 500000 | 121743.4 | 4107 | 119997440 | 488.1 | 462.5 | 239.3 | 2041.6 | 6.2 | 195.6 | 4096 | OK |
| ParquetRoundTrip_500000 | 3 | 500000 | 2000000 | 250 | 0 | 947.7 | 1027.1 | 424.1 | 448.8 | 11.9 | 105 | 4096 | OK |
| ReportDatasetSnapshotReload_500000 | 3 | 500000 | 581395.3 | 860 | 0 | 1187.7 | 1282 | 762.7 | 1477.2 | 13 | 208.2 | 4096 | OK |
| ScalarSubqueryCache_500000_1000keys | 3 | 500000 | 537634.4 | 930 | 40000000 | 1314.5 | 1573.7 | 669.3 | 1434.5 | 9.6 | 213.8 | 4096 | OK |
| SpillCleanupFailure_500000 | 3 | 500000 | 31250000 | 16 | 331776 | 1277.8 | 1534.5 | 614.9 | 13.8 | 43.5 | 123 | 4096 | OK |
| SpillCleanupSuccess_500000 | 3 | 500000 | 1845018.5 | 271 | 16257024 | 1277.8 | 1534.5 | 614.9 | 320.9 | 20 | 145.7 | 4096 | OK |
| StreamingSelect_1000000_cap50000 | 3 | 1000000 | 170852.6 | 5853 | 0 | 782.5 | 885 | 316.1 | 64.2 | 0.5 | 60.1 | 4096 | OK |
| TempTableSpill_500000_SELECT_INTO | 3 | 500000 | 1404494.4 | 356 | 16257024 | 795.7 | 897.2 | 316.1 | 320.8 | 9.9 | 116.1 | 4096 | OK |
| WindowFunction_ROW_NUMBER_500000 | 3 | 500000 | 186428 | 2682 | 112000000 | 891 | 977.8 | 472.7 | 3378.6 | 7.7 | 195.2 | 4096 | OK |

## Environment

- OS: Microsoft Windows 11 Home 10.0.26200
- CPU: Intel(R) Core(TM) Ultra 9 275HX (24 logical cores)
- RAM: 31.4 GB
- Disk: NVMe MTFDKBA1T0QGN-1BN1AABGA, 953.9 GB; workspace free 320.4 GB
- Runtime: .NET 10.0.9, X64, Release, server GC enabled: True
- Engine memory grant: 2048 MB
- Commit: 86564d4ba43c84cf082de6f1aacf548eb27faa8e (release/v0.17.0); dirty: False
- Source fingerprint: dec74e4febeec5ceca7d64368407c9b78b85f1e3be72acba788a37fe5d422cfd
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
