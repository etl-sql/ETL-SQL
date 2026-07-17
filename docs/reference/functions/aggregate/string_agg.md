# STRING_AGG

Concatenates string values within a group, separated by a delimiter.

## Syntax

```sql
STRING_AGG(expression, separator)
STRING_AGG(expression, separator) WITHIN GROUP (ORDER BY col [ASC|DESC])
```

## Parameters

- **expression** - String column or expression to aggregate.
- **separator** - Delimiter placed between values.

## Returns

Returns all non-NULL values joined with `separator`.

## Null Behavior

Ignores `NULL` expression values. Returns `NULL` when all expression values are `NULL`.

## Remarks

Use `WITHIN GROUP (ORDER BY ...)` when output order matters.

## Examples

```sql
SELECT STRING_AGG(name, ', ') AS all_names
FROM #team;
```

```sql
SELECT order_id, STRING_AGG(sku, ',') WITHIN GROUP (ORDER BY sku) AS items
FROM #order_lines
GROUP BY order_id;
```

## References

- [Functions](../README.md)
- [CONCAT_WS](../string/concat_ws.md)
- [LISTAGG](../aggregate/listagg.md)
