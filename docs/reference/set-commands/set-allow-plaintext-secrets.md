# SET ALLOW_PLAINTEXT_SECRETS
Unsafe local-development escape hatch. Controls whether literal `USE PASSWORD = '...'` may remain in saved source instead of being rewritten to `USE PASSWORD PROMPT;`. Connection secrets still encrypt (`ENC:...`).

## Syntax
```text
SET ALLOW_PLAINTEXT_SECRETS [=] ON|OFF;
```

## Parameters
- **ON** — Keep literal `USE PASSWORD = '...'` on disk across saves and first-run execution (emits warnings, may be forbidden by enterprise governance policy, and does not silence security or lint checks). Connection credentials still encrypt (`ENC:...`) using that password (or prompt if no literal is present), unless `NO_SAVE_SENSITIVE` or `NO_SAVE_CONNECTION` replaces them with placeholders instead.
- **OFF** — Default zero-trust behavior. Save tools (VS Code, ETL-SQL TUI Editor, ETL-SQL Studio) and first-run CLI/engine execution automatically rewrite `USE PASSWORD = '...'` to `USE PASSWORD PROMPT;` and encrypt plaintext connection secrets (`ENC:...`), clearing editor undo history.

## Example
```sql
-- Local development only — keep USE PASSWORD on disk while still encrypting connection strings
SET ALLOW_PLAINTEXT_SECRETS = ON;
USE PASSWORD = 'dev-only';

-- Production — rewrite USE PASSWORD = '...' to USE PASSWORD PROMPT; (default)
SET ALLOW_PLAINTEXT_SECRETS = OFF;
USE PASSWORD PROMPT;
```

## Notes
- **Unsafe**: Only use `ON` in isolated local development environments when allowed by governance policy; default governance marks `Engine:AllowPlaintextSecrets` as forbidden.
- **No durable literals**: Literal `USE PASSWORD = '...'` is for initial authoring or migration only. For headless automation and production jobs, pass `--pass`, set `ETL_SQL_MASTER_PASSWORD`, or use a `SECRET:` vault reference.
- Corresponding `appsettings.json` key: `Engine:AllowPlaintextSecrets`.
- See also: `SET NO_SAVE_SENSITIVE`, `SET NO_SAVE_CONNECTION`, `SET CONNECTION_ENCRYPTION`.
- Default: OFF.

## References
- [SET Commands](README.md)
