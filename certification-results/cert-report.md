# ETL-SQL Scale Certification Report

Generated: 2026-07-18 09:46:10  |  Tier: **Smoke**  |  Row scale: **1x**  |  Samples: **1**

## Results

| Scenario | Samples | Rows | Rows/s | Elapsed (ms) | Spill Write | Peak WS (MB) | Private (MB) | Heap (MB) | Allocated (MB) | CPU % | GC Pause (ms) | Bound (MB) | Pass |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| CsvIngest_50000 | 1 | 50000 | 1282051.3 | 39 | 0 | 433 | 368.7 | 105.8 | 51.3 | 3.7 | 0 | 1024 | OK |
| CubeGroupingSets_50000_10x5 | 1 | 50000 | 25523.2 | 1959 | 22400000 | 310.5 | 253.8 | 83.4 | 441.1 | 5.4 | 42.8 | 1024 | OK |
| ExternalAggregate_100000_10grps | 1 | 100000 | 409836.1 | 244 | 8000000 | 355.7 | 300.8 | 78.3 | 117.3 | 4.2 | 0 | 1024 | OK |
| ExternalJoin_50000_equality | 1 | 50000 | 67385.4 | 742 | 8800000 | 356.2 | 306.3 | 107 | 316.5 | 5 | 33.4 | 1024 | OK |
| ExternalSort_50000_DESC | 1 | 50000 | 201612.9 | 248 | 8000000 | 462.8 | 398.7 | 121.8 | 166.6 | 6 | 9.4 | 1024 | OK |
| ParquetRoundTrip_50000 | 1 | 50000 | 342465.8 | 146 | 0 | 357.8 | 296.6 | 79.5 | 53.2 | 2.5 | 0 | 1024 | OK |
| ReportDatasetSnapshotReload_50000 | 1 | 50000 | 306748.5 | 163 | 0 | 483 | 422.4 | 105.8 | 165.1 | 4.1 | 0 | 1024 | OK |
| ScalarSubqueryCache_50000_1000keys | 1 | 50000 | 95602.3 | 523 | 4000000 | 382.6 | 323.1 | 95.7 | 259.6 | 5.5 | 9.7 | 1024 | OK |
| SpillCleanupFailure_50000 | 1 | 50000 | 943396.2 | 53 | 331776 | 196.1 | 139.2 | 38.6 | 14.3 | 3.8 | 0 | 1024 | OK |
| SpillCleanupSuccess_50000 | 1 | 50000 | 1388888.9 | 36 | 1327104 | 455.4 | 392 | 103.9 | 36.9 | 3.4 | 0 | 1024 | OK |
| StreamingSelect_100000_cap50000 | 1 | 100000 | 14543.3 | 6876 | 0 | 355.8 | 298.5 | 70.2 | 65.2 | 0.9 | 0 | 1024 | OK |
| TempTableSpill_50000_SELECT_INTO | 1 | 50000 | 127551 | 392 | 1327104 | 198.6 | 144.5 | 37.3 | 38.2 | 3.7 | 0 | 1024 | OK |
| WindowFunction_ROW_NUMBER_50000 | 1 | 50000 | 35816.6 | 1396 | 11200000 | 328.5 | 285.7 | 170.8 | 344.9 | 6 | 29.4 | 1024 | OK |

## Environment

- OS: Microsoft Windows 11 Home 10.0.26200
- CPU: Intel(R) Core(TM) Ultra 9 275HX (24 logical cores)
- RAM: 31.4 GB
- Disk: NVMe MTFDKBA1T0QGN-1BN1AABGA, 953.9 GB; workspace free 250.6 GB
- Runtime: .NET 10.0.9, X64, Release, server GC enabled: True
- Engine memory grant: 2048 MB
- Commit: e084d9e9d0d06b1f19978f9245b54e5d4c595710 (release/v0.16.0); dirty: True
- Source fingerprint: 5f27577115157c2664553b5943b25b43645591192e1d8ce8f1d8e38e6d5198f4
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
