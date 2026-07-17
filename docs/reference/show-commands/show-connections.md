# SHOW CONNECTIONS
Displays all registered data sources and their current status.

## Syntax
```sql
SHOW CONNECTIONS [INTO #table];
```

## Parameters
- **INTO #table** — Optional. Captures the result set into a temp table for programmatic use.

## Returns
A result set with connection name, type, status, and configuration summary for each registered connection in the session.

## Example
```sql
-- List all active connections
SHOW CONNECTIONS;

-- Capture into a temp table and filter
SHOW CONNECTIONS INTO #conns;
SELECT Name, Type, Status FROM #conns WHERE Status = 'Open';
```

## Notes
- Connection strings and passwords are redacted in the output.
- Use `SHOW CONNECTION <conn> CONFIG` for detailed options on a specific connection.

## References
- [SHOW Commands](README.md)
