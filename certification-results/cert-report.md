# ETL-SQL Scale Certification Report

Generated: 2026-08-20 18:01:00  |  Tier: **Smoke**  |  Row scale: **1x**  |  Samples: **3**

## Results

| Scenario | Samples | Rows | Rows/s | Elapsed (ms) | Spill Write | Peak WS (MB) | Private (MB) | Heap (MB) | Allocated (MB) | CPU % | GC Pause (ms) | Bound (MB) | Pass |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| CsvIngest_50000 | 3 | 50000 | 1136363.6 | 44 | 0 | 505.9 | 470.9 | 129 | 52 | 3.6 | 0 | 1024 | OK |
| CubeGroupingSets_50000_10x5 | 3 | 50000 | 23223.4 | 2153 | 22400000 | 383.3 | 320.5 | 94 | 448.4 | 5.9 | 33.5 | 1024 | OK |
| ExternalAggregate_100000_10grps | 3 | 100000 | 423728.8 | 236 | 8000000 | 423.1 | 393.8 | 95.8 | 121.1 | 4.6 | 0 | 1024 | OK |
| ExternalJoin_50000_equality | 3 | 50000 | 76219.5 | 656 | 8800000 | 442.2 | 417.5 | 128.8 | 330.4 | 6 | 29.5 | 1024 | OK |
| ExternalSort_50000_DESC | 3 | 50000 | 224215.2 | 223 | 8000000 | 533.5 | 498.9 | 116.6 | 170.9 | 4.6 | 0 | 1024 | OK |
| ParquetRoundTrip_50000 | 3 | 50000 | 568181.8 | 88 | 0 | 427.5 | 392 | 97.6 | 54.9 | 4 | 0 | 1024 | OK |
| ReportDatasetSnapshotReload_50000 | 3 | 50000 | 303030.3 | 165 | 0 | 524.1 | 490.2 | 129 | 171 | 4.2 | 0 | 1024 | OK |
| ScalarSubqueryCache_50000_1000keys | 3 | 50000 | 93633 | 534 | 4000000 | 481.1 | 445.9 | 117.4 | 281.4 | 5.5 | 7.2 | 1024 | OK |
| SpillCleanupFailure_50000 | 3 | 50000 | 1923076.9 | 26 | 331776 | 229.9 | 167.6 | 53.1 | 15.9 | 4.6 | 0 | 1024 | OK |
| SpillCleanupSuccess_50000 | 3 | 50000 | 1612903.2 | 31 | 1327104 | 502 | 467.3 | 127.4 | 39.2 | 5.5 | 0 | 1024 | OK |
| StreamingSelect_100000_cap50000 | 3 | 100000 | 14300 | 6993 | 0 | 419.5 | 387.8 | 86.9 | 66.4 | 0.9 | 0 | 1024 | OK |
| TempTableSpill_50000_SELECT_INTO | 3 | 50000 | 177305 | 282 | 1327104 | 225.8 | 167.5 | 45.5 | 40.6 | 5.2 | 0 | 1024 | OK |
| WindowFunction_ROW_NUMBER_50000 | 3 | 50000 | 35410.8 | 1412 | 11200000 | 409.9 | 365.5 | 98.9 | 350.3 | 5.5 | 17.4 | 1024 | OK |

## Environment

- OS: Microsoft Windows 11 Home 10.0.26200
- CPU: Intel(R) Core(TM) Ultra 9 275HX (24 logical cores)
- RAM: 31.4 GB
- Disk: NVMe MTFDKBA1T0QGN-1BN1AABGA, 953.9 GB; workspace free 284.6 GB
- Runtime: .NET 10.0.11, X64, Release, server GC enabled: True
- Engine memory grant: 2048 MB
- Commit: cfcf5c4d7570f7514e9dbb20b1c6344ea9b825e0 (release/v0.18.0); dirty: True
- Source fingerprint: e119456906d2c853bec28267cb5c7e3642b5834a35fdda505d1c3ef5dabeca8a
- Config fingerprint: ccdb168aca26dfd294fc82ed735d51f67f7e43041b979389b822d364dd824e41

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
