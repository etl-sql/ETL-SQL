# LISTAGG

Concatenates grouped values into a single string with a separator. `LISTAGG` is an alias for [`STRING_AGG`](../aggregate/string_agg.md).

## Syntax

```sql
LISTAGG(expression, separator)
```

## Parameters

- **expression** - Value to concatenate.
- **separator** - String placed between non-null values.

## Returns

Returns a `STRING`.

## Null Behavior

`NULL` values are ignored. If all values are `NULL`, the result is `NULL`.

## Remarks

- Use `LISTAGG` for SQL-standard or Oracle-style aggregate naming.
- Use [`STRING_AGG`](../aggregate/string_agg.md) for the T-SQL/Postgres-style name.

## Examples

```sql
SELECT LISTAGG(name, ', ') AS names
FROM #employees;
```

```sql
SELECT department, LISTAGG(name, ', ') AS employees
FROM #employees
GROUP BY department;
```

## References

- [Functions](../README.md)
- [STRING_AGG](../aggregate/string_agg.md)
