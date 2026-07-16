# SNOWFLAKE
Connects to the Snowflake Cloud Data Platform. Supports full SQL pushdown, transactions, and both username/password and private-key (JWT) authentication.

Syntax:
  CREATE CONNECTION <name> AS SNOWFLAKE(
    HOST             = 'myorg-myaccount.snowflakecomputing.com',
    DATABASE         = 'dbname',
    SCHEMA           = 'PUBLIC',
    WAREHOUSE        = 'COMPUTE_WH',
    USERNAME         = 'username',
    PASSWORD         = '<password>'
  );

Options:
- **HOST** — Snowflake account identifier or hostname (required). Accepts the short form (e.g. myorg-myaccount) or the full hostname ending in .snowflakecomputing.com
- **DATABASE** — target Snowflake database (required)
- **SCHEMA** — target schema (default PUBLIC)
- **WAREHOUSE** — virtual warehouse used for query execution (required for DML)
- **USERNAME** — Snowflake user name (required)
- **PASSWORD** — password for username/password authentication
- **PRIVATE_KEY_FILE** — path to an RSA private key PEM file for key-pair JWT authentication (use instead of PASSWORD)
- **TIMEOUT_SECONDS** — command/query execution timeout in seconds (default 1800)
- **ACCOUNT** — explicit account name override; useful when connecting to a local emulator
- **PORT** — service port override; useful when connecting to a local emulator
- **PROTOCOL** — protocol override: https (default) or http; useful with local emulators

```sql
-- Username/password authentication
CREATE CONNECTION Analytics AS SNOWFLAKE(
  HOST      = 'myorg-acct.snowflakecomputing.com',
  DATABASE  = 'PROD_DW',
  SCHEMA    = 'SALES',
  WAREHOUSE = 'COMPUTE_WH',
  USERNAME  = @sf_user,
  PASSWORD  = @sf_pass
);

SELECT region, SUM(amount) AS total
  INTO #summary
  FROM Analytics.SALES.ORDERS
  WHERE order_date >= @start_date
  GROUP BY region;

PRINT 'Regions loaded: ' + @@ROWCOUNT;
```

```sql
-- Key-pair (JWT) authentication
CREATE CONNECTION AnalyticsJWT AS SNOWFLAKE(
  HOST             = 'myorg-acct.snowflakecomputing.com',
  DATABASE         = 'PROD_DW',
  WAREHOUSE        = 'COMPUTE_WH',
  USERNAME         = 'svc_etl',
  PRIVATE_KEY_FILE = 'C:\keys\snowflake_rsa_key.p8'
);
```

References:
- [Data Connectors](../../../guides/administration.md)
