# 06_Advanced_SQL: Advanced Query Constructs and Modern SQL

This directory contains advanced data engineering query patterns supported by the ETL-SQL execution engine.

## Samples Overview

| File | Feature | Description |
| :--- | :--- | :--- |
| [`asof_temporal_join.etlsql`](asof_temporal_join.etlsql) | `ASOF JOIN` | Time-series join matching events to the closest preceding observation (financial tick data, telemetry). |
| [`match_recognize_patterns.etlsql`](match_recognize_patterns.etlsql) | `MATCH_RECOGNIZE` | Complex Event Processing (CEP) syntax to identify patterns (spikes, declines, fraud sequences) over ordered rows. |
| [`qualify_filtering.etlsql`](qualify_filtering.etlsql) | `QUALIFY` | Snowflake-style filtering on window function results directly without an intermediate subquery. |
| [`group_by_all.etlsql`](group_by_all.etlsql) | `GROUP BY ALL` | Automatic inference of all non-aggregated columns in the group-by specification. |
| [`lateral_subqueries.etlsql`](lateral_subqueries.etlsql) | `LATERAL` | Correlated subqueries that reference columns produced by preceding table expressions in the `FROM` clause. |
| [`declarative_watermark.etlsql`](declarative_watermark.etlsql) | Watermarking | Managing stateful incremental ETL pipelines with `DECLARE @watermark WATERMARK`. |
| [`Fuzzy_Joins.etlsql`](Fuzzy_Joins.etlsql) | Fuzzy Matching | Approximate string matching joins using Jaro-Winkler and Levenshtein similarity distance metrics. |
| [`Window_Functions.etlsql`](Window_Functions.etlsql) | Window Functions | Ranking (`ROW_NUMBER`, `RANK`, `DENSE_RANK`), offsets (`LEAD`, `LAG`), and running aggregates over partitions. |
| [`Grouping_Aggregations.etlsql`](Grouping_Aggregations.etlsql) | Multi-Dimensional | Advanced rollup and cross-dimensional grouping sets (`ROLLUP`, `CUBE`, `GROUPING SETS`). |
| [`Join_Algorithm_Hints.etlsql`](Join_Algorithm_Hints.etlsql) | Optimizer Hints | Guiding join execution with `WITH (HINT = HASH_JOIN)` or `MERGE_JOIN`. |

## Running These Samples

```bash
dotnet run --project src/ETL-SQL.App -- run samples/06_Advanced_SQL/asof_temporal_join.etlsql
dotnet run --project src/ETL-SQL.App -- run samples/06_Advanced_SQL/qualify_filtering.etlsql
```
