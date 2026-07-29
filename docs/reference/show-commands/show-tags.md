# SHOW TAGS
Displays lineage tags applied in the current session.

> [!NOTE]
> `SHOW TAGS` is a legacy row-returning command. Prefer `SELECT ... FROM eng.tags` for new scripts so tags can be filtered, joined, ordered, and captured with ordinary query syntax.

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

-- Query tags directly
SELECT TagName, TagValue
FROM eng.tags
WHERE TargetTable = '#customers';
```

## Notes
- Shows tags set via the `TAG` statement in the current session.
- `eng.tags` returns the same tag read model through the canonical engine catalog.
- For full lineage capabilities, see the [Lineage reference](../statements/session-control/lineage.md).

## References
- [SHOW Commands](README.md)
