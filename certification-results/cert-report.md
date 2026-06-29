# ETL-SQL Scale Certification Report

Generated: 2026-06-29 06:40:35  |  Tier: **Stress**  |  Row scale: **100x**

## Results

| Scenario | Rows | Elapsed (ms) | Spill (bytes) | Result Rows | Memory (MB) | Memory Bound (MB) | Pass |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| ExternalSort_5000000_DESC | 5000000 | 66284 | 1328000000 | 5000000 | 876.1 | 40000 | OK |
| ExternalAggregate_10000000_10grps | 10000000 | 47723 | 800000000 | 10 | 2300.5 | 80000 | OK |
| ExternalJoin_5000000_equality | 5000000 | 129846 | 1681503744 | 5000000 | 4804 | 40000 | OK |
| TempTableSpill_5000000_SELECT_INTO | 5000000 | 32685 | 559680000 | 5000000 | 4629.5 | 40000 | OK |
| StreamingSelect_10000000_cap50000 | 10000000 | 1239 | 0 | 50000 | 6059.9 | 80000 | OK |
| WindowFunction_ROW_NUMBER_5000000 | 5000000 | 97947 | 1248000000 | 5000000 | 6924.2 | 40000 | OK |
| CsvIngest_5000000 | 5000000 | 3595 | 0 | 5000000 | 7632.2 | 40000 | OK |
| ParquetRoundTrip_5000000 | 5000000 | 2915 | 0 | 5000000 | 8417 | 40000 | OK |
| ReportDatasetSnapshotReload_5000000 | 5000000 | 34806 | 0 | 5000000 | 7933.4 | 40000 | OK |
| CubeGroupingSets_5000000_10x5 | 5000000 | 160384 | 2240000000 | 66 | 8854.7 | 40000 | OK |
| ScalarSubqueryCache_5000000_1000keys | 5000000 | 26830 | 528000000 | 5000000 | 10047.3 | 40000 | OK |
| SpillCleanupSuccess_5000000 | 5000000 | 11852 | 159680000 | 499 | 9599.7 | 40000 | OK |
| SpillCleanupFailure_5000000 | 5000000 | 43 | 320000 | 1 | 9601.1 | 40000 | OK |

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
