# ETL-SQL Scale Certification Report

Generated: 2026-07-12 16:02:43  |  Tier: **Smoke**  |  Row scale: **1x**  |  Samples: **1**

## Results

| Scenario | Samples | Rows | Rows/s | Elapsed (ms) | Spill Write | Peak WS (MB) | Private (MB) | Heap (MB) | Allocated (MB) | CPU % | GC Pause (ms) | Bound (MB) | Pass |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| CsvIngest_50000 | 1 | 50000 | 1388888.9 | 36 | 0 | 474.6 | 508.1 | 105.4 | 51.3 | 3.9 | 0 | 1024 | OK |
| CubeGroupingSets_50000_10x5 | 1 | 50000 | 33534.5 | 1491 | 22400000 | 324.3 | 270.7 | 105.9 | 681.3 | 6.2 | 80.2 | 1024 | OK |
| ExternalAggregate_100000_10grps | 1 | 100000 | 275482.1 | 363 | 8000000 | 425.7 | 402.2 | 114.4 | 229.6 | 6.1 | 24 | 1024 | OK |
| ExternalJoin_50000_equality | 1 | 50000 | 97465.9 | 513 | 8800000 | 404.9 | 363.4 | 114.3 | 435.9 | 5.5 | 42.7 | 1024 | OK |
| ExternalSort_50000_DESC | 1 | 50000 | 79744.8 | 627 | 8000000 | 500.3 | 537.3 | 124 | 405.8 | 9.3 | 18 | 1024 | OK |
| ParquetRoundTrip_50000 | 1 | 50000 | 568181.8 | 88 | 0 | 423.8 | 394.3 | 78 | 52.6 | 4.1 | 0 | 1024 | OK |
| ReportDatasetSnapshotReload_50000 | 1 | 50000 | 326797.4 | 153 | 0 | 491.1 | 525.2 | 148.5 | 220 | 7.3 | 16.2 | 1024 | OK |
| ScalarSubqueryCache_50000_1000keys | 1 | 50000 | 88968 | 562 | 4000000 | 453.1 | 425.7 | 109.1 | 315.7 | 6.8 | 28.4 | 1024 | OK |
| SpillCleanupFailure_50000 | 1 | 50000 | 2083333.3 | 24 | 331776 | 198 | 141.3 | 36.5 | 13.6 | 4.1 | 0 | 1024 | OK |
| SpillCleanupSuccess_50000 | 1 | 50000 | 1785714.3 | 28 | 1327104 | 489.8 | 524 | 104.2 | 36.2 | 3.9 | 0 | 1024 | OK |
| StreamingSelect_100000_cap50000 | 1 | 100000 | 15595.8 | 6412 | 0 | 416.9 | 398.1 | 144.6 | 506.4 | 1.7 | 53.3 | 1024 | OK |
| TempTableSpill_50000_SELECT_INTO | 1 | 50000 | 197628.5 | 253 | 1327104 | 189.5 | 135.1 | 28.9 | 37.5 | 4.7 | 0 | 1024 | OK |
| WindowFunction_ROW_NUMBER_50000 | 1 | 50000 | 63857 | 783 | 11200000 | 353.1 | 299.2 | 80.6 | 553.4 | 4.7 | 30.2 | 1024 | OK |

## Environment

- OS: Microsoft Windows 11 Home 10.0.26200
- CPU: Intel(R) Core(TM) Ultra 9 275HX (24 logical cores)
- RAM: 31.4 GB
- Disk: NVMe MTFDKBA1T0QGN-1BN1AABGA, 953.9 GB; workspace free 253.1 GB
- Runtime: .NET 10.0.9, X64, Release, server GC enabled: True
- Engine memory grant: 2048 MB
- Commit: cfa51038a3b7642d2ccc700ee2c89de8ed8a96b8 (release/v0.15.0); dirty: True
- Source fingerprint: df0b8547d6bf03f2bfdb647aefbc4f0799167d7071864f344b5003efa3ce9a16
- Config fingerprint: 94e45790da45be168497c5d2f154c58b92c9e493a37355aff14a4d30c303c886

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
