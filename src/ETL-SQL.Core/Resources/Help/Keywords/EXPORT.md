# EXPORT
Writes data from a #temp table to a file or an external connection.

## Syntax
```sql
-- Export to a flat file
EXPORT #orders TO 'output/orders.csv';

-- Export with options
EXPORT #orders TO 'output/orders.csv' WITH (
  DELIMITER = '|',
  HEADER    = ON,
  ENCODING  = 'UTF-8'
);

-- Export to a connection table
EXPORT #orders TO MyDB.dbo.OrdersArchive;

-- Export to SFTP
EXPORT #report TO SftpConn:'reports/daily.csv';

-- Recover a published Orchestrator bundle
EXPORT SCRIPT 'orch://finance-load@3/main.etlsql' TO 'C:\Recovered\finance-load';
```

## Options
| Option | Values | Default |
|---|---|---|
| DELIMITER | any char | `,` |
| HEADER | ON \| OFF | ON |
| ENCODING | UTF-8, UTF-16, ASCII | UTF-8 |
| OVERWRITE | ON \| OFF | ON |
| APPEND | ON \| OFF | OFF |

## Notes
- For database destinations, the target table must exist unless the connection supports auto-create.
- SFTP, S3, and API connection types are supported as destinations.
- To control column order or filter rows before export, `SELECT ... INTO #subset` first.
- `EXPORT SCRIPT` preserves published bundle relative paths but does not decrypt or reveal secrets; recovered scripts may require credentials to be re-entered.
- See: CREATE CONNECTION, SELECT
