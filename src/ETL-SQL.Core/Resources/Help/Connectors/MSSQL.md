# MSSQL
Connects to Microsoft SQL Server or Azure SQL Database. Supports transactions, stored procedure execution, and all SQL Server data types.

Syntax:
  CREATE CONNECTION <name> ON MSSQL(
    SERVER             = 'hostname\instance',
    DATABASE           = 'dbname',
    USER               = 'username',
    PASSWORD           = '<password>',
    TRUSTED_CONNECTION = ON | OFF,
    CONNECT_TIMEOUT    = 30,
    USE_SSL            = ON | OFF
  );

Options:
  SERVER             — server hostname, IP, or hostname\instance (required)
  DATABASE           — target database name (required)
  USER               — SQL login username (for SQL auth)
  PASSWORD           — SQL login password
  TRUSTED_CONNECTION — use Windows integrated authentication (default OFF)
  CONNECT_TIMEOUT    — connection timeout in seconds (default 30)
  USE_SSL            — encrypt the connection (default ON for Azure SQL)
  TABLE              — default table for unqualified INSERT/SELECT

```sql
CREATE CONNECTION SalesDB ON MSSQL(
  SERVER             = 'sql.corp.local',
  DATABASE           = 'SalesData',
  TRUSTED_CONNECTION = ON
);

SELECT order_id, customer, amount
  INTO #orders
  FROM SalesDB.dbo.Orders
  WHERE order_date >= @start;

EXECUTE SalesDB.dbo.UpdateSummary;

BEGIN TRANSACTION;
  INSERT INTO SalesDB.dbo.Staging SELECT * FROM #processed;
COMMIT;
```
