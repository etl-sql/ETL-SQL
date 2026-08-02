# SET WITH_PROMPT
Marks a named variable set so activating it prompts for confirmation.

## Syntax
```text
CREATE SETS !name BEGIN
  @variable = value;
  SET WITH_PROMPT ON;
END;
```

## Parameters
- **ON** — Activating the enclosing named set prompts before applying it.

## Example
```sql
CREATE SETS !Production BEGIN
  @environment = 'PRODUCTION';
  SET WITH_PROMPT ON;
END;

USE SETS !Production;
```

## Notes
- Useful in production environment sets to prevent accidental activation of destructive configurations.
- `SET WITH_PROMPT` is valid only inside `CREATE SETS`; it is not a session-level setting.

## References
- [SET Commands](README.md)
