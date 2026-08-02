# SET SHOW_SECRETS
Controls whether SENSITIVE/ENCRYPTED variable values are unmasked in `eng.variables` output. This is a display-only setting and does not affect save behavior.

## Syntax
```text
SET SHOW_SECRETS = ON|OFF;
```

## Aliases
```text
SET SHOW_PASSWORD = ON|OFF;
```

## Parameters
- **ON** — Unmask sensitive variable values in display output.
- **OFF** — Mask sensitive variable values (default).

## Example
```sql
DECLARE @apiKey SECRET = 'sk-12345';

-- Values are masked by default
SELECT * FROM eng.variables;
-- apiKey = ********

-- Unmask for debugging
SET SHOW_SECRETS ON;
SELECT * FROM eng.variables;
-- apiKey = sk-12345

SET SHOW_SECRETS OFF;
```

## Notes
- This only affects display/output behavior. It does not permit plaintext secrets to remain in saved source files.
- For controlling save-time behavior, see `SET ALLOW_PLAINTEXT_SECRETS`, `SET NO_SAVE_SENSITIVE`.
- Default: OFF.

## References
- [SET Commands](README.md)
