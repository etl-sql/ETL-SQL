# SET NO_SAVE_CONNECTION
Controls whether `CREATE CONNECTION` targets and quoted option values are replaced with placeholders on save. Use for source-controlled templates where hosts, usernames, databases, and credentials should be injected later.

## Syntax
```text
SET NO_SAVE_CONNECTION = ON|OFF;
```

## Parameters
- **ON** — Replace connection details with placeholders on save.
- **OFF** — Leave connection definitions as-is on save (default).

## Example
```sql
-- Template-friendly saving for source control
SET NO_SAVE_CONNECTION = ON;

CREATE CONNECTION SalesDB AS MSSQL(SERVER='prod-server', DATABASE='Sales', USER='svc', PASSWORD='secret');
-- On save, connection targets and options become placeholders
```

## Notes
- Takes precedence over `SET CONNECTION_ENCRYPTION` when both are ON.
- Corresponding `appsettings.json` key: `Engine:NoSaveConnection`.
- See also: `SET NO_SAVE_SENSITIVE`, `SET CONNECTION_ENCRYPTION`.
- Default: OFF.

## References
- [SET Commands](README.md)
