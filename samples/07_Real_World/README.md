# 07_Real_World: Production Pipeline Scenarios

This directory provides 12 self-contained recipes modeling end-to-end data engineering scenarios. Each recipe follows the canonical lifecycle: Extract -> Stage -> Validate -> Transform -> Load -> Cleanup.

## Scenario Catalog

| File | Scenario | Key Pattern |
| :--- | :--- | :--- |
| [`realworld_01_dw_load.etlsql`](realworld_01_dw_load.etlsql) | Data Warehouse Load | Staging flat-file extracts into typed temp tables before loading dimensional targets. |
| [`realworld_02_secure_sftp_alert.etlsql`](realworld_02_secure_sftp_alert.etlsql) | SFTP Transfer & Alerting | Encrypted file transfer combined with webhook notifications on completion or failure. |
| [`realworld_03_schema_quarantine.etlsql`](realworld_03_schema_quarantine.etlsql) | Schema Quarantine | Intercepting malformed or invalid records and routing them to a dead-letter quarantine table. |
| [`realworld_04_incremental_merge.etlsql`](realworld_04_incremental_merge.etlsql) | Incremental Merge | Upserting change data capture (CDC) deltas using `MERGE` against existing master tables. |
| [`realworld_05_masking_json.etlsql`](realworld_05_masking_json.etlsql) | PII Masking & JSON | Masking sensitive personal data and shredding semi-structured JSON payloads into relational columns. |
| [`realworld_06_reconciliation_anti_join.etlsql`](realworld_06_reconciliation_anti_join.etlsql) | Financial Reconciliation | Cross-system balance verification using `LEFT JOIN ... WHERE destination.id IS NULL` anti-joins. |
| [`realworld_07_window_deduplication.etlsql`](realworld_07_window_deduplication.etlsql) | Window Deduplication | Retaining the latest version of records using `ROW_NUMBER() OVER(PARTITION BY ... ORDER BY updated_at DESC)`. |
| [`realworld_08_aggregation_pivot.etlsql`](realworld_08_aggregation_pivot.etlsql) | Cross-Tab Pivoting | Transforming normalized rows into wide reporting tables via `PIVOT`. |
| [`realworld_09_directory_watcher.etlsql`](realworld_09_directory_watcher.etlsql) | Directory Ingestion | Scanning directories for incoming files, processing each in turn, and archiving. |
| [`realworld_10_docker_sync.etlsql`](realworld_10_docker_sync.etlsql) | Docker Engine Sync | Synchronizing data between ephemeral databases using container orchestration. |
| [`realworld_11_customer_entity_resolution.etlsql`](realworld_11_customer_entity_resolution.etlsql) | Customer 360 Matching | Resolving customer records across disconnected source systems using deterministic and fuzzy matching. |
| [`realworld_12_spec_driven_customer_feed.etlsql`](realworld_12_spec_driven_customer_feed.etlsql) | Spec-Driven Ingestion | Driving ingestion mappings and validation rules from a external JSON specification file. |

## Running a Scenario

```bash
dotnet run --project src/ETL-SQL.App -- run samples/07_Real_World/realworld_01_dw_load.etlsql
dotnet run --project src/ETL-SQL.App -- run samples/07_Real_World/realworld_07_window_deduplication.etlsql
```
