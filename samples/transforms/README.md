# Analytical Transforms & Data Preparation

Declarative data preparation helpers and high-level analytical transformations (`TRANSFORM ... USING` and `COMPARE DATASETS`).

These statements eliminate multi-step procedural boilerplate, performing operations like time-series gap filling, moving averages, CDC diffing, and deduplication directly inside the engine.

## Sample Inventory

| Operation | Script | Syntax / Pattern | What it Demonstrates |
| :--- | :--- | :--- | :--- |
| **Date Dimension** | [`generate_calendar.etlsql`](generate_calendar.etlsql) | `GENERATE CALENDAR FROM ... TO ... INTO #calendar` | Generates date dimensions with fiscal quarters, weekday flags, and ISO numbers; outer joins to prevent skipped reporting dates. |
| **CDC Delta Diff** | [`compare_datasets_cdc.etlsql`](compare_datasets_cdc.etlsql) | `COMPARE DATASETS #today WITH #yesterday KEY (...)` | Automatically isolates `INSERT`, `UPDATE`, and `DELETE` rows and pinpoints altered column names. |
| **Rolling Average** | [`transform_rolling_aggregate.etlsql`](transform_rolling_aggregate.etlsql) | `TRANSFORM ... USING ROLLING_AGGREGATE (...)` | Computes moving averages or running cumulative sums over partitioned time windows. |
| **Matrix Pivot** | [`transform_pivot.etlsql`](transform_pivot.etlsql) | `TRANSFORM ... USING PIVOT (...)` | Rotates row values into columns to construct cross-tabulated reporting matrices without `CASE` statements. |
| **Gap Filling** | [`transform_fill_dates.etlsql`](transform_fill_dates.etlsql) | `TRANSFORM ... USING FILL_DATES (...)` | Fills missing daily records across groups, defaulting missing metrics to zero. |
| **Deduplication** | [`transform_deduplicate.etlsql`](transform_deduplicate.etlsql) | `TRANSFORM ... USING DEDUPLICATE (...)` | Deduplicates records by key with deterministic `ORDER_BY` tie-breaking (`KEEP = 'FIRST'`). |
| **Feature Scaling** | [`transform_normalize.etlsql`](transform_normalize.etlsql) | `TRANSFORM ... USING NORMALIZE (...)` | Min-Max scaling and Z-Score normalization within groups. |

## Running

Run any script with the CLI:

```bash
dotnet src/ETL-SQL.App/bin/Debug/net10.0/ETL-SQL.dll run samples/transforms/generate_calendar.etlsql
```
