# ETL-SQL Scale Certification Report

Generated: 2026-09-06 16:08:52  |  Tier: **Standard**  |  Row scale: **10x**  |  Samples: **3**

## Results

| Scenario | Samples | Rows | Rows/s | Elapsed (ms) | Spill Write | Peak WS (MB) | Private (MB) | Heap (MB) | Allocated (MB) | CPU % | GC Pause (ms) | Bound (MB) | Pass |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| CsvIngest_500000 | 3 | 500000 | 1886792.5 | 265 | 0 | 997.9 | 1237.5 | 513.3 | 532.1 | 19.7 | 138.5 | 4096 | OK |
| CubeGroupingSets_500000_10x5 | 3 | 500000 | 98425.2 | 5080 | 224000000 | 1421.5 | 1624.9 | 706.9 | 4203.3 | 8.1 | 268 | 4096 | OK |
| ExternalAggregate_1000000_10grps | 3 | 1000000 | 460193.3 | 2173 | 80000000 | 729.8 | 813.4 | 300.8 | 1118.7 | 8.5 | 154.2 | 4096 | OK |
| ExternalJoin_500000_equality | 3 | 500000 | 148456.1 | 3368 | 88000000 | 885 | 1136.5 | 382.4 | 3145.1 | 9.8 | 316.2 | 4096 | OK |
| ExternalSort_500000_DESC | 3 | 500000 | 102228.6 | 4891 | 119997440 | 533.8 | 552.5 | 256.5 | 2081.6 | 6.4 | 114.1 | 4096 | OK |
| ParquetRoundTrip_500000 | 3 | 500000 | 1742160.3 | 287 | 0 | 1000.5 | 1237.5 | 509 | 455.3 | 9.6 | 100.7 | 4096 | OK |
| ReportDatasetSnapshotReload_500000 | 3 | 500000 | 588235.3 | 850 | 0 | 1307.6 | 1523.2 | 708.7 | 1522.8 | 13.3 | 197.6 | 4096 | OK |
| ScalarSubqueryCache_500000_1000keys | 3 | 500000 | 277315.6 | 1803 | 40000000 | 1447.1 | 1659.9 | 736.1 | 1569.4 | 9.1 | 223.4 | 4096 | OK |
| SpillCleanupFailure_500000 | 3 | 500000 | 26315789.5 | 19 | 331776 | 1371.5 | 1579.6 | 676.6 | 16 | 35.3 | 128.6 | 4096 | OK |
| SpillCleanupSuccess_500000 | 3 | 500000 | 1968503.9 | 254 | 16257024 | 1384.5 | 1592.5 | 677.3 | 334.4 | 19.8 | 150.8 | 4096 | OK |
| StreamingSelect_1000000_cap50000 | 3 | 1000000 | 121951.2 | 8200 | 0 | 771.4 | 1006.8 | 253.6 | 67.4 | 0.7 | 69.3 | 4096 | OK |
| TempTableSpill_500000_SELECT_INTO | 3 | 500000 | 1333333.3 | 375 | 16257024 | 828.3 | 1061.1 | 362.5 | 334.1 | 15.4 | 91.3 | 4096 | OK |
| WindowFunction_ROW_NUMBER_500000 | 3 | 500000 | 128402.7 | 3894 | 112000000 | 1010.2 | 1260.7 | 410.3 | 3422.3 | 7.3 | 221 | 4096 | OK |

## Environment

- OS: Microsoft Windows 11 Home 10.0.26200
- CPU: Intel(R) Core(TM) Ultra 9 275HX (24 logical cores)
- RAM: 31.4 GB
- Disk: NVMe MTFDKBA1T0QGN-1BN1AABGA, 953.9 GB; workspace free 212.1 GB
- Runtime: .NET 10.0.11, X64, Release, server GC enabled: True
- Engine memory grant: 2048 MB
- Commit: 9f20c3a98ee94f99d95e62975dfc36fba8b9fd43 (release/v0.19.0); dirty: True
- Source fingerprint: 4840b48716535ba56ca5eb3b8fe2fbcd5da283caac8ce40d986ea1cc44cacfac
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
