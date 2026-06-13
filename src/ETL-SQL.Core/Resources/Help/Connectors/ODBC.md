# ODBC
Connects to any ODBC-compatible data source using a DSN or a full driver connection string. Use for databases without a native ETL-SQL connector.

Syntax:
  CREATE CONNECTION <name> AS ODBC(
    DSN      = 'MyDSN',
    -- or build a connection string:
    DRIVER   = '{SQL Server}',
    SERVER   = 'hostname',
    DATABASE = 'dbname',
    UID      = 'username',
    PASSWORD = '<password>'
  );

Options:
  DSN       — ODBC Data Source Name configured in the OS
  DRIVER    — ODBC driver name (if not using a DSN)
  SERVER    — server hostname
  DATABASE  — database name
  UID             — username
  PASSWORD        — password
  TIMEOUT_SECONDS — command/query execution timeout in seconds (default 30)

```sql
-- Using a pre-configured DSN
CREATE CONNECTION LegacyDB AS ODBC(
  DSN = 'LegacyERP',
  UID = @user,
  PASSWORD = @password
);

SELECT part_no, description, quantity
  INTO #parts
  FROM LegacyDB.PARTS;

PRINT 'Parts loaded: ' + @@ROWCOUNT;
```

References:
- [Data Connectors](../../../../../Docs/Reference/Data_Connectors.md)
