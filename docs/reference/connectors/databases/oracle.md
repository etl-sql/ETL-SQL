# ORACLE
Connects to Oracle Database using a host/service combination or a TNS alias.

Syntax:
  CREATE CONNECTION <name> AS ORACLE(
    HOST         = 'oracle.corp.local',
    PORT         = 1521,
    SERVICE_NAME = 'ORCL',
    USER         = 'username',
    PASSWORD     = '<password>'
  );

Options:
- **HOST** — Oracle server hostname or IP (required unless using TNS_NAME)
- **PORT** — listener port (default 1521)
- **SERVICE_NAME** — Oracle service name
- **TNS_NAME** — TNS alias (alternative to HOST + PORT + SERVICE_NAME)
- **USER** — schema/user (required)
- **PASSWORD** — password (required)
- **TIMEOUT_SECONDS** — command/query execution timeout in seconds (default 30)
- **TABLE** — default table for unqualified SELECT/INSERT

```sql
CREATE CONNECTION FinanceDB AS ORACLE(
  HOST         = 'oracle.finance.corp',
  PORT         = 1521,
  SERVICE_NAME = 'FINPROD',
  USER         = @ora_user,
  PASSWORD     = @ora_pass
);

SELECT account_id, balance, last_updated
  INTO #accounts
  FROM FinanceDB.FINANCE.ACCOUNTS
  WHERE status = 'ACTIVE';

PRINT 'Accounts loaded: ' + @@ROWCOUNT;
```

References:
- [Data Connectors](../../../guides/administration.md)
