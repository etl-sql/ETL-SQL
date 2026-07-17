# SET NO_SAVE_SENSITIVE
Controls whether sensitive literals are scrubbed from saved source. When enabled, rewrites `USE PASSWORD` literals to `PROMPT` and replaces SENSITIVE/ENCRYPTED literals plus credential-like options with placeholders.

## Syntax
```text
SET NO_SAVE_SENSITIVE = ON|OFF;
```

## Parameters
- **ON** — Scrub sensitive literals from saved source.
- **OFF** — Leave source as-is on save (default).

## Example
```sql
-- Enable source scrubbing before saving
SET NO_SAVE_SENSITIVE = ON;
USE PASSWORD = 'my-secret';
-- On save, the above line becomes: USE PASSWORD PROMPT;
```

## Notes
- Corresponding `appsettings.json` key: `Engine:NoSaveSensitive`.
- See also: `SET NO_SAVE_CONNECTION`, `SET CONNECTION_ENCRYPTION`, `SET ALLOW_PLAINTEXT_SECRETS`.
- Default: OFF.

## References
- [SET Commands](README.md)
