# ETL-SQL Scale Certification Report

Generated: 2026-06-02 12:45:25  |  Tier: **Smoke**  |  Row scale: **1x**

## Results

| Scenario | Rows | Elapsed (ms) | Spill (bytes) | Result Rows | Memory (MB) | Memory Bound (MB) | Pass |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| TempTableSpill_50000_SELECT_INTO | 50000 | 1516 | 5280000 | 50000 | 41.3 | 1000 | OK |
| SpillCleanupFailure_50000 | 50000 | 69 | 320000 | 1 | 1.4 | 1000 | OK |
| CubeGroupingSets_50000_10x5 | 50000 | 2329 | 22400000 | 66 | 9.8 | 1000 | OK |
| WindowFunction_ROW_NUMBER_50000 | 50000 | 1276 | 11200000 | 50000 | 45.5 | 1000 | OK |
| ExternalJoin_50000_equality | 50000 | 778 | 8800000 | 50000 | 59.4 | 1000 | OK |
| StreamingSelect_100000_cap50000 | 100000 | 3887 | 0 | 50000 | 19.8 | 2000 | OK |
| ExternalAggregate_100000_10grps | 100000 | 775 | 8000000 | 10 | 15 | 2000 | OK |
| ParquetRoundTrip_50000 | 50000 | 93 | 0 | 50000 | 19.5 | 1000 | OK |
| ScalarSubqueryCache_50000_1000keys | 50000 | 932 | 4000000 | 50000 | 17.2 | 1000 | OK |
| ExternalSort_50000_DESC | 50000 | 614 | 12000000 | 50000 | 45.5 | 1000 | OK |
| SpillCleanupSuccess_50000 | 50000 | 124 | 1280000 | 4 | 1.2 | 1000 | OK |
| CsvIngest_50000 | 50000 | 60 | 0 | 50000 | 8.4 | 1000 | OK |
| ReportDatasetSnapshotReload_50000 | 50000 | 403 | 0 | 50000 | 51.9 | 1000 | OK |

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
