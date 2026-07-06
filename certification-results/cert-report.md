# ETL-SQL Scale Certification Report

Generated: 2026-06-29 17:07:07  |  Tier: **Huge**  |  Row scale: **200x**

## Results

| Scenario | Rows | Elapsed (ms) | Spill (bytes) | Result Rows | Memory (MB) | Memory Bound (MB) | Pass |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| ExternalSort_10000000_DESC | 10000000 | 174993 | 3488000000 | 10000000 | 158.1 | 80000 | OK |
| ExternalAggregate_20000000_10grps | 20000000 | 104695 | 1600000000 | 10 | 159.4 | 160000 | OK |
| ExternalJoin_10000000_equality | 10000000 | 378176 | 4608000000 | 10000000 | 308.9 | 80000 | OK |
| TempTableSpill_10000000_SELECT_INTO | 10000000 | 73439 | 1119680000 | 10000000 | 313.8 | 80000 | OK |
| StreamingSelect_20000000_cap50000 | 20000000 | 5783 | 0 | 50000 | 322.4 | 160000 | OK |
| WindowFunction_ROW_NUMBER_10000000 | 10000000 | 224263 | 3328000000 | 10000000 | 473.7 | 80000 | OK |
| CsvIngest_10000000 | 10000000 | 9756 | 0 | 10000000 | 469.1 | 80000 | OK |
| ParquetRoundTrip_10000000 | 10000000 | 6494 | 0 | 10000000 | 478.1 | 80000 | OK |
| ReportDatasetSnapshotReload_10000000 | 10000000 | 83460 | 0 | 10000000 | 771.4 | 80000 | OK |
| CubeGroupingSets_10000000_10x5 | 10000000 | 254185 | 4480000000 | 66 | 773.4 | 80000 | OK |
| ScalarSubqueryCache_10000000_1000keys | 10000000 | 55842 | 1088000000 | 10000000 | 3179.5 | 80000 | OK |
| SpillCleanupSuccess_10000000 | 10000000 | 29778 | 319680000 | 999 | 2286.4 | 80000 | OK |
| SpillCleanupFailure_10000000 | 10000000 | 43 | 320000 | 1 | 2287.8 | 80000 | OK |

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
