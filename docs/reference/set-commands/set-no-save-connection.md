# SET NO_SAVE_CONNECTION
Controls whether `CREATE CONNECTION` targets and quoted option values are replaced with placeholders on save. Use for source-controlled templates where hosts, usernames, databases, and credentials should be injected later.

## Syntax
```text
SET NO_SAVE_CONNECTION [=] ON|OFF;
```

## Parameters
- **ON** — Superset of `NO_SAVE_SENSITIVE`: replace `CREATE CONNECTION` targets with `<connection>`, quoted connection options (`SERVER`, `HOST`, `DATABASE`, `USER`, `PASSWORD`, etc.) with `<placeholder>`, and `DECLARE ... SENSITIVE|ENCRYPTED` literals with `<secret>`, while rewriting literal `USE PASSWORD = '...'` to `USE PASSWORD PROMPT;`. Overrides `SET CONNECTION_ENCRYPTION` and `SET ALLOW_PLAINTEXT_SECRETS = ON`.
- **OFF** — Encrypt connection credentials (`ENC:...`) on save and first-run execution (default), leaving non-secret connection properties unencrypted unless `CONNECTION_ENCRYPTION = ON`. Literal `USE PASSWORD = '...'` is rewritten to `USE PASSWORD PROMPT;` unless `SET ALLOW_PLAINTEXT_SECRETS = ON`.

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
