# ETL-SQL Scale Certification Report

Generated: 2026-06-28 13:36:26  |  Tier: **Standard**  |  Row scale: **10x**

## Results

| Scenario | Rows | Elapsed (ms) | Spill (bytes) | Result Rows | Memory (MB) | Memory Bound (MB) | Pass |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| ExternalSort_500000_DESC | 500000 | 6876 | 120000000 | 500000 | 150.7 | 6000 | OK |
| ExternalAggregate_1000000_10grps | 1000000 | 6485 | 80000000 | 10 | 289.5 | 12000 | OK |
| ExternalJoin_500000_equality | 500000 | 15473 | 152000000 | 500000 | 589.5 | 6000 | OK |
| TempTableSpill_500000_SELECT_INTO | 500000 | 4708 | 55680000 | 500000 | 579.8 | 6000 | OK |
| StreamingSelect_1000000_cap50000 | 1000000 | 1469 | 0 | 50000 | 723.3 | 12000 | OK |
| WindowFunction_ROW_NUMBER_500000 | 500000 | 11737 | 112000000 | 500000 | 867.1 | 6000 | OK |
| CsvIngest_500000 | 500000 | 862 | 0 | 500000 | 932.9 | 6000 | OK |
| ParquetRoundTrip_500000 | 500000 | 854 | 0 | 500000 | 989.3 | 6000 | OK |
| ReportDatasetSnapshotReload_500000 | 500000 | 4023 | 0 | 500000 | 1076.3 | 6000 | OK |
| CubeGroupingSets_500000_10x5 | 500000 | 31636 | 224000000 | 66 | 1173.6 | 6000 | OK |
| ScalarSubqueryCache_500000_1000keys | 500000 | 3020 | 40000000 | 500000 | 1319.1 | 6000 | OK |
| SpillCleanupSuccess_500000 | 500000 | 1153 | 15680000 | 49 | 1276.3 | 6000 | OK |
| SpillCleanupFailure_500000 | 500000 | 48 | 320000 | 1 | 1277.7 | 6000 | OK |

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
