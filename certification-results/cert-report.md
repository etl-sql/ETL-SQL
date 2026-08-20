# ETL-SQL Scale Certification Report

Generated: 2026-08-20 15:31:30  |  Tier: **Standard**  |  Row scale: **10x**  |  Samples: **3**

## Results

| Scenario | Samples | Rows | Rows/s | Elapsed (ms) | Spill Write | Peak WS (MB) | Private (MB) | Heap (MB) | Allocated (MB) | CPU % | GC Pause (ms) | Bound (MB) | Pass |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| CsvIngest_500000 | 3 | 500000 | 1742160.3 | 287 | 0 | 939.7 | 1170.1 | 467.7 | 531.4 | 21.4 | 124.4 | 4096 | OK |
| CubeGroupingSets_500000_10x5 | 3 | 500000 | 115101.3 | 4344 | 224000000 | 1358.5 | 1573.4 | 673 | 4212.7 | 8.3 | 244.5 | 4096 | OK |
| ExternalAggregate_1000000_10grps | 3 | 1000000 | 557103.1 | 1795 | 80000000 | 659.4 | 678.3 | 244.4 | 1117.5 | 7.9 | 137.1 | 4096 | OK |
| ExternalJoin_500000_equality | 3 | 500000 | 177053.8 | 2824 | 88000000 | 878.1 | 1113.8 | 356.3 | 3151 | 10.6 | 361.9 | 4096 | OK |
| ExternalSort_500000_DESC | 3 | 500000 | 112714.2 | 4436 | 119997440 | 543.1 | 506.3 | 241.3 | 2080.9 | 6.4 | 132.6 | 4096 | OK |
| ParquetRoundTrip_500000 | 3 | 500000 | 1886792.5 | 265 | 0 | 969.4 | 1211.5 | 452.3 | 455.9 | 14.1 | 113 | 4096 | OK |
| ReportDatasetSnapshotReload_500000 | 3 | 500000 | 663130 | 754 | 0 | 1278.8 | 1456.6 | 720.2 | 1520.8 | 11.8 | 192.8 | 4096 | OK |
| ScalarSubqueryCache_500000_1000keys | 3 | 500000 | 312695.4 | 1599 | 40000000 | 1399.5 | 1643.4 | 718 | 1568.7 | 8.9 | 206.5 | 4096 | OK |
| SpillCleanupFailure_500000 | 3 | 500000 | 33333333.3 | 15 | 331776 | 1334.3 | 1577.1 | 659 | 15.3 | 34.7 | 140.8 | 4096 | OK |
| SpillCleanupSuccess_500000 | 3 | 500000 | 2252252.3 | 222 | 16257024 | 1384.8 | 1639.2 | 718 | 333.6 | 23.1 | 154.1 | 4096 | OK |
| StreamingSelect_1000000_cap50000 | 3 | 1000000 | 194855.8 | 5132 | 0 | 772.3 | 1029.7 | 232.7 | 66.3 | 1 | 76.9 | 4096 | OK |
| TempTableSpill_500000_SELECT_INTO | 3 | 500000 | 1602564.1 | 312 | 16257024 | 878.1 | 1078.8 | 334.7 | 333.3 | 8.9 | 95.9 | 4096 | OK |
| WindowFunction_ROW_NUMBER_500000 | 3 | 500000 | 154511.7 | 3236 | 112000000 | 918.8 | 1176.1 | 391.1 | 3421.6 | 7.7 | 228.2 | 4096 | OK |

## Environment

- OS: Microsoft Windows 11 Home 10.0.26200
- CPU: Intel(R) Core(TM) Ultra 9 275HX (24 logical cores)
- RAM: 31.4 GB
- Disk: NVMe MTFDKBA1T0QGN-1BN1AABGA, 953.9 GB; workspace free 285.1 GB
- Runtime: .NET 10.0.11, X64, Release, server GC enabled: True
- Engine memory grant: 2048 MB
- Commit: a377e6d9cfbdf66c810a06fbe2956ab1b1438d7e (release/v0.18.0); dirty: True
- Source fingerprint: 43ea8db52df9b01016d73b8cf1300c3fc082c110182f21703fc514dac85ef728
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
