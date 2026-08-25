# SNOWFLAKE

Native connector for the Snowflake Cloud Data Platform. Supports full SQL pushdown, schema
introspection, batch reads/writes, and transactions. Two authentication modes are supported: username +
password, and private-key JWT (recommended for production).

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `HOST` | Account identifier — plain (`myorg-myaccount`) or full FQDN (`myorg-myaccount.snowflakecomputing.com`). The `.snowflakecomputing.com` suffix is stripped automatically. | Yes |
| `USERNAME` | Snowflake login name | Yes |
| `PASSWORD` | Password (for username/password auth) | Cond. |
| `PRIVATE_KEY_FILE` | Path to RSA private key PEM file (enables JWT auth; takes precedence over `PASSWORD`) | Cond. |
| `WAREHOUSE` | Virtual warehouse for query execution (e.g. `COMPUTE_WH`) | No |
| `DATABASE` | Target database | No |
| `SCHEMA` | Target schema (defaults to `PUBLIC`) | No |

> [!NOTE]
> Either `PASSWORD` or `PRIVATE_KEY_FILE` must be supplied. If both are present, `PRIVATE_KEY_FILE`
> wins and `PASSWORD` is ignored.

> [!TIP]
> For production workloads, use key-pair authentication. Generate an RSA key pair, upload the public
> key to Snowflake (`ALTER USER … SET RSA_PUBLIC_KEY = '…'`), and store the private key path in
> `PRIVATE_KEY_FILE`.

## Authentication

Snowflake supports two primary authentication modes:
- **Username / Password**: Supply `USER` and `PASSWORD`.
- **Key-Pair Authentication**: Supply `PRIVATE_KEY_FILE` (and optional `PRIVATE_KEY_PASSPHRASE`) for automated non-interactive pipelines.

## Examples

```sql
-- Username + password
CREATE CONNECTION sf AS SNOWFLAKE(HOST='myorg-myaccount', USERNAME='etl_user', PASSWORD='s3cr3t',
         DATABASE='PROD', SCHEMA='STAGING', WAREHOUSE='LOAD_WH');

-- Private-key JWT (recommended for production)
CREATE CONNECTION sf AS SNOWFLAKE(HOST='myorg-myaccount.snowflakecomputing.com',
         USERNAME='etl_user',
         PRIVATE_KEY_FILE='/etc/certs/snowflake_rsa_key.p8',
         DATABASE='ANALYTICS', WAREHOUSE='TRANSFORM_WH');

-- Query with full SQL pushdown
SELECT account_id, SUM(revenue) AS total
FROM   sf.ORDERS
GROUP  BY account_id
HAVING total > 100000
QUALIFY ROW_NUMBER() OVER (PARTITION BY region ORDER BY total DESC) = 1;

-- Stage rows in an engine temp table
SELECT * INTO #top_accounts FROM sf.ORDERS WHERE status = 'CLOSED';
```

## Supported Snowflake-specific SQL

| Feature | Notes |
| :--- | :--- |
| `QUALIFY` | Filter on window functions without a sub-query |
| `ILIKE` / `RLIKE` | Case-insensitive `LIKE`; regex `LIKE` |
| `IFF(cond, t, f)` | Inline conditional (equivalent to `CASE WHEN`) |
| `NVL` / `NVL2` / `ZEROIFNULL` / `NULLIFZERO` | Null-substitution functions |
| `TRY_CAST` / `TRY_TO_DATE` / `TRY_TO_NUMBER` | Safe type casts that return `NULL` on failure |
| `ARRAY_AGG` / `OBJECT_CONSTRUCT` | Semi-structured aggregation |
| `FLATTEN` / `LATERAL` | Lateral flattening of arrays/variants |
| `PARSE_JSON` / `GET_PATH` | JSON access |
| `SAMPLE` / `TABLESAMPLE` | Statistical sampling |
| `APPROX_COUNT_DISTINCT` / `APPROX_PERCENTILE` | Approximate aggregates |

The keywords `TOP` and `NOLOCK` are excluded (T-SQL only). Use `LIMIT` for row capping.

## Troubleshooting

- **JWT Token Expired / Invalid Key**: Verify private key format (PKCS#8 PEM) and matching public key registered on Snowflake user.
- **Role / Warehouse Not Found**: Confirm `ROLE` and `WAREHOUSE` exist and user has usage permissions.
- **Case Sensitivity**: Object identifiers in Snowflake default to uppercase unless quoted.

## References

- [Database Connectors](README.md)
- [Connectors](../README.md)
- [BigQuery](bigquery.md) · [ODBC](odbc.md)
