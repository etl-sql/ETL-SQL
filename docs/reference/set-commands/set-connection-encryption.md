# SET CONNECTION_ENCRYPTION
Controls whether `CREATE CONNECTION` targets and quoted option values are encrypted on save using the script/master password.

## Syntax
```text
SET CONNECTION_ENCRYPTION = ON|OFF;
```

## Parameters
- **ON** — Encrypt connection targets and options on save.
- **OFF** — Leave connection definitions unencrypted on save (default).

## Example
```sql
-- Encrypt connection details on save
SET CONNECTION_ENCRYPTION = ON;
USE PASSWORD = 'master-key';

CREATE CONNECTION SalesDB AS MSSQL(SERVER='prod-server', DATABASE='Sales', USER='svc', PASSWORD='secret');
-- On save, connection values are encrypted with the master password
```

## Notes
- `SET NO_SAVE_CONNECTION` takes precedence when both are ON.
- Corresponding `appsettings.json` key: `Engine:ConnectionEncryption`.
- See also: `SET NO_SAVE_CONNECTION`, `SET NO_SAVE_SENSITIVE`.
- Default: OFF.

## References
- [SET Commands](README.md)
