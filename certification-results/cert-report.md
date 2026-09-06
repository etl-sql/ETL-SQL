# ETL-SQL Scale Certification Report

Generated: 2026-09-06 10:09:27  |  Tier: **Standard**  |  Row scale: **10x**  |  Samples: **3**

## Results

| Scenario | Samples | Rows | Rows/s | Elapsed (ms) | Spill Write | Peak WS (MB) | Private (MB) | Heap (MB) | Allocated (MB) | CPU % | GC Pause (ms) | Bound (MB) | Pass |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| CsvIngest_500000 | 3 | 500000 | 1689189.2 | 296 | 0 | 995.4 | 1176 | 510.1 | 532 | 18.1 | 139.7 | 4096 | OK |
| CubeGroupingSets_500000_10x5 | 3 | 500000 | 109098.8 | 4583 | 224000000 | 1403.8 | 1549.9 | 695.1 | 4207.4 | 8.1 | 275.1 | 4096 | OK |
| ExternalAggregate_1000000_10grps | 3 | 1000000 | 554016.6 | 1805 | 80000000 | 698 | 719.2 | 418.9 | 1118.7 | 7.8 | 122 | 4096 | OK |
| ExternalJoin_500000_equality | 3 | 500000 | 170823.4 | 2927 | 88000000 | 905.6 | 999.5 | 418.9 | 3147.2 | 10 | 336 | 4096 | OK |
| ExternalSort_500000_DESC | 3 | 500000 | 111607.1 | 4480 | 119997440 | 557.2 | 514.9 | 267.9 | 2080.8 | 6.2 | 193.1 | 4096 | OK |
| ParquetRoundTrip_500000 | 3 | 500000 | 1886792.5 | 265 | 0 | 1008.5 | 1187.2 | 488.4 | 455.9 | 10.4 | 105.9 | 4096 | OK |
| ReportDatasetSnapshotReload_500000 | 3 | 500000 | 647668.4 | 772 | 0 | 1316.6 | 1454.4 | 728.8 | 1521.8 | 12.5 | 201.9 | 4096 | OK |
| ScalarSubqueryCache_500000_1000keys | 3 | 500000 | 312500 | 1600 | 40000000 | 1394.5 | 1637.7 | 733.5 | 1569.5 | 9.5 | 208 | 4096 | OK |
| SpillCleanupFailure_500000 | 3 | 500000 | 31250000 | 16 | 331776 | 1350.3 | 1588.8 | 675.3 | 16 | 43.4 | 134.5 | 4096 | OK |
| SpillCleanupSuccess_500000 | 3 | 500000 | 2262443.4 | 221 | 16257024 | 1350.3 | 1588.8 | 674.2 | 334 | 20.4 | 148.8 | 4096 | OK |
| StreamingSelect_1000000_cap50000 | 3 | 1000000 | 141582.9 | 7063 | 0 | 788.4 | 878.8 | 270.5 | 67.5 | 0.7 | 79.2 | 4096 | OK |
| TempTableSpill_500000_SELECT_INTO | 3 | 500000 | 1577287.1 | 317 | 16257024 | 838.6 | 932 | 371.5 | 334.2 | 16.6 | 93.6 | 4096 | OK |
| WindowFunction_ROW_NUMBER_500000 | 3 | 500000 | 150285.5 | 3327 | 112000000 | 1003.2 | 1050.6 | 518.1 | 3422.6 | 8.5 | 210.5 | 4096 | OK |

## Environment

- OS: Microsoft Windows 11 Home 10.0.26200
- CPU: Intel(R) Core(TM) Ultra 9 275HX (24 logical cores)
- RAM: 31.4 GB
- Disk: NVMe MTFDKBA1T0QGN-1BN1AABGA, 953.9 GB; workspace free 114.3 GB
- Runtime: .NET 10.0.11, X64, Release, server GC enabled: True
- Engine memory grant: 2048 MB
- Commit: 6c07e4f8d3afe37f28a8df49731671f74b66bb84 (release/v0.19.0); dirty: True
- Source fingerprint: 710fc0eb48c7a463fd18f19d015fdd568ba5c3760e71f154388282ca2aa0a9cd
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
