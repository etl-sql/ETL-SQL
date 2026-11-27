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
| `TIMEOUT_SECONDS` | Command/query timeout in seconds (default: `1800`, or `Connectors:DataWarehouse:DefaultCommandTimeoutSeconds`) | No |

> [!NOTE]
> `CREDENTIAL_FILE` and Application Default Credentials (ADC) are mutually exclusive authentication
> paths: omit `CREDENTIAL_FILE` to use ambient ADC, or supply `CREDENTIAL_FILE` to authenticate with
> an explicit service-account JSON key file.

## Authentication

BigQuery supports two mutually exclusive authentication methods:
- **Service Account Key**: Pass the local JSON key file path using `CREDENTIAL_FILE` (or `${ENV}` allow-listed in `Security:AllowedEnvVars`, or `'SECRET:...'`).
- **Application Default Credentials (ADC)**: When running on GCP (GCE, Cloud Run, GKE) or after running `gcloud auth application-default login`, omit `CREDENTIAL_FILE` to use implicit ambient credentials.

## Examples

```sql
-- Service-account auth using an allow-listed environment variable path (Security:AllowedEnvVars)
CREATE CONNECTION bq AS BIGQUERY(PROJECT_ID='my-gcp-project', DATASET='analytics',
         CREDENTIAL_FILE='${BQ_SA_KEY_PATH}', LOCATION='US', TIMEOUT_SECONDS=1800);

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

## Write Path & Transactions

- **Bulk / write path** — Cross-source `INSERT INTO` and `COPY INTO` use BigQuery streaming inserts (`BigQueryClient.InsertRowsAsync`), assigning a per-row `insertId` (`{batchId}-{ordinal}`) for best-effort deduplication and serializing engine `decimal` values as invariant numeric strings. SDK transport retries are disabled (`NumTries = 1`) so an ambiguous response fails rather than replaying partial writes. Same-connection `INSERT INTO ... SELECT` pushes down as Standard SQL. `COPY INTO ... APPEND=FALSE` runs `TRUNCATE TABLE` first.
- **`MERGE` behavior** — Engine-emulated. `MERGE` against a `BIGQUERY` target evaluates in bounded engine memory and rewrites the target via `TRUNCATE TABLE` + streaming inserts. For large tables, load `#delta` into a BigQuery staging table and run BigQuery's native `MERGE` inside `EXECUTE bq BEGIN ... END`.
- **Transactions** — **Not supported** (`BigQueryDataSource` does not implement `ITransactionalDataSource`). Every DML statement is auto-committed; `BEGIN TRANSACTION` / `COMMIT` / `ROLLBACK` has no effect on BigQuery.
- **`WHAT_IF` behavior** — Under `SET WHAT_IF ON`, `WriteBatches` and `TruncateAsync` return without calling BigQuery. Pushed-down DML and `EXECUTE` blocks log the compiled SQL without executing it.
- **Idempotent-load pattern** — Write `#delta` into a dated or run-scoped staging table (or truncate and reload a dedicated staging table), reconcile on error if a streaming insert fails, then run an atomic native BigQuery `MERGE` (or partition replace) inside `EXECUTE bq BEGIN ... END`.

## Troubleshooting

- **Dataset Not Found**: Verify `PROJECT_ID` and `DATASET` match your GCP console exactly.
- **Permission Denied**: Ensure the service account has at least `BigQuery Data Viewer` and `BigQuery Job User` IAM roles.
- **Quota Exceeded**: Query exceeded project byte budget or rate limits. Stage filtered results into a `#temp` table before wide joins.

## References

- [BigQuery streaming insert deduplication](https://docs.cloud.google.com/bigquery/docs/write-api-rest)
- [Database Connectors](README.md)
- [Connectors](../README.md)
- [Snowflake](snowflake.md) · [ODBC](odbc.md)
