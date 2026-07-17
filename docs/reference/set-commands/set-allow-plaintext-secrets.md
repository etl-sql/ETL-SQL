# SET ALLOW_PLAINTEXT_SECRETS
Unsafe local-development escape hatch. Controls whether plaintext secrets may remain in saved source files.

## Syntax
```text
SET ALLOW_PLAINTEXT_SECRETS = ON|OFF;
```

## Parameters
- **ON** — Allow plaintext secrets to remain in saved source. A warning is emitted when the script runs.
- **OFF** — Save helpers rewrite `USE PASSWORD = 'literal'` to `USE PASSWORD PROMPT` and encrypt plaintext connection credentials when a master password is supplied (default).

## Example
```sql
-- Local development only — keep plaintext for convenience
SET ALLOW_PLAINTEXT_SECRETS = ON;
USE PASSWORD = 'dev-only';

-- Production — never allow plaintext
SET ALLOW_PLAINTEXT_SECRETS = OFF;
```

## Notes
- **Unsafe**: Only use this in local development environments. Never enable in production.
- Corresponding `appsettings.json` key: `Engine:AllowPlaintextSecrets`.
- See also: `SET NO_SAVE_SENSITIVE`, `SET NO_SAVE_CONNECTION`, `SET CONNECTION_ENCRYPTION`.
- Default: OFF.

## References
- [SET Commands](README.md)
