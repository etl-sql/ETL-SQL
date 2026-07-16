# CURRENT_TIMESTAMP
Returns the current UTC date and time. Equivalent to GETDATE() / NOW().

**Category:** Date

## Syntax
```sql
CURRENT_TIMESTAMP()
```

## Returns
`DATETIME` — Current UTC datetime.

## Remarks
- `CURRENT_TIMESTAMP` (no parentheses) is also supported as a bare identifier alongside `SYSDATE`.

## Example
```sql
SELECT CURRENT_TIMESTAMP();
INSERT INTO #audit (ts) VALUES (CURRENT_TIMESTAMP());
```

## See Also
- [Standard Library — §4. Date & Time Functions](../../../../../Docs/Reference/Standard_Library.md#4-date--time-functions)
- Related: [`GETDATE`](GETDATE.md), [`NOW`](NOW.md)
