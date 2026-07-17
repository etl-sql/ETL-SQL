# SET WHAT_IF
Enables or disables dry-run mode. When enabled, side-effecting operations (INSERT, UPDATE, DELETE, MERGE, file writes, SEND EMAIL, Docker) are logged but not executed.

## Syntax
```text
SET WHAT_IF = ON|OFF;
```

## Parameters
- **ON** — Enable dry-run mode. Destructive operations are simulated and logged but not executed.
- **OFF** — Disable dry-run mode (default). Operations execute normally.

## Example
```sql
-- Preview what a destructive script would do
SET WHAT_IF = ON;
DELETE FROM prod.OldOrders WHERE order_date < '2020-01-01';
-- outputs: [WHAT_IF] Would delete 14,832 rows from prod.OldOrders
SET WHAT_IF = OFF;

-- Run for real after validating the output
DELETE FROM prod.OldOrders WHERE order_date < '2020-01-01';
```

## Notes
- SELECT, PRINT, DECLARE, and SET still execute normally in WHAT_IF mode.
- Recommended as a safety pattern before any destructive operation in production scripts.
- Default: OFF.

## References
- [SET Commands](README.md)
