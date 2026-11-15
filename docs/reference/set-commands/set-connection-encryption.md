# SET CONNECTION_ENCRYPTION
Controls whether `CREATE CONNECTION` targets and quoted option values are encrypted on save using the script/master password.

## Syntax
```text
SET CONNECTION_ENCRYPTION [=] ON|OFF;
```

## Parameters
- **ON** — Prompt for or use the single script/master password to encrypt the entire `CREATE CONNECTION` target and every quoted connection option (`SERVER`, `HOST`, `DATABASE`, `USER`, `PASSWORD`, etc.) on save, along with rewriting literal `USE PASSWORD = '...'` to `USE PASSWORD PROMPT;`. Overrides `SET ALLOW_PLAINTEXT_SECRETS = ON`.
- **OFF** — Follow the default save-sealing policy (default): encrypt credentials (`Password`, `API_KEY`, connection strings with credentials) rather than non-secret connection properties unless `NO_SAVE_SENSITIVE` or `NO_SAVE_CONNECTION` is `ON`.

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
