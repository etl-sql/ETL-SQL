# SET WITH_PROMPT
Controls whether SET operations marked with `WITH_PROMPT` prompt for confirmation before applying.

## Syntax
```sql
SET WITH_PROMPT = ON|OFF;
```

## Parameters
- **ON** — Activating a SET marked with `WITH_PROMPT` prompts the user for confirmation before applying.
- **OFF** — SET operations apply without prompting (default).

## Example
```sql
-- Enable confirmation prompts for safety in production
SET WITH_PROMPT = ON;

-- The following SET will now prompt before applying
SET @environment = 'PRODUCTION';
```

## Notes
- Useful in production environment sets to prevent accidental activation of destructive configurations.
- Default: OFF.

## References
- [SET Commands](README.md)
