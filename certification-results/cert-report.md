# ETL-SQL Scale Certification Report

Generated: 2026-06-20 12:23:59  |  Tier: **Smoke**  |  Row scale: **1x**

## Results

| Scenario | Rows | Elapsed (ms) | Spill (bytes) | Result Rows | Memory (MB) | Memory Bound (MB) | Pass |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| TempTableSpill_50000_SELECT_INTO | 50000 | 1613 | 5280000 | 50000 | 41.1 | 1000 | OK |
| SpillCleanupFailure_50000 | 50000 | 79 | 320000 | 1 | 1.5 | 1000 | OK |
| CubeGroupingSets_50000_10x5 | 50000 | 2682 | 22400000 | 66 | 9.9 | 1000 | OK |
| WindowFunction_ROW_NUMBER_50000 | 50000 | 1264 | 11200000 | 50000 | 45.6 | 1000 | OK |
| ExternalJoin_50000_equality | 50000 | 871 | 8800000 | 50000 | 59.4 | 1000 | OK |
| StreamingSelect_100000_cap50000 | 100000 | 1469 | 0 | 50000 | 19.9 | 2000 | OK |
| ExternalAggregate_100000_10grps | 100000 | 891 | 8000000 | 10 | 15.1 | 2000 | OK |
| ParquetRoundTrip_50000 | 50000 | 99 | 0 | 50000 | 19.6 | 1000 | OK |
| ScalarSubqueryCache_50000_1000keys | 50000 | 903 | 4000000 | 50000 | 17.3 | 1000 | OK |
| ExternalSort_50000_DESC | 50000 | 626 | 12000000 | 50000 | 45.6 | 1000 | OK |
| SpillCleanupSuccess_50000 | 50000 | 125 | 1280000 | 4 | 1.3 | 1000 | OK |
| CsvIngest_50000 | 50000 | 65 | 0 | 50000 | 8.4 | 1000 | OK |
| ReportDatasetSnapshotReload_50000 | 50000 | 397 | 0 | 50000 | 51.4 | 1000 | OK |

## Operator Status

| Operator | Execution Mode | Scale Tested | Notes |
| :--- | :--- | :--- | :--- |
| ORDER BY | External Sort (multi-chunk) | 50k rows | ExternalSortChunkSize forced to 5k |
| GROUP BY | External Aggregate | 100k rows | OperatorMemoryGrantMB forced to 1 MB |
| JOIN (equality) | External Hash Join | 50k rows | JoinSpillThreshold forced to 5k |
| SELECT INTO #temp | Temp Table Spill | 50k rows | TempTableSpillThresholdRows forced to 10k |
| SELECT (streaming) | Result Cap | 100k rows | MaxLastResultRows cap enforced at 50k |
| WINDOW ROW_NUMBER | External Window | 50k rows | WindowSpillThreshold forced to 5k |
| CSV ingest | Connector batch read | 50k rows | Row count and checksum certified |
| Parquet round trip | Connector batch write/read | 50k rows | Row count and checksum certified |
| CREATE DATASET snapshot/reload | Query -> Parquet cache -> reload | 50k rows | Row count and checksum certified after cached reload |
| GROUP BY CUBE | External Aggregate grouping-set expansion | 50k rows | Expanded row count, checksum, and spill bytes certified |
| Scalar subquery cache | Correlated subquery LRU cache | 50k rows | Row count, checksum, and exact hit/miss counts certified |
| Spill cleanup after success | Non-persistent temp-table spill lifecycle | 50k rows | Spill directory removed after evaluator disposal |
| Spill cleanup after failure | Non-persistent temp-table spill lifecycle | 50k rows | Forced source failure still removes spill directory after evaluator disposal |
