# BIGQUERY

Native connector for Google BigQuery. Uses the BigQuery REST API (not ADO.NET). Supports full Standard
SQL pushdown, schema introspection, streaming inserts, and batch reads. Two authentication modes:
service-account JSON key file (`CREDENTIAL_FILE`) or Application Default Credentials (ADC / workload
identity) when no credential file is provided.

> [!IMPORTANT]
> BigQuery does **not** support traditional RDBMS transactions. All DML statements (`INSERT`, `UPDATE`,
> `DELETE`, `MERGE`, `TRUNCATE`) are auto-committed per statement. Using `BEGIN TRANSACTION` / `COMMIT`
> / `ROLLBACK` with a BigQuery connection has no effect.

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PROJECT_ID` | GCP project ID | Yes |
| `DATASET` | Default dataset (equivalent to schema). Required for schema introspection and write operations. | No |
| `CREDENTIAL_FILE` | Path to service account JSON key file. Omit to use ADC (Cloud Run, GKE workload identity, `gcloud auth application-default login`). | No |
| `LOCATION` | BigQuery job location: `US`, `EU`, `us-central1`, etc. Defaults to `US`. | No |

## Examples

```sql
-- Service-account auth with a fixed dataset
CREATE CONNECTION bq AS BIGQUERY(PROJECT_ID='my-gcp-project', DATASET='analytics',
         CREDENTIAL_FILE='/etc/sa/bigquery-sa.json', LOCATION='US');

-- ADC (workload identity / developer machine)
CREATE CONNECTION bq AS BIGQUERY(PROJECT_ID='my-gcp-project', DATASET='analytics');

-- SQL pushdown — BigQuery Standard SQL sent directly
SELECT account_id, SUM(revenue) AS total
FROM   bq.orders
WHERE  status = 'CLOSED'
GROUP  BY account_id
QUALIFY ROW_NUMBER() OVER (PARTITION BY region ORDER BY total DESC) = 1
LIMIT  100;

-- Three-part table name (project.dataset.table)
SELECT * FROM bq.`myproject.staging.raw_events` LIMIT 1000;

-- Stage rows in an engine temp table
SELECT * INTO #events FROM bq.events WHERE event_date >= '2024-01-01';
```

## BigQuery SQL dialect notes

| Feature | Notes |
| :--- | :--- |
| Identifiers | Backtick-quoted: `` `project.dataset.table` `` |
| `QUALIFY` | Filter on window functions without a sub-query |
| `SAFE_CAST` | Type cast returning `NULL` on failure (instead of error) |
| `COUNTIF(cond)` | Conditional aggregate — equivalent to `COUNT(CASE WHEN cond THEN 1 END)` |
| `APPROX_COUNT_DISTINCT` | Approximate distinct count (HLL++ algorithm) |
| `UNNEST(array)` | Flatten array to rows |
| `STRUCT(...)` | Inline record construction |
| `TO_JSON_STRING` | Serialize a value to a JSON string |
| `GENERATE_ARRAY(start, end, step)` | Produce an integer array |

T-SQL keywords `TOP`, `NOLOCK`, `ISNULL`, `GETDATE`, and `SYSDATE` are excluded. Use `LIMIT`, `IFNULL`,
and `CURRENT_DATETIME()` respectively.

**Write behavior:** `COPY INTO` uses BigQuery streaming inserts (low latency, no staging).
`COPY INTO … APPEND=FALSE` truncates via `TRUNCATE TABLE` before inserting.

## References

- [Database Connectors](README.md)
- [Connectors](../README.md)
- [Snowflake](snowflake.md) · [ODBC](odbc.md)
