# SET NO_SAVE_SENSITIVE
Controls whether secrets in saved source are scrubbed to `<secret>` placeholders instead of being encrypted (`ENC:...`). Intended for template or shareable scripts where credentials should not stay on disk even in encrypted form.

## Syntax
```text
SET NO_SAVE_SENSITIVE [=] ON|OFF;
```

## Parameters
- **ON** — Replace credential options (`PASSWORD`, `PWD`, `API_KEY`, `APIKEY`, `TOKEN`, `ACCESS_TOKEN`, `SECRET`, `CLIENT_SECRET`, `PASSPHRASE`, `PRIVATE_KEY`), in each `CREATE CONNECTION` target the URL userinfo password (`postgres://u:<secret>@host`), every connection-string key that names a credential (`Password`, `Pwd`, `AccountKey`, `SharedAccessSignature`, `Client Secret`, `Access Token`, …), and the whole value of a connector that takes no file path (an opaque token), and `DECLARE @var SENSITIVE|ENCRYPTED = '...'` literals with `<secret>` placeholders on save (without encrypting or prompting for a save password), and rewrite literal `USE PASSWORD = '...'` to `USE PASSWORD PROMPT;`. Overrides `SET ALLOW_PLAINTEXT_SECRETS = ON`.
- **OFF** — Encrypt connection credentials (`ENC:...`) using the script/master password on save and first-run execution (default). Literal `USE PASSWORD = '...'` is rewritten to `USE PASSWORD PROMPT;` unless `SET ALLOW_PLAINTEXT_SECRETS = ON`.

## Example
```sql
-- Strip sensitive values to placeholders on save for sharing
SET NO_SAVE_SENSITIVE = ON;

DECLARE @api_token SENSITIVE = 'tok-live-123';
CREATE CONNECTION SalesDB AS MSSQL('Server=prod-sql;Database=Sales;Password=db-pass;', API_KEY='key-123');
-- On save:
--   DECLARE @api_token SENSITIVE = '<secret>';
--   CREATE CONNECTION SalesDB AS MSSQL('Server=prod-sql;Database=Sales;Password=<secret>;', API_KEY='<secret>');
```

## Notes
- `SET NO_SAVE_CONNECTION = ON` is a superset that also replaces connection hosts, databases, usernames, and all `CREATE CONNECTION` options with placeholders.
- Corresponding `appsettings.json` key: `Engine:NoSaveSensitive`.
- See also: `SET NO_SAVE_CONNECTION`, `SET CONNECTION_ENCRYPTION`, `SET ALLOW_PLAINTEXT_SECRETS`.
- Default: OFF.

## References
- [SET Commands](README.md)
