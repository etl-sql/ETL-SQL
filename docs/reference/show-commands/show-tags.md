# SHOW TAGS
Displays lineage tags applied in the current session.

## Syntax
```sql
SHOW TAGS [INTO #table];
```

## Parameters
- **INTO #table** — Optional. Captures the result set into a temp table for programmatic use.

## Returns
A result set with tag name, value, and scope for each lineage tag in the session.

## Example
```sql
-- Apply tags to a temp table
SELECT customer_id /* @pii: true; @owner: CRM */ INTO #customers FROM src.Customers;

-- View tags
SHOW TAGS;

-- Capture and query
SHOW TAGS INTO #tags;
SELECT TagName, TagValue FROM #tags;
```

## Notes
- Shows tags set via the `TAG` statement in the current session.
- For full lineage capabilities, see the [Lineage reference](../statements/session-control/lineage.md).

## References
- [SHOW Commands](README.md)
