# BIGQUERY
Connects to Google BigQuery using the REST API. Supports full SQL pushdown and streaming inserts.
Auth: service account JSON file or Application Default Credentials (ADC) when omitted.
Transactions are not supported — BigQuery DML is auto-committed per statement.

Syntax:
  CREATE CONNECTION <name> ON BIGQUERY(
    PROJECT_ID      = 'my-gcp-project',
    DATASET         = 'my_dataset',
    CREDENTIAL_FILE = 'C:\keys\sa.json'
  );

Options:
  PROJECT_ID      — GCP project ID (required)
  DATASET         — default dataset for unqualified table references
  CREDENTIAL_FILE — path to a service account JSON key file
                    (omit to use Application Default Credentials / Workload Identity)
  LOCATION        — query location / region (e.g. US, EU, us-central1)
  TIMEOUT_SECONDS — query execution timeout in seconds (default 1800)

```sql
-- Service account authentication
CREATE CONNECTION BQ ON BIGQUERY(
  PROJECT_ID      = 'analytics-prod-12345',
  DATASET         = 'sales',
  CREDENTIAL_FILE = 'C:\keys\bq-service-account.json'
);

SELECT region, SUM(revenue) AS total_revenue
  INTO #summary
  FROM BQ.sales.orders
  WHERE DATE(order_date) >= DATE_SUB(CURRENT_DATE(), INTERVAL 30 DAY)
  GROUP BY region;

PRINT 'Regions loaded: ' + @@ROWCOUNT;
```

```sql
-- Application Default Credentials (gcloud auth application-default login)
CREATE CONNECTION BQ_ADC ON BIGQUERY(
  PROJECT_ID = 'analytics-prod-12345',
  DATASET    = 'dw',
  LOCATION   = 'US'
);
```

Notes:
- Table names can be fully qualified as project.dataset.table or dataset.table.
- DATASET is required for WRITE operations (INSERT INTO a BigQuery table).
- Streaming inserts (INSERT) do not support transactions — each batch is auto-committed.
- Use LOCATION when your dataset resides in a specific region to avoid cross-region billing.
- For large exports, prefer SELECT ... INTO #temp then process locally rather than streaming back millions of BigQuery rows.

References:
- [Data Connectors](../../../../../Docs/Reference/Data_Connectors.md)
