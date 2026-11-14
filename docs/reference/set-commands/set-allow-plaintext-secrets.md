# SET ALLOW_PLAINTEXT_SECRETS
Unsafe local-development escape hatch. Controls whether plaintext secrets may remain in saved source files.

## Syntax
```text
SET ALLOW_PLAINTEXT_SECRETS = ON|OFF;
```

## Parameters
- **ON** — Unsafe local-development escape hatch that keeps plaintext secrets and literal `USE PASSWORD = '...'` on disk (emits warnings, may be forbidden by enterprise governance policy, and does not silence security or lint checks).
- **OFF** — Default zero-trust behavior. Save tools (VS Code, ETL-SQL TUI Editor, ETL-SQL Studio) and first-run CLI/engine execution automatically rewrite `USE PASSWORD = '...'` to `USE PASSWORD PROMPT;` and encrypt plaintext secrets (`ENC:...`), clearing editor undo history.

## Example
```sql
-- Local development only — keep plaintext for convenience
SET ALLOW_PLAINTEXT_SECRETS = ON;
USE PASSWORD = 'dev-only';

-- Production — never allow plaintext (default)
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
